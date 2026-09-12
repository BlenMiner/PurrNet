using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    public static class PurrPackageManagerInstaller
    {
        private static readonly SemaphoreSlim OperationGate = new(1, 1);
        private const string LegacyPackagesFolderName = "PurrPackages";

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string PackagesDirectory => Path.Combine(ProjectRoot, "Packages");
        private static string LegacyPackagesDir => Path.Combine(ProjectRoot, LegacyPackagesFolderName);
        private static string ManifestPath => Path.Combine(PackagesDirectory, "manifest.json");
        private static string LockFilePath => Path.Combine(PackagesDirectory, "packages-lock.json");
        private static string EditableInstallStatePath =>
            Path.Combine(ProjectRoot, "ProjectSettings", "PurrNetEditablePackages.json");
        private const string EditableAssetsInstallPrefix = "assets:";

        // The package-manager window re-queries install state on every OnGUI repaint — once per
        // package row, plus the "Update All" count — and each query parsed manifest.json /
        // packages-lock.json from disk. Parsing a ~500-line JSON document dozens of times per frame
        // was the bulk of the window's CPU/GC cost, so the parsed forms are cached and invalidated
        // by file modification time (and explicitly after our own writes).
        private static JObject _cachedManifest;
        private static DateTime _cachedManifestMtime;
        private static JObject _cachedLockFile;
        private static DateTime _cachedLockFileMtime;
        private static JObject _cachedEditableInstallState;
        private static DateTime _cachedEditableInstallStateMtime;

        private static JObject ReadJsonCached(string path, ref JObject cache, ref DateTime cacheMtime)
        {
            if (!File.Exists(path))
            {
                cache = null;
                return null;
            }

            var mtime = File.GetLastWriteTimeUtc(path);
            if (cache != null && mtime == cacheMtime)
                return cache;

            try
            {
                cache = JObject.Parse(File.ReadAllText(path));
                cacheMtime = mtime;
            }
            catch
            {
                cache = null;
            }
            return cache;
        }

        private static JObject GetManifestJson() => ReadJsonCached(ManifestPath, ref _cachedManifest, ref _cachedManifestMtime);
        private static JObject GetLockFileJson() => ReadJsonCached(LockFilePath, ref _cachedLockFile, ref _cachedLockFileMtime);
        private static JObject GetEditableInstallState() =>
            ReadJsonCached(EditableInstallStatePath, ref _cachedEditableInstallState,
                ref _cachedEditableInstallStateMtime);

        private static void InvalidateFileCaches()
        {
            _cachedManifest = null;
            _cachedLockFile = null;
            _cachedEditableInstallState = null;
        }

        private static string FormatInstallFailure(Exception exception, PackageInfo package)
        {
            var message = exception.Message;
            if (message.IndexOf("Unity could not replace the cached package folder", StringComparison.Ordinal) >= 0)
                return message;

            if (!LooksLikePackageCacheAccessDenied(message))
                return message;

            var packageName = package?.GetUpmPackageName();
            var cachePath = string.IsNullOrEmpty(packageName)
                ? "Library/PackageCache/<package>@*"
                : $"Library/PackageCache/{packageName}@*";

            return message + "\n\n" +
                   "Unity could not replace the cached package folder. On Windows this usually means " +
                   "the current Unity session, an IDE, antivirus, or another process still has a file " +
                   "inside the old package cache open.\n\n" +
                   $"Close Unity, delete {cachePath}, then reopen the project and install again. " +
                   "Removing the package, letting Unity resolve, and then adding it back also works.";
        }

        private static string FormatCommittedResolveFailure(Exception exception, PackageInfo package, string operation)
        {
            return FormatInstallFailure(exception, package) + "\n\n" +
                   $"The package {operation} was committed safely, but Unity did not finish refreshing its package state. " +
                   "Close and reopen the project; Unity will retry resolution from the valid manifest on startup.";
        }

        private static bool LooksLikePackageCacheAccessDenied(string message)
        {
            return !string.IsNullOrEmpty(message)
                   && message.IndexOf("PackageCache", StringComparison.OrdinalIgnoreCase) >= 0
                   && message.IndexOf("access", StringComparison.OrdinalIgnoreCase) >= 0
                   && message.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static async Task<Result<bool>> ResolvePackagesWithRetry(PackageInfo package = null)
        {
            await OperationGate.WaitAsync();
            try
            {
                await ResolvePackagesCore(package);
                return Result<bool>.Ok(true);
            }
            catch (Exception e)
            {
                return Result<bool>.Fail(FormatInstallFailure(e, package));
            }
            finally
            {
                OperationGate.Release();
            }
        }

        /// <summary>
        /// Resolves a staged batch and treats all batch targets becoming visible as completion. This
        /// also handles a no-op Resolve after embedded package files auto-register before the call.
        /// </summary>
        public static async Task<Result<bool>> ResolvePackagesWithRetry(Func<bool> completionPredicate)
        {
            await OperationGate.WaitAsync();
            try
            {
                await ResolvePackagesCore(completionPredicate: completionPredicate);
                return Result<bool>.Ok(true);
            }
            catch (Exception e)
            {
                return Result<bool>.Fail(FormatInstallFailure(e, null));
            }
            finally
            {
                OperationGate.Release();
            }
        }

        private static async Task ResolvePackagesCore(PackageInfo package = null, string expectedVersion = null,
            string expectedPackageName = null, bool expectRemoved = false, bool acceptAlreadyVisible = false,
            Func<bool> completionPredicate = null)
        {
            const int maxAttempts = 3;
            const int eventTimeoutMs = 12000;
            const int baseDelayMs = 500;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var registration = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnRegistered(UnityEditor.PackageManager.PackageRegistrationEventArgs args)
                {
                    if (RegistrationMatches(args, package, expectedPackageName, expectRemoved))
                        registration.TrySetResult(true);
                }

                UnityEditor.PackageManager.Events.registeredPackages += OnRegistered;
                try
                {
                    UnityEditor.PackageManager.Client.Resolve();

                    if (completionPredicate != null)
                    {
                        const int predicatePollMs = 250;
                        for (var elapsed = 0; elapsed < eventTimeoutMs; elapsed += predicatePollMs)
                        {
                            await Task.Delay(predicatePollMs);
                            bool complete = false;
                            try
                            {
                                complete = completionPredicate();
                            }
                            catch
                            {
                                // Package state can be briefly unavailable during registration.
                            }

                            if (!complete)
                                continue;

                            await WaitForLockFileToSettle();
                            return;
                        }

                        throw new TimeoutException(
                            "Unity Package Manager did not make every staged package update visible.");
                    }

                    // Resolve has no Request object. A registration event is the only public completion
                    // signal Unity exposes. If the requested state is already visible, a short quiet
                    // period is enough because Unity intentionally emits no event for a no-op resolve.
                    var initialWait = await Task.WhenAny(registration.Task, Task.Delay(1000));
                    if (initialWait == registration.Task
                        || (expectRemoved && IsExpectedPackageStateVisible(package, null, expectedPackageName, true))
                        || (!string.IsNullOrEmpty(expectedVersion)
                            && IsExpectedPackageStateVisible(package, expectedVersion, expectedPackageName, false)))
                    {
                        await WaitForLockFileToSettle();
                        return;
                    }

                    var completed = await Task.WhenAny(registration.Task, Task.Delay(eventTimeoutMs - 1000));
                    if (completed == registration.Task
                        || (acceptAlreadyVisible
                            && IsExpectedPackageStateVisible(package, null, expectedPackageName, false)))
                    {
                        await WaitForLockFileToSettle();
                        return;
                    }

                    throw new TimeoutException(
                        "Unity Package Manager did not finish resolving packages. Its cache may be locked by Unity, an IDE, antivirus, or another Editor instance.");
                }
                catch (Exception e) when (IsTransientPackageIoFailure(e) && attempt < maxAttempts)
                {
                    Debug.LogWarning($"[PurrNet] Unity could not resolve packages because package files are busy. Retrying ({attempt + 1}/{maxAttempts})...");
                    await Task.Delay(baseDelayMs * attempt);
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException(FormatInstallFailure(e, package), e);
                }
                finally
                {
                    UnityEditor.PackageManager.Events.registeredPackages -= OnRegistered;
                }
            }
        }

        private static bool RegistrationMatches(UnityEditor.PackageManager.PackageRegistrationEventArgs args,
            PackageInfo package, string expectedPackageName, bool expectRemoved)
        {
            if (package == null)
                return true;

            var packageName = expectedPackageName ?? package.GetUpmPackageName();
            var candidates = expectRemoved ? args.removed : args.added.Concat(args.changedTo);
            foreach (var candidate in candidates)
            {
                if (!string.Equals(candidate.name, packageName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // A registration event for the requested package is the completion signal. Version
                // validation is only used for the no-event fast path because API catalog versions and
                // git tag/package.json versions are not guaranteed to use identical text.
                return true;
            }

            return false;
        }

        private static bool IsExpectedPackageStateVisible(PackageInfo package, string expectedVersion,
            string expectedPackageName, bool expectRemoved)
        {
            if (package == null)
                return false;

            var packageName = expectedPackageName ?? package.GetUpmPackageName();
            try
            {
                foreach (var registered in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
                {
                    if (!string.Equals(registered.name, packageName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (expectRemoved)
                        return false;

                    return string.IsNullOrEmpty(expectedVersion)
                           || string.Equals(registered.version, expectedVersion, StringComparison.OrdinalIgnoreCase);
                }

                return expectRemoved;
            }
            catch
            {
                // Package registration can be unavailable briefly while Unity reloads assemblies.
            }

            return false;
        }

        private static bool IsTransientPackageIoFailure(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is UnauthorizedAccessException || current is IOException || current is TimeoutException)
                    return true;
                if (LooksLikePackageCacheAccessDenied(current.Message))
                    return true;
            }

            return false;
        }

        private static async Task WaitForLockFileToSettle()
        {
            const int pollMs = 150;
            const int stableMs = 600;
            const int maximumMs = 3000;

            var elapsed = 0;
            var stableFor = 0;
            var previousWrite = File.Exists(LockFilePath) ? File.GetLastWriteTimeUtc(LockFilePath) : DateTime.MinValue;
            var previousLength = File.Exists(LockFilePath) ? new FileInfo(LockFilePath).Length : -1;

            while (elapsed < maximumMs && stableFor < stableMs)
            {
                await Task.Delay(pollMs);
                elapsed += pollMs;

                var exists = File.Exists(LockFilePath);
                var write = exists ? File.GetLastWriteTimeUtc(LockFilePath) : DateTime.MinValue;
                var length = exists ? new FileInfo(LockFilePath).Length : -1;
                if (write == previousWrite && length == previousLength)
                    stableFor += pollMs;
                else
                    stableFor = 0;

                previousWrite = write;
                previousLength = length;
            }
        }

        /// <summary>
        /// Snapshot of Packages/manifest.json and packages-lock.json taken before an install/remove
        /// touches them. If filesystem or manifest mutation throws before commit, the snapshot is
        /// restored so the project is never left with a partial package operation. Once committed,
        /// Unity resolution is allowed to recover on the next project open instead of rewriting state
        /// while Package Manager may still be active.
        /// </summary>
        private readonly struct ManifestBackup
        {
            private readonly string _manifest;
            private readonly bool _hadManifest;
            private readonly string _lock;
            private readonly bool _hadLock;

            private ManifestBackup(string manifest, bool hadManifest, string lockFile, bool hadLock)
            {
                _manifest = manifest;
                _hadManifest = hadManifest;
                _lock = lockFile;
                _hadLock = hadLock;
            }

            public static ManifestBackup Capture()
            {
                bool hadManifest = File.Exists(ManifestPath);
                bool hadLock = File.Exists(LockFilePath);
                return new ManifestBackup(
                    hadManifest ? File.ReadAllText(ManifestPath) : null, hadManifest,
                    hadLock ? File.ReadAllText(LockFilePath) : null, hadLock);
            }

            public void Restore()
            {
                try
                {
                    RestoreFile(ManifestPath, _manifest, _hadManifest);
                    RestoreFile(LockFilePath, _lock, _hadLock);
                    InvalidateFileCaches();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[PurrNet] Failed to restore manifest after a failed package operation: {e.Message}");
                }

                static void RestoreFile(string path, string contents, bool existed)
                {
                    if (!existed)
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                        return;
                    }

                    if (contents != null && (!File.Exists(path) || File.ReadAllText(path) != contents))
                        PurrPackageManagerIO.WriteAllTextAtomic(path, contents);
                }
            }
        }

        /// <summary>
        /// Moves package files aside without deleting them until the filesystem/manifest transaction
        /// commits. If a mutation fails before UPM resolution begins, the original content is moved back.
        /// </summary>
        private sealed class QuarantineScope : IDisposable
        {
            private readonly string _root = Path.Combine(ProjectRoot, "Temp", "PurrNetTransactions", Guid.NewGuid().ToString("N"));
            private readonly List<(string original, string quarantined, bool directory)> _moves = new();
            private bool _completed;

            public void MoveDirectory(string path)
            {
                if (!Directory.Exists(path))
                    return;

                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    // Unity creates Packages/<name> junctions for local/file dependencies. Moving or
                    // recursively quarantining one can operate on its target on some Windows runtimes.
                    // Unlink only the junction; the manifest snapshot is sufficient for UPM to recreate
                    // it if a pre-commit rollback is needed.
                    Directory.Delete(path, false);
                    return;
                }

                Directory.CreateDirectory(_root);
                var destination = GetDestination(path);
                Directory.Move(path, destination);
                _moves.Add((path, destination, true));
            }

            public void MoveFile(string path)
            {
                if (!File.Exists(path))
                    return;

                Directory.CreateDirectory(_root);
                var destination = GetDestination(path);
                File.Move(path, destination);
                _moves.Add((path, destination, false));
            }

            public void Commit()
            {
                if (_completed)
                    return;

                _completed = true;
                PurrPackageManagerIO.DeleteDirectoryBestEffort(_root);
            }

            public void Rollback()
            {
                if (_completed)
                    return;

                var errors = new List<string>();
                for (var i = _moves.Count - 1; i >= 0; i--)
                {
                    var move = _moves[i];
                    try
                    {
                        if (move.directory)
                        {
                            if (Directory.Exists(move.original))
                            {
                                var replacement = Path.Combine(_root, $"rollback-replacement-{i:D3}-{Guid.NewGuid():N}");
                                Directory.Move(move.original, replacement);
                            }
                            if (Directory.Exists(move.quarantined))
                                Directory.Move(move.quarantined, move.original);
                        }
                        else
                        {
                            if (File.Exists(move.original))
                            {
                                var replacement = Path.Combine(_root, $"rollback-replacement-{i:D3}-{Guid.NewGuid():N}");
                                File.Move(move.original, replacement);
                            }
                            if (File.Exists(move.quarantined))
                                File.Move(move.quarantined, move.original);
                        }
                    }
                    catch (Exception e)
                    {
                        errors.Add($"'{move.original}': {e.Message}");
                    }
                }

                _completed = true;
                if (errors.Count == 0)
                    PurrPackageManagerIO.DeleteDirectoryBestEffort(_root);
                else
                    Debug.LogError("[PurrNet] Could not fully roll back package files: " + string.Join("; ", errors));
            }

            public void Dispose()
            {
                Rollback();
            }

            private string GetDestination(string original)
            {
                var name = PurrPackageManagerIO.GetSafeFileName(Path.GetFileName(original), "package");
                return Path.Combine(_root, $"{_moves.Count:D3}_{Guid.NewGuid():N}_{name}");
            }
        }

        private static string GetEditableInstallStateKey(PackageInfo package)
        {
            return !string.IsNullOrEmpty(package?.Id)
                ? package.Id
                : package?.GetUpmPackageName();
        }

        private static JObject GetEditableInstallRecord(PackageInfo package)
        {
            var key = GetEditableInstallStateKey(package);
            if (string.IsNullOrEmpty(key))
                return null;

            return GetEditableInstallState()?["packages"]?[key] as JObject;
        }

        private static bool HasEditableImportedAssets(JObject record)
        {
            bool hasRecordedGuids = false;
            if (record?["asset_guids"] is JArray guids)
            {
                foreach (var guid in guids.Values<string>())
                {
                    if (string.IsNullOrEmpty(guid))
                        continue;

                    hasRecordedGuids = true;
                    if (!string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
                        return true;
                }
            }

            if (hasRecordedGuids)
                return false;

            if (record?["asset_paths"] is JArray paths)
            {
                foreach (var path in paths.Values<string>())
                {
                    var normalized = NormalizeImportedAssetPath(path);
                    if (normalized == null)
                        continue;

                    var fullPath = Path.Combine(
                        ProjectRoot,
                        normalized.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(fullPath) || Directory.Exists(fullPath))
                        return true;
                }
            }

            return false;
        }

        private static void RecordEditableAssetsInstall(PackageInfo package, VersionInfo version,
            IEnumerable<string> importedAssetPaths)
        {
            var key = GetEditableInstallStateKey(package);
            if (string.IsNullOrEmpty(key))
                throw new InvalidOperationException("Cannot record an editable package without an id or package name.");

            var state = GetEditableInstallState() ?? new JObject();
            if (state["packages"] is not JObject packages)
            {
                packages = new JObject();
                state["packages"] = packages;
            }

            var existing = packages[key] as JObject;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (existing?["asset_paths"] is JArray existingPaths)
            {
                foreach (var path in existingPaths.Values<string>())
                {
                    if (!string.IsNullOrEmpty(path))
                        paths.Add(path);
                }
            }

            if (existing?["asset_guids"] is JArray existingGuids)
            {
                foreach (var guid in existingGuids.Values<string>())
                {
                    if (!string.IsNullOrEmpty(guid))
                        guids.Add(guid);
                }
            }

            foreach (var importedPath in importedAssetPaths ?? Array.Empty<string>())
            {
                var assetPath = NormalizeImportedAssetPath(importedPath);
                if (assetPath == null)
                    continue;

                paths.Add(assetPath);
                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (!string.IsNullOrEmpty(guid))
                    guids.Add(guid);
            }

            packages[key] = new JObject
            {
                ["display_name"] = package.DisplayName,
                ["upm_package_name"] = package.GetUpmPackageName(),
                ["version"] = version.Version,
                ["asset_paths"] = new JArray(paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)),
                ["asset_guids"] = new JArray(guids.OrderBy(guid => guid, StringComparer.OrdinalIgnoreCase))
            };

            PurrPackageManagerIO.WriteAllTextAtomic(
                EditableInstallStatePath,
                state.ToString(Formatting.Indented));
            InvalidateFileCaches();
        }

        private static string NormalizeImportedAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var normalized = path.Replace('\\', '/').Trim();
            if (Path.IsPathRooted(normalized))
            {
                try
                {
                    normalized = Path.GetRelativePath(ProjectRoot, normalized).Replace('\\', '/');
                }
                catch
                {
                    return null;
                }
            }

            return normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase)
                   || normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                ? normalized
                : null;
        }

        private static void RemoveEditableInstallRecord(PackageInfo package)
        {
            var key = GetEditableInstallStateKey(package);
            var state = GetEditableInstallState();
            if (string.IsNullOrEmpty(key) || state?["packages"] is not JObject packages)
                return;

            packages.Remove(key);
            PurrPackageManagerIO.WriteAllTextAtomic(
                EditableInstallStatePath,
                state.ToString(Formatting.Indented));
            InvalidateFileCaches();
        }

        private static void RemoveEditableAssetsInstall(PackageInfo package)
        {
            var key = GetEditableInstallStateKey(package);
            var state = GetEditableInstallState();
            if (string.IsNullOrEmpty(key) || state?["packages"] is not JObject packages)
                return;

            var record = packages[key] as JObject;
            if (record == null)
                return;

            var protectedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var protectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in packages.Properties())
            {
                if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase)
                    || property.Value is not JObject otherRecord)
                    continue;

                if (otherRecord["asset_guids"] is JArray otherGuids)
                {
                    foreach (var guid in otherGuids.Values<string>())
                    {
                        if (!string.IsNullOrEmpty(guid))
                            protectedGuids.Add(guid);
                    }
                }

                if (otherRecord["asset_paths"] is JArray otherPaths)
                {
                    foreach (var path in otherPaths.Values<string>())
                    {
                        var normalized = NormalizeImportedAssetPath(path);
                        if (normalized != null)
                            protectedPaths.Add(normalized);
                    }
                }
            }

            var assetsToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasRecordedGuids = record["asset_guids"] is JArray { Count: > 0 };
            if (record["asset_guids"] is JArray guids)
            {
                foreach (var guid in guids.Values<string>())
                {
                    if (string.IsNullOrEmpty(guid) || protectedGuids.Contains(guid))
                        continue;

                    var currentPath = NormalizeImportedAssetPath(AssetDatabase.GUIDToAssetPath(guid));
                    if (currentPath != null && !protectedPaths.Contains(currentPath))
                        assetsToDelete.Add(currentPath);
                }
            }

            // Paths are a compatibility fallback for records created when Unity did not return
            // asset GUIDs. Prefer GUIDs so moved assets are removed at their current location
            // without risking an unrelated replacement at their original path.
            if (!hasRecordedGuids && record["asset_paths"] is JArray paths)
            {
                foreach (var path in paths.Values<string>())
                {
                    var normalized = NormalizeImportedAssetPath(path);
                    if (normalized != null && !protectedPaths.Contains(normalized))
                        assetsToDelete.Add(normalized);
                }
            }

            var failures = new List<string>();
            foreach (var assetPath in assetsToDelete.OrderByDescending(path => path.Length))
            {
                // Never recursively delete imported folders: users may have added their own files.
                if (AssetDatabase.IsValidFolder(assetPath))
                    continue;

                var fullPath = Path.Combine(ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath) && !File.Exists(fullPath + ".meta"))
                    continue;

                if (!AssetDatabase.DeleteAsset(assetPath))
                    failures.Add(assetPath);
            }

            if (failures.Count > 0)
            {
                throw new IOException(
                    "Unity could not remove these imported assets: " + string.Join(", ", failures));
            }

            RemoveEditableInstallRecord(package);
            AssetDatabase.Refresh();
        }

        private static bool IsEditableAssetsInstall(string value)
        {
            return value?.StartsWith(EditableAssetsInstallPrefix, StringComparison.OrdinalIgnoreCase) == true;
        }

        public static bool IsInstalled(PackageInfo package)
        {
            return FindInstalledEntry(package) != null;
        }

        public static string GetInstalledVersion(PackageInfo package)
        {
            var match = FindInstalledEntry(package);
            if (match == null)
                return null;

            var value = match.Value.value;
            var key = match.Value.key;

            if (IsEditableAssetsInstall(value))
                return value.Substring(EditableAssetsInstallPrefix.Length);

            // Git URL entries — prefer the version tag baked into the manifest URL (#vX.Y.Z).
            // It's written synchronously by Install() and is the source of truth, unlike
            // Library/PackageCache which Unity only updates after an async resolve — reading
            // it right after a (batch) update yields a stale @oldhash folder.
            if (IsGitUrl(value))
            {
                var tagVersion = GetVersionFromGitUrlRef(value);
                if (tagVersion != null)
                    return tagVersion;

                var resolved = GetResolvedPackageVersion(key, GetInstalledCommitHash(package));
                return resolved ?? "git";
            }

            // Parse version from the entry value
            // Format: "embedded:{name}-{version}" (current), or legacy "file:../PurrPackages/{name}-{version}.tgz" / "file:../PurrPackages/{name}-{version}"
            string nameAndVersion;
            if (value.StartsWith("embedded:"))
                nameAndVersion = value.Substring("embedded:".Length);
            else if (value.EndsWith(".tgz"))
                nameAndVersion = Path.GetFileNameWithoutExtension(value);
            else
                nameAndVersion = Path.GetFileName(value);

            if (nameAndVersion.StartsWith(key + "-"))
                return nameAndVersion[(key.Length + 1)..];
            return null;
        }

        /// <summary>
        /// Returns true if the package is currently installed via a git URL
        /// (either in manifest.json or resolved by Unity).
        /// </summary>
        public static bool IsInstalledViaGit(PackageInfo package)
        {
            var match = FindInstalledEntry(package);
            if (match == null)
                return false;
            return IsGitUrl(match.Value.value);
        }

        /// <summary>
        /// Reads the resolved commit hash from packages-lock.json for a git-installed package.
        /// Uses the actual manifest key (which may differ from GetUpmPackageName).
        /// </summary>
        public static string GetInstalledCommitHash(PackageInfo package)
        {
            var lockFile = GetLockFileJson();
            if (lockFile == null)
                return null;

            // Use the actual installed key, which may differ from apiName
            // when the package was matched by git URL scanning.
            var match = FindInstalledEntry(package);
            var lookupName = match?.key ?? package.GetUpmPackageName();

            try
            {
                var deps = lockFile["dependencies"] as JObject;
                var entry = deps?[lookupName] as JObject;
                if (entry == null)
                    return null;

                var source = entry["source"]?.ToString();
                if (source != "git")
                    return null;

                return entry["hash"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        public static bool IsPackageLockVersion(string packageName, string expectedVersion)
        {
            if (string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(expectedVersion))
                return false;

            try
            {
                var entry = GetLockFileJson()?["dependencies"]?[packageName] as JObject;
                var resolvedVersion = entry?["version"]?.ToString();
                return GitUrlsMatch(resolvedVersion, expectedVersion);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsRegisteredPackageVersion(string packageName, string expectedVersion)
        {
            if (string.IsNullOrEmpty(packageName))
                return false;

            try
            {
                foreach (var registered in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
                {
                    if (!string.Equals(registered.name, packageName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    return string.IsNullOrEmpty(expectedVersion)
                           || string.Equals(registered.version, expectedVersion,
                               StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // Registration can be temporarily unavailable during a package refresh.
            }

            return false;
        }

        /// <summary>
        /// Finds the installed entry for a package. Checks user-selected Assets imports,
        /// embedded packages in Packages/{name}/, manifest entries (git URLs), and legacy
        /// PurrPackages/ file: references.
        /// </summary>
        private static (string key, string value)? FindInstalledEntry(PackageInfo package)
        {
            var apiName = package.GetUpmPackageName();
            if (string.IsNullOrEmpty(apiName))
                return null;

            var editableRecord = GetEditableInstallRecord(package);
            var editableVersion = editableRecord?["version"]?.ToString();
            if (!string.IsNullOrEmpty(editableVersion)
                && HasEditableImportedAssets(editableRecord))
            {
                return (apiName, EditableAssetsInstallPrefix + editableVersion);
            }

            // Check for embedded package first (Packages/{name}/ takes priority in Unity)
            if (HasEmbeddedPackage(apiName))
            {
                var pkgJsonPath = Path.Combine(PackagesDirectory, apiName, "package.json");
                try
                {
                    var json = JObject.Parse(File.ReadAllText(pkgJsonPath));
                    var ver = json["version"]?.ToString() ?? "0.0.0";
                    return (apiName, $"embedded:{apiName}-{ver}");
                }
                catch
                {
                    return (apiName, $"embedded:{apiName}-0.0.0");
                }
            }

            var manifest = GetManifestJson();
            var deps = manifest?["dependencies"] as JObject;
            if (deps == null)
                return null;

            // Try direct lookup with API-provided name
            var directEntry = deps[apiName]?.ToString();
            if (directEntry != null && (directEntry.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                                        || IsGitUrl(directEntry)))
                return (apiName, directEntry);

            // API name may differ from the real name in package.json.
            // Scan legacy entries pointing to PurrPackages/ — this is safe because only our
            // installer puts tarballs there, unlike Packages/ which has unrelated packages.
            if (package.Versions != null && package.Versions.Length > 0)
            {
                foreach (var prop in deps.Properties())
                {
                    var val = prop.Value.ToString();
                    if (!val.Contains(LegacyPackagesFolderName))
                        continue;

                    // Legacy: val is "file:../PurrPackages/{key}-{version}.tgz" or "file:../PurrPackages/{key}-{version}"
                    var filename = val.EndsWith(".tgz") ? Path.GetFileNameWithoutExtension(val) : Path.GetFileName(val);
                    if (!filename.StartsWith(prop.Name + "-"))
                        continue;

                    var fileVersion = filename[(prop.Name.Length + 1)..];
                    foreach (var v in package.Versions)
                    {
                        if (v.Version == fileVersion)
                            return (prop.Name, val);
                    }
                }
            }

            // Scan manifest for git URLs matching the package's known git URLs.
            // This handles cases where the UPM name differs or the user installed
            // via Unity's Package Manager using the same repo URL.
            if (!string.IsNullOrEmpty(package.GitInstallUrlRelease) || !string.IsNullOrEmpty(package.GitInstallUrlDev))
            {
                var knownBaseUrls = new HashSet<string>();
                if (!string.IsNullOrEmpty(package.GitInstallUrlRelease))
                    knownBaseUrls.Add(GetBaseGitUrl(package.GitInstallUrlRelease));
                if (!string.IsNullOrEmpty(package.GitInstallUrlDev))
                    knownBaseUrls.Add(GetBaseGitUrl(package.GitInstallUrlDev));

                foreach (var prop in deps.Properties())
                {
                    var val = prop.Value.ToString();
                    if (!IsGitUrl(val))
                        continue;

                    if (knownBaseUrls.Contains(GetBaseGitUrl(val)))
                        return (prop.Name, val);
                }
            }

            return null;
        }

        private static bool HasEmbeddedPackage(string upmName)
        {
            if (string.IsNullOrEmpty(upmName))
                return false;
            var path = Path.Combine(PackagesDirectory, upmName);
            if (!Directory.Exists(path))
                return false;

            // Unity also creates this folder for tgz/file: installs.
            // Only consider it embedded if there's no file: reference in the manifest.
            var deps = GetManifestJson()?["dependencies"] as JObject;
            var entry = deps?[upmName]?.ToString();
            if (entry != null && (entry.StartsWith("file:") || IsGitUrl(entry)))
                return false;

            return true;
        }

        /// <summary>
        /// Clears any existing install of <paramref name="package"/> so a new version can be written.
        /// Removes manifest entries, legacy PurrPackages/ files, and embedded Packages/{name}/ folders
        /// for both the detected install key and the canonical name (needed when api name ≠ upm name,
        /// or when a rename crossed versions).
        /// </summary>
        /// <remarks>
        /// Ordering within each name: quarantine Packages/{name} BEFORE legacy targets.
        /// Packages/{name}/ may be a Unity-created junction pointing at PurrPackages/{name}-{version}/.
        /// Deleting the target first leaves the junction dangling and Directory.Exists returns false,
        /// orphaning the junction forever.
        /// </remarks>
        private static void ClearExistingInstall(PackageInfo package, string canonicalName, QuarantineScope quarantine)
        {
            var match = FindInstalledEntry(package);
            if (match != null)
                ClearByName(match.Value.key, match.Value.value.StartsWith("file:", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(canonicalName) && (match == null || match.Value.key != canonicalName))
                ClearByName(canonicalName, false);

            void ClearByName(string name, bool unlinkUnityFilePackage)
            {
                var packageDirectory = Path.Combine(PackagesDirectory, name);
                if (!unlinkUnityFilePackage)
                {
                    quarantine.MoveDirectory(packageDirectory);
                }
                // UPM exposes file: dependencies at Packages/<name> through a managed/virtual link.
                // Any System.IO move or delete against that path can be redirected to the source on
                // some Unity/Mono versions. Leave it entirely to UPM after the manifest entry changes.
                CleanupLegacyPackageFiles(name, quarantine);
                RemoveManifestEntry(name);
            }
        }

        /// <summary>
        /// Installs files from <paramref name="sourceDir"/> into Packages/{canonicalName}/ by
        /// per-file sync: unchanged files are left untouched (critical — prevents Unity from
        /// re-importing / reloading locked native DLLs that didn't actually change), new files
        /// are copied in, and files absent from the source are removed. Also clears manifest
        /// entries and legacy PurrPackages/ files for both the detected install key and the
        /// canonical name so the install is auto-discovered as embedded.
        /// </summary>
        private static void InstallFilesSynced(PackageInfo package, string canonicalName, string sourceDir,
            QuarantineScope quarantine)
        {
            var targetFolder = Path.Combine(PackagesDirectory, canonicalName);
            var match = FindInstalledEntry(package);

            CleanupLegacyPackageFiles(canonicalName, quarantine);

            // Different-name install (rename or api/upm mismatch) — can't sync across names, full wipe.
            if (match != null && !string.Equals(match.Value.key, canonicalName, StringComparison.OrdinalIgnoreCase))
            {
                quarantine.MoveDirectory(Path.Combine(PackagesDirectory, match.Value.key));
                CleanupLegacyPackageFiles(match.Value.key, quarantine);
                RemoveManifestEntry(match.Value.key);
            }

            // Unlink junction at target without following it — else sync would write into the
            // legacy PurrPackages/ target rather than replacing the junction with a real folder.
            if (Directory.Exists(targetFolder)
                && match != null
                && string.Equals(match.Value.key, canonicalName, StringComparison.OrdinalIgnoreCase)
                && match.Value.value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Unity still has the local package '{canonicalName}' mounted. Remove it first, let Unity finish resolving, then install the embedded package.");
            }
            else if (Directory.Exists(targetFolder)
                     && (File.GetAttributes(targetFolder) & FileAttributes.ReparsePoint) != 0)
            {
                quarantine.MoveDirectory(targetFolder);
            }

            RemoveManifestEntry(canonicalName);
            PurrPackageManagerIO.SyncDirectoryTransactional(sourceDir, targetFolder);
        }

        /// <summary>
        /// Installs missing dependencies depth-first, then installs the requested package and
        /// resolves once. Hidden packages are included in the API catalog but never shown in the UI.
        /// Existing dependencies are left at their installed version.
        /// </summary>
        public static async Task<Result<bool>> InstallWithDependencies(string apiKey, PackageInfo package,
            VersionInfo version, PackageInfo[] catalog, bool resolve = true,
            bool allowUserEditableDependencies = true,
            Action<string> onDownloadedPackagePrepared = null)
        {
            if (version == null)
                return Result<bool>.Fail($"'{package?.DisplayName}' has no installable version.");

            var dependencies = await InstallMissingDependencies(apiKey, package, version.Channel, catalog,
                allowUserEditableDependencies);
            if (!dependencies.Success)
                return dependencies;

            return await Install(apiKey, package, version, resolve, onDownloadedPackagePrepared);
        }

        public static async Task<Result<bool>> InstallExternalWithDependencies(string apiKey, PackageInfo package,
            string gitUrl, PackageInfo[] catalog, bool resolve = true,
            bool allowUserEditableDependencies = true)
        {
            if (string.IsNullOrEmpty(gitUrl))
                return Result<bool>.Fail($"'{package?.DisplayName}' has no install URL.");

            var channel = string.Equals(gitUrl, package?.GitInstallUrlDev, StringComparison.Ordinal)
                ? "dev"
                : "release";
            var dependencies = await InstallMissingDependencies(apiKey, package, channel, catalog,
                allowUserEditableDependencies);
            if (!dependencies.Success)
                return dependencies;

            return await InstallExternal(package, gitUrl, resolve);
        }

        private static async Task<Result<bool>> InstallMissingDependencies(string apiKey, PackageInfo root,
            string preferredChannel, PackageInfo[] catalog, bool allowUserEditableDependencies)
        {
            if (root?.DependencyIds == null || root.DependencyIds.Length == 0)
                return Result<bool>.Ok(true);
            if (catalog == null)
                return Result<bool>.Fail("The package catalog is unavailable; dependencies cannot be resolved.");

            var byId = new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in catalog)
            {
                if (item != null && !string.IsNullOrEmpty(item.Id))
                    byId[item.Id] = item;
            }

            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.Id };
            var complete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            async Task<Result<bool>> Ensure(PackageInfo owner)
            {
                if (owner.DependencyIds == null)
                    return Result<bool>.Ok(true);

                foreach (var dependencyId in owner.DependencyIds)
                {
                    if (complete.Contains(dependencyId))
                        continue;
                    if (!byId.TryGetValue(dependencyId, out var dependency))
                        return Result<bool>.Fail($"'{owner.DisplayName}' requires a package that is missing from the catalog ({dependencyId}).");
                    if (!visiting.Add(dependencyId))
                        return Result<bool>.Fail($"Circular package dependency detected at '{dependency.DisplayName}'.");

                    bool dependencyInstalled = IsInstalled(dependency);
                    if (!dependencyInstalled && dependency.IsUserEditable && !allowUserEditableDependencies)
                    {
                        return Result<bool>.Fail(
                            $"'{owner.DisplayName}' requires user-editable package '{dependency.DisplayName}'. " +
                            "Install or update that dependency individually before running Update All.");
                    }

                    var nested = await Ensure(dependency);
                    if (!nested.Success)
                        return nested;

                    if (!dependencyInstalled)
                    {
                        if (!dependency.HasAccess)
                            return Result<bool>.Fail($"'{owner.DisplayName}' requires '{dependency.DisplayName}', but your account does not have access to it.");

                        Result<bool> installed;
                        if (dependency.IsExternal)
                        {
                            var dependencyUrl = GetGitUrlForChannel(dependency, preferredChannel);
                            if (string.IsNullOrEmpty(dependencyUrl))
                                return Result<bool>.Fail($"Dependency '{dependency.DisplayName}' has no install URL.");
                            installed = await InstallExternal(dependency, dependencyUrl, false);
                        }
                        else
                        {
                            var dependencyVersion = GetLatestVersionForChannel(dependency, preferredChannel);
                            if (dependencyVersion == null)
                                return Result<bool>.Fail($"Dependency '{dependency.DisplayName}' has no installable version.");
                            installed = await Install(apiKey, dependency, dependencyVersion, false);
                        }

                        if (!installed.Success)
                            return Result<bool>.Fail($"Failed to install dependency '{dependency.DisplayName}': {installed.Error}");
                    }

                    visiting.Remove(dependencyId);
                    complete.Add(dependencyId);
                }

                return Result<bool>.Ok(true);
            }

            return await Ensure(root);
        }

        private static VersionInfo GetLatestVersionForChannel(PackageInfo package, string preferredChannel)
        {
            if (package?.Versions == null || package.Versions.Length == 0)
                return null;

            foreach (var version in package.Versions)
            {
                if (string.Equals(version.Channel, preferredChannel, StringComparison.OrdinalIgnoreCase))
                    return version;
            }

            return package.Versions[0];
        }

        private static async Task<Result<bool>> ImportEditableAssetsPackage(string packagePath,
            PackageInfo package, VersionInfo version, Action beforeRecording = null, Action afterRecording = null)
        {
            if (!string.Equals(Path.GetExtension(packagePath), ".unitypackage",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Result<bool>.Fail(
                    $"'{package.DisplayName}' is marked user-editable, but its release asset is not a .unitypackage. " +
                    "Attach a .unitypackage release asset so Unity can show the interactive file importer.");
            }

            var completion =
                new TaskCompletionSource<Result<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var importedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Unsubscribe()
            {
                AssetDatabase.importPackageCancelled -= OnCancelled;
                AssetDatabase.importPackageFailed -= OnFailed;
                AssetDatabase.onImportPackageItemsCompleted -= OnItemsCompleted;
            }

            void Complete(Result<bool> result)
            {
                Unsubscribe();
                completion.TrySetResult(result);
            }

            void OnItemsCompleted(string[] assetPaths)
            {
                try
                {
                    if (assetPaths != null)
                    {
                        foreach (var path in assetPaths)
                            importedPaths.Add(path);
                    }

                    beforeRecording?.Invoke();
                    RecordEditableAssetsInstall(package, version, importedPaths);
                    afterRecording?.Invoke();
                    Complete(Result<bool>.Ok(true));
                }
                catch (Exception e)
                {
                    Complete(Result<bool>.Fail(
                        $"Unity imported {package.DisplayName}, but its installation metadata could not be finalized: {e.Message}"));
                }
            }

            void OnCancelled(string importedPackageName)
            {
                Complete(Result<bool>.Fail("Package import cancelled by user."));
            }

            void OnFailed(string importedPackageName, string error)
            {
                Complete(Result<bool>.Fail($"Unity could not import {package.DisplayName}: {error}"));
            }

            AssetDatabase.importPackageCancelled += OnCancelled;
            AssetDatabase.importPackageFailed += OnFailed;
            AssetDatabase.onImportPackageItemsCompleted += OnItemsCompleted;

            try
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.ImportPackage(packagePath, true);
            }
            catch (Exception e)
            {
                Complete(Result<bool>.Fail($"Unity could not open the package importer: {e.Message}"));
            }

            return await completion.Task;
        }

        public static async Task<Result<bool>> Install(string apiKey, PackageInfo package, VersionInfo version,
            bool resolve = true, Action<string> onDownloadedPackagePrepared = null)
        {
            if (package == null || string.IsNullOrEmpty(package.GetUpmPackageName()))
                return Result<bool>.Fail($"'{package?.DisplayName ?? "Package"}' has no valid name from package.json. Refresh the package catalog and try again.");

            await OperationGate.WaitAsync();
            try
            {
                return await InstallCore(apiKey, package, version, resolve, onDownloadedPackagePrepared);
            }
            finally
            {
                OperationGate.Release();
            }
        }

        private static async Task<Result<bool>> InstallCore(string apiKey, PackageInfo package, VersionInfo version,
            bool resolve, Action<string> onDownloadedPackagePrepared)
        {
            var backup = ManifestBackup.Capture();
            using var quarantine = new QuarantineScope();
            string installedFolder = null;
            bool installedFolderExisted = false;
            string operationTempDirectory = null;
            var mutationCommitted = false;

            try
            {
                var existingInstall = FindInstalledEntry(package);
                if (!package.IsUserEditable
                    && existingInstall != null
                    && IsEditableAssetsInstall(existingInstall.Value.value))
                {
                    return Result<bool>.Fail(
                        $"{package.DisplayName} was previously imported into Assets. Remove it first before " +
                        "switching back to the Package Manager installation, so local customizations are not deleted without confirmation.");
                }

                // User-editable packages use the release asset and Unity's interactive importer.
                // The normal Git path is cached by UPM and read-only.
                if (!package.IsExternal && !package.IsUserEditable)
                {
                    var gitManifestValue = GetGitManifestValueForVersion(package, version);
                    if (gitManifestValue != null)
                    {
                        EditorUtility.DisplayProgressBar("PurrNet Package Manager", $"Installing {package.DisplayName}...", 0.5f);

                        var gitUpmName = package.GetUpmPackageName();

                        ClearExistingInstall(package, gitUpmName, quarantine);

                        SetManifestEntry(gitUpmName, gitManifestValue);

                        // Commit before asking UPM to resolve. Resolving can trigger an assembly/domain
                        // reload, so no in-memory rollback state may be assumed to survive this point.
                        quarantine.Commit();
                        mutationCommitted = true;

                        EditorUtility.ClearProgressBar();
                        if (resolve)
                        {
                            PurrPackageManagerCache.Invalidate();
                            await ResolvePackagesCore(package, version.Version);
                        }

                        return Result<bool>.Ok(true);
                    }
                }

                EditorUtility.DisplayProgressBar("PurrNet Package Manager", "Getting download URL...", 0.1f);

                var downloadResult = await PurrPackageManagerAPI.GetDownloadUrl(apiKey, package.Id, version.Id);
                if (!downloadResult.Success)
                {
                    EditorUtility.ClearProgressBar();
                    return Result<bool>.Fail(downloadResult.Error);
                }

                EditorUtility.DisplayProgressBar("PurrNet Package Manager", $"Downloading {package.DisplayName}...", 0.3f);

                operationTempDirectory = PurrPackageManagerIO.CreateUniqueTempDirectory("downloads");
                var downloadFilename = PurrPackageManagerIO.GetSafeFileName(
                    downloadResult.Value.Filename,
                    package.GetUpmPackageName() + ".unitypackage");
                var tempPath = PurrPackageManagerIO.GetContainedPath(operationTempDirectory, downloadFilename);

                var fileResult = await PurrPackageManagerAPI.DownloadFile(downloadResult.Value.Url, tempPath);
                if (!fileResult.Success)
                {
                    EditorUtility.ClearProgressBar();
                    return Result<bool>.Fail(fileResult.Error);
                }

                if (package.IsUserEditable)
                {
                    var previousInstall = FindInstalledEntry(package);
                    bool replaceUpmInstall = previousInstall != null
                                             && !IsEditableAssetsInstall(previousInstall.Value.value);

                    Action beforeRecording = null;
                    Action afterRecording = null;
                    if (replaceUpmInstall)
                    {
                        beforeRecording = () =>
                        {
                            ClearExistingInstall(package, package.GetUpmPackageName(), quarantine);
                            quarantine.Commit();
                        };
                        if (resolve)
                        {
                            afterRecording = () =>
                            {
                                PurrPackageManagerCache.Invalidate();
                                UnityEditor.PackageManager.Client.Resolve();
                            };
                        }
                    }

                    var importResult = await ImportEditableAssetsPackage(
                        tempPath, package, version, beforeRecording, afterRecording);
                    if (importResult.Success)
                        mutationCommitted = true;
                    return importResult;
                }

                EditorUtility.DisplayProgressBar("PurrNet Package Manager", "Installing package...", 0.7f);

                // Extract to a temp directory to read package.json
                var tempExtractDir = Path.Combine(operationTempDirectory, "extracted");

                try
                {
                    ExtractUnityPackage(tempPath, tempExtractDir);
                }
                catch (Exception extractEx)
                {
                    EditorUtility.ClearProgressBar();
                    return Result<bool>.Fail($"Failed to extract package: {extractEx.Message}");
                }

                // Read the real package name and version from package.json
                var pkgJsonPath = Path.Combine(tempExtractDir, "package.json");
                if (!File.Exists(pkgJsonPath))
                {
                    EditorUtility.ClearProgressBar();
                    return Result<bool>.Fail("Extracted package does not contain a package.json");
                }

                var pkgJson = JObject.Parse(await File.ReadAllTextAsync(pkgJsonPath));
                var upmName = pkgJson["name"]?.ToString();
                var upmVersion = pkgJson["version"]?.ToString();

                if (string.IsNullOrEmpty(upmName) || string.IsNullOrEmpty(upmVersion))
                {
                    EditorUtility.ClearProgressBar();
                    return Result<bool>.Fail("package.json is missing 'name' or 'version' field");
                }

                // The catalog version can differ from package.json. Let resumable callers persist
                // the exact target while failure is still side-effect free, before any package or
                // manifest mutation can trigger an assembly reload.
                onDownloadedPackagePrepared?.Invoke(upmVersion);

                // Remove embedded packages if they exist (Unity prioritizes Packages/{name}/ over manifest)
                var apiName = package.GetUpmPackageName();
                if (HasEmbeddedPackage(apiName) || HasEmbeddedPackage(upmName))
                {
                    EditorUtility.ClearProgressBar();
                    if (!EditorUtility.DisplayDialog("Embedded Package Found",
                        $"An embedded copy of {package.DisplayName} exists in the Packages folder. " +
                        "It will be updated transactionally. Any local changes will be replaced.",
                        "Update & Continue", "Cancel"))
                    {
                        return Result<bool>.Fail("Installation cancelled by user.");
                    }
                    EditorUtility.DisplayProgressBar("PurrNet Package Manager", "Removing embedded package...", 0.7f);
                }

                installedFolder = Path.Combine(PackagesDirectory, upmName);
                installedFolderExisted = Directory.Exists(installedFolder);

                InstallFilesSynced(package, upmName, tempExtractDir, quarantine);

                // The embedded package is now internally consistent. Commit before Resolve because
                // package registration can reload this editor assembly and abandon async continuations.
                quarantine.Commit();
                mutationCommitted = true;

                EditorUtility.DisplayProgressBar("PurrNet Package Manager", "Cleaning up...", 0.9f);

                EditorUtility.ClearProgressBar();
                if (resolve)
                {
                    PurrPackageManagerCache.Invalidate();
                    await ResolvePackagesCore(package, upmVersion, upmName);
                }

                return Result<bool>.Ok(true);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PurrNet] Install failed: {e}");
                EditorUtility.ClearProgressBar();
                var message = mutationCommitted
                    ? FormatCommittedResolveFailure(e, package, "change")
                    : FormatInstallFailure(e, package);

                // Roll back: a partial install must not leave the project worse off than before.
                // Remove any half-written embedded folder we created, then restore manifest/lock.
                if (!mutationCommitted)
                {
                    if (installedFolder != null && !installedFolderExisted && Directory.Exists(installedFolder))
                        PurrPackageManagerIO.DeleteDirectoryBestEffort(installedFolder);
                    quarantine.Rollback();
                    backup.Restore();
                }

                return Result<bool>.Fail(message);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                PurrPackageManagerIO.DeleteDirectoryBestEffort(operationTempDirectory);
            }
        }

        public static Task<Result<bool>> Remove(PackageInfo package)
        {
            return Remove(package, true);
        }

        internal static async Task<Result<bool>> Remove(PackageInfo package, bool askForConfirmation)
        {
            await OperationGate.WaitAsync();
            try
            {
                var match = FindInstalledEntry(package);
                if (match == null)
                    return Result<bool>.Ok(false);

                if (askForConfirmation)
                {
                    string removeMessage = IsEditableAssetsInstall(match.Value.value)
                        ? $"Are you sure you want to remove {package.DisplayName}? " +
                          "Assets selected in Unity's package importer, including local modifications to them, " +
                          "will be deleted. Additional files you created are left in place."
                        : $"Are you sure you want to remove {package.DisplayName}?";
                    if (!EditorUtility.DisplayDialog("Remove Package", removeMessage, "Remove", "Cancel"))
                        return Result<bool>.Ok(false);
                }

                if (IsEditableAssetsInstall(match.Value.value))
                {
                    try
                    {
                        RemoveEditableAssetsInstall(package);
                        PurrPackageManagerCache.Invalidate();
                        return Result<bool>.Ok(true);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[PurrNet] Failed to remove imported package assets: {e}");
                        return Result<bool>.Fail(e.Message);
                    }
                }

                var backup = ManifestBackup.Capture();
                using var quarantine = new QuarantineScope();
                var mutationCommitted = false;
                try
                {
                    ClearExistingInstall(package, package.GetUpmPackageName(), quarantine);

                    quarantine.Commit();
                    mutationCommitted = true;

                    PurrPackageManagerCache.Invalidate();
                    await ResolvePackagesCore(package, expectedPackageName: match.Value.key, expectRemoved: true);
                    return Result<bool>.Ok(true);
                }
                catch (Exception e)
                {
                    if (!mutationCommitted)
                    {
                        quarantine.Rollback();
                        backup.Restore();
                    }
                    var message = mutationCommitted
                        ? FormatCommittedResolveFailure(e, package, "removal")
                        : FormatInstallFailure(e, package);
                    Debug.LogError($"[PurrNet] Failed to remove package: {e}");
                    return Result<bool>.Fail(message);
                }
            }
            finally
            {
                OperationGate.Release();
            }
        }

        public static async Task<Result<bool>> InstallExternal(PackageInfo package, string gitUrl, bool resolve = true)
        {
            if (package == null || string.IsNullOrEmpty(package.GetUpmPackageName()))
                return Result<bool>.Fail($"'{package?.DisplayName ?? "Package"}' has no valid name from package.json. Refresh the package catalog and try again.");

            await OperationGate.WaitAsync();
            try
            {
                var backup = ManifestBackup.Capture();
                using var quarantine = new QuarantineScope();
                var mutationCommitted = false;
                try
                {
                    EditorUtility.DisplayProgressBar("PurrNet Package Manager", $"Installing {package.DisplayName}...", 0.5f);

                    var upmName = package.GetUpmPackageName();
                    var existing = FindInstalledEntry(package);
                    bool acceptAlreadyVisible = existing != null
                                                && string.Equals(existing.Value.key, upmName,
                                                    StringComparison.OrdinalIgnoreCase)
                                                && string.Equals(existing.Value.value, gitUrl,
                                                    StringComparison.Ordinal)
                                                && IsExpectedPackageStateVisible(package, null, upmName, false);

                    ClearExistingInstall(package, upmName, quarantine);

                    SetManifestEntry(upmName, gitUrl);

                    quarantine.Commit();
                    mutationCommitted = true;

                    // Resolving a code package can compile scripts and reload this editor assembly,
                    // abandoning the async continuation (including the finally block below). Clear
                    // Unity's native progress dialog before crossing that reload boundary.
                    EditorUtility.ClearProgressBar();

                    if (resolve)
                    {
                        PurrPackageManagerCache.Invalidate();
                        // Resolve emits no registration event when the identical dependency is already
                        // current. Only allow the visible-by-name fallback when this operation restored
                        // the exact manifest reference that was registered before the mutation.
                        await ResolvePackagesCore(package, expectedPackageName: upmName,
                            acceptAlreadyVisible: acceptAlreadyVisible);
                    }

                    return Result<bool>.Ok(true);
                }
                catch (Exception e)
                {
                    if (!mutationCommitted)
                    {
                        quarantine.Rollback();
                        backup.Restore();
                    }
                    var message = mutationCommitted
                        ? FormatCommittedResolveFailure(e, package, "installation")
                        : FormatInstallFailure(e, package);
                    Debug.LogError($"[PurrNet] Failed to install external package: {e}");
                    return Result<bool>.Fail(message);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                OperationGate.Release();
            }
        }

        public static string GetInstalledGitChannel(PackageInfo package)
        {
            var match = FindInstalledEntry(package);
            if (match == null) return "release";
            var value = match.Value.value;
            return ClassifyInstalledGitChannel(package, value, GetInstalledCommitHash(package));
        }

        internal static string ClassifyInstalledGitChannel(PackageInfo package, string installedValue,
            string installedHash)
        {
            if (package == null || !IsGitUrl(installedValue))
                return "release";

            if (GitUrlsMatch(installedValue, package.GitInstallUrlDev))
                return "dev";
            if (GitUrlsMatch(installedValue, package.GitInstallUrlRelease))
                return "release";

            // Equivalent/manual URLs can differ in superficial spelling. The resolved commit is a
            // stronger channel signal when the API supplies hashes for both channels.
            bool matchesDev = HashesMatch(installedHash, package.LatestCommitDev);
            bool matchesRelease = HashesMatch(installedHash, package.LatestCommitRelease);
            if (matchesDev && !matchesRelease)
                return "dev";
            if (matchesRelease && !matchesDev)
                return "release";

            var installedBase = GetBaseGitUrl(installedValue);
            bool baseMatchesDev = !string.IsNullOrEmpty(package.GitInstallUrlDev)
                                  && string.Equals(installedBase, GetBaseGitUrl(package.GitInstallUrlDev),
                                      StringComparison.OrdinalIgnoreCase);
            bool baseMatchesRelease = !string.IsNullOrEmpty(package.GitInstallUrlRelease)
                                      && string.Equals(installedBase,
                                          GetBaseGitUrl(package.GitInstallUrlRelease),
                                          StringComparison.OrdinalIgnoreCase);
            if (baseMatchesDev && !baseMatchesRelease)
                return "dev";
            if (baseMatchesRelease && !baseMatchesDev)
                return "release";

            // A manual/older pin can share a repository base with both channels. Do not guess and
            // silently switch that installation to release during Update All.
            return null;
        }

        internal static string GetGitManifestValueForVersion(PackageInfo package, VersionInfo version)
        {
            if (package == null || version == null || package.IsExternal || package.IsUserEditable
                || string.IsNullOrEmpty(version.TagName))
                return null;

            var gitUrl = GetGitUrlForChannel(package, version.Channel);
            return string.IsNullOrEmpty(gitUrl) ? null : StripGitRef(gitUrl) + "#" + version.TagName;
        }

        private static bool GitUrlsMatch(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;

            static string Normalize(string value)
            {
                value = value.Trim();
                if (value.StartsWith("git+", StringComparison.OrdinalIgnoreCase))
                    value = value.Substring("git+".Length);

                int queryIndex = value.IndexOf('?');
                int fragmentIndex = value.IndexOf('#');
                int suffixIndex = queryIndex < 0
                    ? fragmentIndex
                    : fragmentIndex < 0 ? queryIndex : Math.Min(queryIndex, fragmentIndex);
                if (suffixIndex < 0)
                    return value.TrimEnd('/');

                return value.Substring(0, suffixIndex).TrimEnd('/') + value.Substring(suffixIndex);
            }

            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        private static bool HashesMatch(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;
            int minLength = Math.Min(a.Length, b.Length);
            return string.Compare(a, 0, b, 0, minLength, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static bool IsGitUrl(string value)
        {
            return value != null &&
                   (value.StartsWith("https://") || value.StartsWith("git://") || value.StartsWith("git+"));
        }

        /// <summary>
        /// Strips the #fragment from a git URL but preserves ?path= query parameters.
        /// Used when constructing git+tag manifest entries.
        /// </summary>
        private static string StripGitRef(string gitUrl)
        {
            if (gitUrl == null) return "";
            var hashIdx = gitUrl.IndexOf('#');
            return hashIdx >= 0 ? gitUrl.Substring(0, hashIdx) : gitUrl;
        }

        /// <summary>
        /// Picks the appropriate git install URL for a given channel,
        /// falling back to the other channel if the preferred one is null.
        /// </summary>
        private static string GetGitUrlForChannel(PackageInfo package, string channel)
        {
            if (string.Equals(channel, "dev", StringComparison.OrdinalIgnoreCase))
                return package.GitInstallUrlDev ?? package.GitInstallUrlRelease;
            return package.GitInstallUrlRelease ?? package.GitInstallUrlDev;
        }

        /// <summary>
        /// Cleans up old package files in the legacy PurrPackages/ directory for a given UPM name.
        /// Handles both legacy .tgz files and folder-based installs.
        /// Files are quarantined until the surrounding package operation commits.
        /// </summary>
        private static void CleanupLegacyPackageFiles(string upmName, QuarantineScope quarantine)
        {
            if (!Directory.Exists(LegacyPackagesDir)) return;

            // Old tgz files
            foreach (var f in Directory.GetFiles(LegacyPackagesDir, upmName + "-*.tgz"))
                quarantine.MoveFile(f);

            // Folder installs
            foreach (var d in Directory.GetDirectories(LegacyPackagesDir, upmName + "-*"))
                quarantine.MoveDirectory(d);
        }

        /// <summary>
        /// Strips the fragment (#branch/tag/commit) and query string from a git URL
        /// so that URLs pointing to the same repo can be compared regardless of ref.
        /// </summary>
        private static string GetBaseGitUrl(string gitUrl)
        {
            if (gitUrl == null) return "";
            var hashIdx = gitUrl.IndexOf('#');
            if (hashIdx >= 0)
                gitUrl = gitUrl.Substring(0, hashIdx);
            var queryIdx = gitUrl.IndexOf('?');
            if (queryIdx >= 0)
                gitUrl = gitUrl.Substring(0, queryIdx);
            return gitUrl.TrimEnd('/');
        }

        /// <summary>
        /// Extracts a semver version from the #fragment of a git URL (e.g. "...#v1.2.3" -> "1.2.3").
        /// Returns null when the ref is a branch name, commit hash, or otherwise not a version tag.
        /// </summary>
        private static string GetVersionFromGitUrlRef(string gitUrl)
        {
            if (string.IsNullOrEmpty(gitUrl))
                return null;
            var hashIdx = gitUrl.IndexOf('#');
            if (hashIdx < 0 || hashIdx == gitUrl.Length - 1)
                return null;

            var refName = gitUrl.Substring(hashIdx + 1);
            // Tags are conventionally "v{semver}"; tolerate a bare semver too.
            if (refName.Length > 1 && (refName[0] == 'v' || refName[0] == 'V') && char.IsDigit(refName[1]))
                refName = refName.Substring(1);

            // Looks like a version only if it starts with a digit and contains a dot.
            return refName.Length > 0 && char.IsDigit(refName[0]) && refName.Contains('.') ? refName : null;
        }

        /// <summary>
        /// Reads the actual semver version from the resolved package in Library/PackageCache.
        /// When several "<name>@<hash>" folders exist (a stale one lingering after a re-resolve),
        /// prefers the one matching <paramref name="preferredHash"/>, then the most recently written.
        /// </summary>
        private static string GetResolvedPackageVersion(string packageName, string preferredHash = null)
        {
            var cacheDir = Path.Combine(ProjectRoot, "Library", "PackageCache");
            if (!Directory.Exists(cacheDir))
                return null;

            try
            {
                var dirs = Directory.GetDirectories(cacheDir, packageName + "@*");
                if (dirs.Length == 0)
                    return null;

                string chosen = null;
                if (!string.IsNullOrEmpty(preferredHash))
                {
                    foreach (var d in dirs)
                    {
                        var at = Path.GetFileName(d);
                        var atIdx = at.IndexOf('@');
                        if (atIdx >= 0 && string.Equals(at.Substring(atIdx + 1), preferredHash, StringComparison.OrdinalIgnoreCase))
                        {
                            chosen = d;
                            break;
                        }
                    }
                }

                if (chosen == null)
                {
                    chosen = dirs[0];
                    var newest = Directory.GetLastWriteTimeUtc(chosen);
                    for (int i = 1; i < dirs.Length; i++)
                    {
                        var t = Directory.GetLastWriteTimeUtc(dirs[i]);
                        if (t > newest) { newest = t; chosen = dirs[i]; }
                    }
                }

                var pkgJsonPath = Path.Combine(chosen, "package.json");
                if (!File.Exists(pkgJsonPath))
                    return null;

                var json = JObject.Parse(File.ReadAllText(pkgJsonPath));
                return json["version"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static void SetManifestEntry(string packageName, string value)
        {
            try
            {
                var manifest = JObject.Parse(File.ReadAllText(ManifestPath));
                var deps = manifest["dependencies"] as JObject;
                if (deps == null)
                {
                    deps = new JObject();
                    manifest["dependencies"] = deps;
                }
                deps[packageName] = value;
                PurrPackageManagerIO.WriteAllTextAtomic(ManifestPath, manifest.ToString(Formatting.Indented));
                InvalidateFileCaches();
            }
            catch (Exception e)
            {
                Debug.LogError($"[PurrNet] Failed to update manifest.json: {e.Message}");
                throw;
            }
        }

        private static void RemoveManifestEntry(string packageName)
        {
            try
            {
                var manifest = JObject.Parse(File.ReadAllText(ManifestPath));
                var deps = manifest["dependencies"] as JObject;
                if (deps != null && deps.ContainsKey(packageName))
                {
                    deps.Remove(packageName);
                    PurrPackageManagerIO.WriteAllTextAtomic(ManifestPath, manifest.ToString(Formatting.Indented));
                    InvalidateFileCaches();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PurrNet] Failed to update manifest.json: {e.Message}");
                throw;
            }
        }

        private static void ExtractUnityPackage(string packagePath, string targetDir)
        {
            // .unitypackage = gzipped tar
            // Each asset is a folder named by GUID containing:
            //   pathname  - the original asset path
            //   asset     - the file content
            //   asset.meta - the .meta file content

            var entries = new Dictionary<string, PackageEntry>();
            string longName = null;

            using (var fileStream = File.OpenRead(packagePath))
            using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
            using (var memStream = new MemoryStream())
            {
                gzipStream.CopyTo(memStream);
                var tarBytes = memStream.ToArray();

                int pos = 0;
                while (pos + 512 <= tarBytes.Length)
                {
                    // Check for zero block (end of archive)
                    bool allZero = true;
                    for (int i = 0; i < 512; i++)
                    {
                        if (tarBytes[pos + i] != 0) { allZero = false; break; }
                    }
                    if (allZero) break;

                    // Parse tar header
                    string tarName = Encoding.ASCII.GetString(tarBytes, pos, 100).TrimEnd('\0');
                    string sizeStr = Encoding.ASCII.GetString(tarBytes, pos + 124, 12).Trim('\0', ' ');
                    long size = sizeStr.Length > 0 ? Convert.ToInt64(sizeStr, 8) : 0;
                    char typeFlag = (char)tarBytes[pos + 156];

                    // ustar prefix field (offset 345, 155 bytes)
                    string tarPrefix = Encoding.ASCII.GetString(tarBytes, pos + 345, 155).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(tarPrefix))
                        tarName = tarPrefix + "/" + tarName;

                    pos += 512;

                    byte[] content = null;
                    if (size > 0)
                    {
                        if (size > int.MaxValue || pos + size > tarBytes.Length)
                            throw new InvalidDataException($"Invalid tar entry size for '{tarName}'.");

                        var paddedSize = (size + 511) / 512 * 512;
                        if (pos + paddedSize > tarBytes.Length)
                            throw new InvalidDataException($"Truncated tar entry '{tarName}'.");

                        content = new byte[(int)size];
                        Array.Copy(tarBytes, pos, content, 0, (int)size);
                        pos += (int)paddedSize;
                    }

                    // Handle GNU long name extension
                    if (typeFlag == 'L')
                    {
                        longName = content != null ? Encoding.ASCII.GetString(content).TrimEnd('\0') : null;
                        continue;
                    }

                    // Use long name if set by previous ././@LongLink entry
                    if (longName != null)
                    {
                        tarName = longName;
                        longName = null;
                    }

                    // Skip pax extended headers
                    if (typeFlag == 'x' || typeFlag == 'g')
                        continue;

                    // Skip directories
                    if (typeFlag == '5')
                        continue;

                    // Strip leading "./"
                    if (tarName.StartsWith("./"))
                        tarName = tarName.Substring(2);

                    // Strip trailing "/"
                    tarName = tarName.TrimEnd('/');

                    // Entries are "{guid}/{type}" where type is pathname, asset, or asset.meta
                    var slashIdx = tarName.IndexOf('/');
                    if (slashIdx < 0)
                        continue;

                    string guid = tarName.Substring(0, slashIdx);
                    string entryName = tarName.Substring(slashIdx + 1);

                    if (!entries.TryGetValue(guid, out var entry))
                    {
                        entry = new PackageEntry();
                        entries[guid] = entry;
                    }

                    if (entryName == "pathname" && content != null)
                        entry.Pathname = Encoding.UTF8.GetString(content).Trim();
                    else if (entryName == "asset")
                        entry.AssetContent = content;
                    else if (entryName == "asset.meta")
                        entry.MetaContent = content;
                }
            }

            // Find the root prefix by locating the shallowest package.json
            string rootPrefix = null;
            foreach (var entry in entries.Values)
            {
                if (entry.Pathname == null)
                    continue;

                var fn = entry.Pathname;
                // Normalize slashes
                fn = fn.Replace('\\', '/');
                entry.Pathname = fn;

                if (fn.EndsWith("/package.json") || fn == "package.json")
                {
                    var prefix = fn.Substring(0, fn.Length - "package.json".Length);
                    if (rootPrefix == null || prefix.Length < rootPrefix.Length)
                        rootPrefix = prefix;
                }
            }

            // Fallback: find the shortest common directory prefix
            if (rootPrefix == null)
            {
                foreach (var entry in entries.Values)
                {
                    if (entry.Pathname == null)
                        continue;
                    var lastSlash = entry.Pathname.LastIndexOf('/');
                    var dir = lastSlash >= 0 ? entry.Pathname.Substring(0, lastSlash + 1) : "";
                    if (rootPrefix == null || dir.Length < rootPrefix.Length)
                        rootPrefix = dir;
                }
            }

            rootPrefix ??= "";

            // Write files to target directory
            Directory.CreateDirectory(targetDir);
            int fileCount = 0;

            foreach (var entry in entries.Values)
            {
                if (entry.Pathname == null)
                    continue;

                // Skip entries that are parent directories of the root prefix
                // e.g., "Assets" or "Assets/SomePlugin" when rootPrefix is "Assets/SomePlugin/"
                if (rootPrefix.Length > 0 && rootPrefix.StartsWith(entry.Pathname + "/"))
                    continue;

                // Strip root prefix
                string relativePath = entry.Pathname;
                if (rootPrefix.Length > 0 && relativePath.StartsWith(rootPrefix))
                    relativePath = relativePath.Substring(rootPrefix.Length);

                if (string.IsNullOrEmpty(relativePath))
                    continue;

                // Write asset content
                if (entry.AssetContent != null)
                {
                    var fullPath = PurrPackageManagerIO.GetContainedPath(targetDir, relativePath);
                    var dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllBytes(fullPath, entry.AssetContent);
                    fileCount++;
                }

                // Write .meta file
                if (entry.MetaContent != null)
                {
                    var metaPath = PurrPackageManagerIO.GetContainedPath(targetDir, relativePath + ".meta");
                    var dir = Path.GetDirectoryName(metaPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllBytes(metaPath, entry.MetaContent);
                }
            }

            if (fileCount == 0)
                Debug.LogWarning("[PurrNet] Package extraction produced no files.");
        }

        private class PackageEntry
        {
            public string Pathname;
            public byte[] AssetContent;
            public byte[] MetaContent;
        }
    }
}
