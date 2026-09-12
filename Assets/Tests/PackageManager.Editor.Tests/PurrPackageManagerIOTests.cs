using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace PurrNet.Editor.Tests
{
    public class PurrPackageManagerIOTests
    {
        private string _testRoot;

        [SetUp]
        public void SetUp()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "PurrNetIOTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (!Directory.Exists(_testRoot))
                return;

            foreach (var file in Directory.GetFiles(_testRoot, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_testRoot, true);
        }

        [Test]
        public void GetContainedPath_AllowsNestedChild()
        {
            var result = PurrPackageManagerIO.GetContainedPath(_testRoot, "Runtime/Package.cs");

            Assert.That(result, Is.EqualTo(Path.GetFullPath(Path.Combine(_testRoot, "Runtime", "Package.cs"))));
        }

        [TestCase("../outside.txt")]
        [TestCase("nested/../../outside.txt")]
        public void GetContainedPath_RejectsTraversal(string path)
        {
            Assert.Throws<InvalidDataException>(() => PurrPackageManagerIO.GetContainedPath(_testRoot, path));
        }

        [Test]
        public void GetContainedPath_RejectsRootedPath()
        {
            var rooted = Path.Combine(Path.GetPathRoot(_testRoot) ?? Path.DirectorySeparatorChar.ToString(), "outside.txt");
            Assert.Throws<InvalidDataException>(() => PurrPackageManagerIO.GetContainedPath(_testRoot, rooted));
        }

        [Test]
        public void GetSafeFileName_RemovesDirectories()
        {
            var unsafeName = Path.Combine("..", "downloads", "package.unitypackage");
            Assert.That(PurrPackageManagerIO.GetSafeFileName(unsafeName, "fallback.unitypackage"),
                Is.EqualTo("package.unitypackage"));
            Assert.That(PurrPackageManagerIO.GetSafeFileName(@"..\downloads\package.unitypackage", "fallback.unitypackage"),
                Is.EqualTo("package.unitypackage"));
        }

        [Test]
        public void PackageInfo_UserEditableDefaultsToFalse()
        {
            var package = JsonConvert.DeserializeObject<PackageInfo>("{}");

            Assert.That(package.IsUserEditable, Is.False);
        }

        [Test]
        public void PackageInfo_UserEditableDeserializesFromCatalog()
        {
            var package = JsonConvert.DeserializeObject<PackageInfo>("{\"is_user_editable\":true}");

            Assert.That(package.IsUserEditable, Is.True);
        }

        [Test]
        public void PackageUpdateBatchState_JsonRoundTripPreservesRecoveryState()
        {
            var state = new PackageUpdateBatchState
            {
                NextIndex = 1,
                Phase = PackageUpdateBatchPhase.Resolving,
                ResolveRequired = true,
                ResolveStarted = true,
                Errors = new List<string> { "download failed" },
                Items = new List<PackageUpdateBatchItem>
                {
                    new()
                    {
                        PackageId = "package-id",
                        DisplayName = "Package",
                        ExpectedCommit = "abcdef",
                        ExpectedLockVersion = "https://example.test/package.git#v2.0.0",
                        DependencyIds = new[] { "dependency-id" },
                        InstallStarted = true,
                        Failed = true
                    }
                }
            };

            var restored = JsonUtility.FromJson<PackageUpdateBatchState>(JsonUtility.ToJson(state));

            Assert.That(restored.NextIndex, Is.EqualTo(1));
            Assert.That(restored.Phase, Is.EqualTo(PackageUpdateBatchPhase.Resolving));
            Assert.That(restored.ResolveRequired, Is.True);
            Assert.That(restored.ResolveStarted, Is.True);
            Assert.That(restored.Errors, Is.EqualTo(new[] { "download failed" }));
            Assert.That(restored.Items[0].PackageId, Is.EqualTo("package-id"));
            Assert.That(restored.Items[0].ExpectedCommit, Is.EqualTo("abcdef"));
            Assert.That(restored.Items[0].ExpectedLockVersion,
                Is.EqualTo("https://example.test/package.git#v2.0.0"));
            Assert.That(restored.Items[0].DependencyIds, Is.EqualTo(new[] { "dependency-id" }));
            Assert.That(restored.Items[0].InstallStarted, Is.True);
            Assert.That(restored.Items[0].Failed, Is.True);
        }

        [Test]
        public void ClassifyInstalledGitChannel_AmbiguousSameRepositoryPinReturnsUnknown()
        {
            var package = JsonConvert.DeserializeObject<PackageInfo>(
                "{\"git_install_url_release\":\"https://example.test/package.git#release\"," +
                "\"git_install_url_dev\":\"https://example.test/package.git#dev\"," +
                "\"latest_commit_release\":\"aaaaaaaa\"," +
                "\"latest_commit_dev\":\"bbbbbbbb\"}");

            var channel = PurrPackageManagerInstaller.ClassifyInstalledGitChannel(
                package, "https://example.test/package.git#old-manual-pin", "cccccccc");

            Assert.That(channel, Is.Null);
        }

        [Test]
        public void WriteAllTextAtomic_ReadOnlyDestinationPreservesOriginal()
        {
            var path = Path.Combine(_testRoot, "manifest.json");
            File.WriteAllText(path, "old");
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

            Assert.Throws<UnauthorizedAccessException>(() => PurrPackageManagerIO.WriteAllTextAtomic(path, "new"));
            Assert.That(File.ReadAllText(path), Is.EqualTo("old"));
        }

        [Test]
        public void SyncDirectoryTransactional_UpdatesAndRemovesFiles()
        {
            var source = Path.Combine(_testRoot, "source");
            var destination = Path.Combine(_testRoot, "destination");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(source, "kept.txt"), "new");
            File.WriteAllText(Path.Combine(source, "added.txt"), "added");
            File.WriteAllText(Path.Combine(destination, "kept.txt"), "old");
            File.WriteAllText(Path.Combine(destination, "removed.txt"), "removed");

            PurrPackageManagerIO.SyncDirectoryTransactional(source, destination);

            Assert.That(File.ReadAllText(Path.Combine(destination, "kept.txt")), Is.EqualTo("new"));
            Assert.That(File.ReadAllText(Path.Combine(destination, "added.txt")), Is.EqualTo("added"));
            Assert.That(File.Exists(Path.Combine(destination, "removed.txt")), Is.False);
        }

        [Test]
        public void SyncDirectoryTransactional_ReadOnlyFileLeavesDestinationUntouched()
        {
            var source = Path.Combine(_testRoot, "source");
            var destination = Path.Combine(_testRoot, "destination");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(destination);
            var blockedPath = Path.Combine(destination, "blocked.txt");
            File.WriteAllText(Path.Combine(source, "blocked.txt"), "new-blocked");
            File.WriteAllText(Path.Combine(source, "other.txt"), "new-other");
            File.WriteAllText(blockedPath, "old-blocked");
            File.WriteAllText(Path.Combine(destination, "other.txt"), "old-other");
            File.SetAttributes(blockedPath, File.GetAttributes(blockedPath) | FileAttributes.ReadOnly);

            Assert.Throws<IOException>(() => PurrPackageManagerIO.SyncDirectoryTransactional(source, destination));
            Assert.That(File.ReadAllText(blockedPath), Is.EqualTo("old-blocked"));
            Assert.That(File.ReadAllText(Path.Combine(destination, "other.txt")), Is.EqualTo("old-other"));
        }

        [Test]
        public void SyncDirectoryTransactional_OpenHandleLeavesDestinationUntouched()
        {
            var source = Path.Combine(_testRoot, "source");
            var destination = Path.Combine(_testRoot, "destination");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(destination);
            var blockedPath = Path.Combine(destination, "blocked.dll");
            File.WriteAllText(Path.Combine(source, "blocked.dll"), "new-blocked");
            File.WriteAllText(Path.Combine(source, "other.txt"), "new-other");
            File.WriteAllText(blockedPath, "old-blocked");
            File.WriteAllText(Path.Combine(destination, "other.txt"), "old-other");

            using (new FileStream(blockedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Throws<IOException>(() => PurrPackageManagerIO.SyncDirectoryTransactional(source, destination));
                Assert.That(File.ReadAllText(blockedPath), Is.EqualTo("old-blocked"));
                Assert.That(File.ReadAllText(Path.Combine(destination, "other.txt")), Is.EqualTo("old-other"));
            }
        }

        [Test]
        public async Task PackageUpdateBatchRunner_StagesMixedItemsBeforeSingleResolve()
        {
            var state = new PackageUpdateBatchState
            {
                Items = new List<PackageUpdateBatchItem>
                {
                    new() { DisplayName = "Git package", GitUrl = "https://example.test/package.git" },
                    new() { DisplayName = "Downloaded package", Version = "2.0.0" }
                }
            };
            var events = new List<string>();
            var persistedCursors = new List<int>();
            var applied = new HashSet<string>();
            var resolved = new HashSet<string>();

            await PackageUpdateBatchRunner.Run(
                state,
                item => applied.Contains(item.DisplayName),
                item => resolved.Contains(item.DisplayName),
                (item, resolve) =>
                {
                    Assert.That(item.InstallStarted, Is.True);
                    Assert.That(state.ResolveRequired, Is.True);
                    Assert.That(persistedCursors[^1], Is.EqualTo(state.NextIndex),
                        "The in-flight marker must be persisted before entering the installer.");
                    var kind = string.IsNullOrEmpty(item.GitUrl) ? "download" : "git";
                    events.Add($"install:{kind}:{resolve}");
                    applied.Add(item.DisplayName);
                    return Task.FromResult(Result<bool>.Ok(true));
                },
                persisted => persistedCursors.Add(persisted.NextIndex),
                (_, _, item) => events.Add($"progress:{item.DisplayName}"),
                () => events.Add("clear"),
                () => events.Add("before-resolve"),
                () =>
                {
                    events.Add("resolve");
                    foreach (var packageName in applied)
                        resolved.Add(packageName);
                    return Task.FromResult(Result<bool>.Ok(true));
                });

            Assert.That(state.NextIndex, Is.EqualTo(2));
            Assert.That(state.Errors, Is.Empty);
            Assert.That(persistedCursors, Does.Contain(1));
            Assert.That(persistedCursors, Does.Contain(2));
            Assert.That(persistedCursors[^1], Is.EqualTo(2));
            Assert.That(state.Phase, Is.EqualTo(PackageUpdateBatchPhase.Resolving));
            Assert.That(state.ResolveStarted, Is.True);
            Assert.That(events, Is.EqualTo(new[]
            {
                "progress:Git package",
                "install:git:False",
                "progress:Downloaded package",
                "install:download:False",
                "clear",
                "before-resolve",
                "resolve"
            }));
        }

        [Test]
        public async Task PackageUpdateBatchRunner_ResumesAppliedItemAndContinuesAfterFailure()
        {
            var state = new PackageUpdateBatchState
            {
                Items = new List<PackageUpdateBatchItem>
                {
                    new() { DisplayName = "Already committed", GitUrl = "https://example.test/first.git" },
                    new() { DisplayName = "Failed download", Version = "2.0.0" },
                    new() { DisplayName = "Later Git package", GitUrl = "https://example.test/last.git" }
                }
            };
            var installed = new List<string>();
            var persistedCursors = new List<int>();
            var applied = new HashSet<string> { "Already committed" };
            int resolveCount = 0;

            await PackageUpdateBatchRunner.Run(
                state,
                item => applied.Contains(item.DisplayName),
                item => applied.Contains(item.DisplayName),
                (item, resolve) =>
                {
                    installed.Add($"{item.DisplayName}:{resolve}");
                    if (item.DisplayName == "Failed download")
                        return Task.FromResult(Result<bool>.Fail("download failed"));

                    applied.Add(item.DisplayName);
                    return Task.FromResult(Result<bool>.Ok(true));
                },
                persisted => persistedCursors.Add(persisted.NextIndex),
                null,
                null,
                null,
                () =>
                {
                    resolveCount++;
                    return Task.FromResult(Result<bool>.Ok(true));
                });

            Assert.That(installed, Is.EqualTo(new[]
            {
                "Failed download:False",
                "Later Git package:False"
            }));
            Assert.That(persistedCursors, Does.Contain(1));
            Assert.That(persistedCursors, Does.Contain(2));
            Assert.That(persistedCursors, Does.Contain(3));
            Assert.That(persistedCursors[^1], Is.EqualTo(3));
            Assert.That(state.NextIndex, Is.EqualTo(3));
            Assert.That(state.Errors, Is.EqualTo(new[] { "Failed download: download failed" }));
            Assert.That(resolveCount, Is.EqualTo(1));
        }

        [Test]
        public async Task PackageUpdateBatchRunner_SkipsDependentAfterDependencyFailure()
        {
            var state = new PackageUpdateBatchState
            {
                Items = new List<PackageUpdateBatchItem>
                {
                    new() { PackageId = "dependency", DisplayName = "Dependency" },
                    new()
                    {
                        PackageId = "root",
                        DisplayName = "Root",
                        DependencyIds = new[] { "dependency" }
                    },
                    new() { PackageId = "independent", DisplayName = "Independent" }
                }
            };
            var installed = new List<string>();
            var applied = new HashSet<string>();

            await PackageUpdateBatchRunner.Run(
                state,
                item => applied.Contains(item.PackageId),
                item => applied.Contains(item.PackageId),
                (item, resolve) =>
                {
                    installed.Add(item.PackageId);
                    if (item.PackageId == "dependency")
                        return Task.FromResult(Result<bool>.Fail("network failure"));

                    applied.Add(item.PackageId);
                    return Task.FromResult(Result<bool>.Ok(true));
                },
                _ => { },
                null,
                null,
                null,
                () => Task.FromResult(Result<bool>.Ok(true)));

            Assert.That(installed, Is.EqualTo(new[] { "dependency", "independent" }));
            Assert.That(state.Items[1].Failed, Is.True);
            Assert.That(state.Errors, Is.EqualTo(new[]
            {
                "Dependency: network failure",
                "Root: skipped because dependency 'Dependency' failed to update."
            }));
        }

        [Test]
        public async Task PackageUpdateBatchRunner_ResolvingReloadUsesPersistedAppliedState()
        {
            var state = new PackageUpdateBatchState
            {
                Phase = PackageUpdateBatchPhase.Resolving,
                ResolveRequired = true,
                ResolveStarted = true,
                NextIndex = 1,
                Errors = new List<string> { "Earlier package: failed" },
                Items = new List<PackageUpdateBatchItem>
                {
                    new() { DisplayName = "Applied package", Succeeded = true }
                }
            };
            int resolveCount = 0;

            await PackageUpdateBatchRunner.Run(
                state,
                _ => true,
                _ => true,
                null,
                _ => { },
                null,
                null,
                null,
                () =>
                {
                    resolveCount++;
                    return Task.FromResult(Result<bool>.Ok(true));
                });

            Assert.That(resolveCount, Is.Zero);
            Assert.That(state.Errors, Is.EqualTo(new[] { "Earlier package: failed" }));
        }

        [Test]
        public async Task PackageUpdateBatchRunner_StagingReloadRecoversCommittedInFlightItem()
        {
            var state = new PackageUpdateBatchState
            {
                Items = new List<PackageUpdateBatchItem>
                {
                    new()
                    {
                        DisplayName = "Committed before reload",
                        InstallStarted = true
                    }
                }
            };
            int installCount = 0;
            int resolveCount = 0;

            await PackageUpdateBatchRunner.Run(
                state,
                _ => true,
                _ => true,
                (_, _) =>
                {
                    installCount++;
                    return Task.FromResult(Result<bool>.Ok(true));
                },
                _ => { },
                null,
                null,
                null,
                () =>
                {
                    resolveCount++;
                    return Task.FromResult(Result<bool>.Ok(true));
                });

            Assert.That(installCount, Is.Zero);
            Assert.That(resolveCount, Is.EqualTo(1));
            Assert.That(state.Items[0].Succeeded, Is.True);
            Assert.That(state.ResolveRequired, Is.True);
            Assert.That(state.NextIndex, Is.EqualTo(1));
        }

        [Test]
        public async Task PackageUpdateBatchRunner_StagingReloadUsesPersistedPackageJsonVersion()
        {
            var state = new PackageUpdateBatchState
            {
                Items = new List<PackageUpdateBatchItem>
                {
                    new()
                    {
                        DisplayName = "Downloaded package",
                        Version = "catalog-version",
                        InstallStarted = true
                    }
                }
            };

            // This is the commit-boundary callback: package.json is authoritative and is journaled
            // before package files can trigger a domain reload.
            state.Items[0].Version = "1.2.3";
            var restored = JsonUtility.FromJson<PackageUpdateBatchState>(JsonUtility.ToJson(state));
            int installCount = 0;

            await PackageUpdateBatchRunner.Run(
                restored,
                item => string.Equals(item.Version, "1.2.3", StringComparison.Ordinal),
                _ => true,
                (_, _) =>
                {
                    installCount++;
                    return Task.FromResult(Result<bool>.Ok(true));
                },
                _ => { },
                null,
                null,
                null,
                () => Task.FromResult(Result<bool>.Ok(true)));

            Assert.That(installCount, Is.Zero);
            Assert.That(restored.Items[0].Version, Is.EqualTo("1.2.3"));
            Assert.That(restored.Items[0].Succeeded, Is.True);
        }

        [Test]
        public async Task PackageUpdateBatchRunner_ResolvingReloadRetriesUnresolvedTarget()
        {
            var state = new PackageUpdateBatchState
            {
                Phase = PackageUpdateBatchPhase.Resolving,
                ResolveRequired = true,
                ResolveStarted = true,
                NextIndex = 1,
                Items = new List<PackageUpdateBatchItem>
                {
                    new() { DisplayName = "Pending package", Succeeded = true }
                }
            };
            bool resolved = false;
            int resolveCount = 0;

            await PackageUpdateBatchRunner.Run(
                state,
                _ => true,
                _ => resolved,
                null,
                _ => { },
                null,
                null,
                null,
                () =>
                {
                    resolveCount++;
                    resolved = true;
                    return Task.FromResult(Result<bool>.Ok(true));
                });

            Assert.That(resolveCount, Is.EqualTo(1));
            Assert.That(state.Errors, Is.Empty);
        }

        [Test]
        public async Task PackageUpdateBatchRunner_PersistsResolverFailure()
        {
            var state = new PackageUpdateBatchState
            {
                Phase = PackageUpdateBatchPhase.Resolving,
                ResolveRequired = true,
                Items = new List<PackageUpdateBatchItem>
                {
                    new() { DisplayName = "Pending package", Succeeded = true }
                }
            };
            var persistedErrors = new List<int>();

            await PackageUpdateBatchRunner.Run(
                state,
                _ => true,
                _ => false,
                null,
                persisted => persistedErrors.Add(persisted.Errors.Count),
                null,
                null,
                null,
                () => Task.FromResult(Result<bool>.Fail("UPM failed")));

            Assert.That(state.Errors, Is.EqualTo(new[] { "Package resolution: UPM failed" }));
            Assert.That(persistedErrors[^1], Is.EqualTo(1));
        }
    }
}
