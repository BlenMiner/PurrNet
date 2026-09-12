using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace PurrNet.Editor
{
    public struct Result<T>
    {
        public bool Success { get; }
        public T Value { get; }
        public string Error { get; }

        private Result(bool success, T value, string error)
        {
            Success = success;
            Value = value;
            Error = error;
        }

        public static Result<T> Ok(T value) => new Result<T>(true, value, null);
        public static Result<T> Fail(string error) => new Result<T>(false, default, error);
    }

    public class PackagesResponse
    {
        [JsonProperty("packages")]
        public PackageInfo[] Packages { get; private set; }
    }

    public class PackageInfo
    {
        [JsonProperty("id")]
        public string Id { get; private set; }

        [JsonProperty("slug")]
        public string Slug { get; private set; }

        [JsonProperty("github_owner")]
        public string GithubOwner { get; private set; }

        [JsonProperty("github_repo")]
        public string GithubRepo { get; private set; }

        [JsonProperty("display_name")]
        public string DisplayName { get; private set; }

        [JsonProperty("description")]
        public string Description { get; private set; }

        [JsonProperty("upm_package_name")]
        public string UpmPackageName { get; private set; }

        [JsonProperty("required_tier")]
        public string RequiredTier { get; private set; }

        [JsonProperty("is_hidden")]
        public bool IsHidden { get; private set; }

        [JsonProperty("is_early_access")]
        public bool IsEarlyAccess { get; private set; }

        [JsonProperty("is_user_editable")]
        public bool IsUserEditable { get; private set; }

        [JsonProperty("dependency_ids")]
        public string[] DependencyIds { get; private set; }

        [JsonProperty("entitled_version")]
        public string EntitledVersion { get; private set; }

        [JsonProperty("has_access")]
        public bool HasAccess { get; private set; }

        [JsonProperty("frozen")]
        public bool Frozen { get; private set; }

        [JsonProperty("latest_version")]
        public string LatestVersion { get; private set; }

        [JsonProperty("category")]
        public string Category { get; private set; }

        [JsonProperty("display_order")]
        public int DisplayOrder { get; private set; }

        [JsonProperty("versions")]
        public VersionInfo[] Versions { get; private set; }

        [JsonProperty("is_external")]
        public bool IsExternal { get; private set; }

        [JsonProperty("git_install_url_release")]
        public string GitInstallUrlRelease { get; private set; }

        [JsonProperty("git_install_url_dev")]
        public string GitInstallUrlDev { get; private set; }

        [JsonProperty("latest_commit_release")]
        public string LatestCommitRelease { get; private set; }

        [JsonProperty("latest_commit_dev")]
        public string LatestCommitDev { get; private set; }

        public string GetUpmPackageName()
        {
            // The website reads this directly from the repository's package.json.
            // Never guess: a fabricated manifest key can install the right files under
            // the wrong package identity and leave Unity's lock file inconsistent.
            return UpmPackageName;
        }
    }

    public class VersionInfo
    {
        [JsonProperty("id")]
        public string Id { get; private set; }

        [JsonProperty("version")]
        public string Version { get; private set; }

        [JsonProperty("channel")]
        public string Channel { get; private set; }

        [JsonProperty("tag_name")]
        public string TagName { get; private set; }

        [JsonProperty("release_notes")]
        public string ReleaseNotes { get; private set; }

        [JsonProperty("published_at")]
        public string PublishedAt { get; private set; }
    }

    public class DownloadResponse
    {
        [JsonProperty("url")]
        public string Url { get; private set; }

        [JsonProperty("filename")]
        public string Filename { get; private set; }
    }

    public class EntitlementsResponse
    {
        [JsonProperty("tier")]
        public string Tier { get; private set; }

        [JsonProperty("total_donated_cents")]
        public int TotalDonatedCents { get; private set; }

        [JsonProperty("features")]
        public FeaturesInfo Features { get; private set; }
    }

    public class FeaturesInfo
    {
        [JsonProperty("basic-tools")]
        public bool BasicTools { get; private set; }

        [JsonProperty("pro-tools")]
        public bool ProTools { get; private set; }

        [JsonProperty("premium-tools")]
        public bool PremiumTools { get; private set; }

        [JsonProperty("studio-tools")]
        public bool StudioTools { get; private set; }

        [JsonProperty("supporter")]
        public bool Supporter { get; private set; }
    }

    public class UserInfo
    {
        [JsonProperty("id")]
        public string Id { get; private set; }

        [JsonProperty("username")]
        public string Username { get; private set; }

        [JsonProperty("avatar_url")]
        public string AvatarUrl { get; private set; }

        [JsonProperty("is_admin")]
        public bool IsAdmin { get; private set; }
    }

    public class PackageRegistrationRequest
    {
        [JsonProperty("github_owner")]
        public string GithubOwner { get; }

        [JsonProperty("github_repo")]
        public string GithubRepo { get; }

        [JsonProperty("display_name")]
        public string DisplayName { get; }

        [JsonProperty("required_tier")]
        public string RequiredTier { get; }

        [JsonProperty("is_early_access")]
        public bool IsEarlyAccess { get; }

        public PackageRegistrationRequest(string githubOwner, string githubRepo, string displayName)
        {
            GithubOwner = githubOwner;
            GithubRepo = githubRepo;
            DisplayName = displayName;
            RequiredTier = "admin";
            IsEarlyAccess = true;
        }
    }

    public class PackageRegistrationResponse
    {
        [JsonProperty("success")]
        public bool Success { get; private set; }

        [JsonProperty("package")]
        public PackageInfo Package { get; private set; }
    }

    public class ApiError
    {
        [JsonProperty("error")]
        public string Error { get; private set; }

        [JsonProperty("message")]
        public string Message { get; private set; }
    }

    [Serializable]
    internal enum PackageUpdateBatchPhase
    {
        Staging,
        Resolving
    }

    [Serializable]
    internal sealed class PackageUpdateBatchState
    {
        public List<PackageUpdateBatchItem> Items = new();
        public int NextIndex;
        public List<string> Errors = new();
        public PackageUpdateBatchPhase Phase;
        public bool ResolveRequired;
        public bool ResolveStarted;
    }

    [Serializable]
    internal sealed class PackageUpdateBatchItem
    {
        public string PackageId;
        public string UpmName;
        public string DisplayName;
        public string VersionId;
        public string Version;
        public string GitUrl;
        public string ExpectedCommit;
        public string ExpectedLockVersion;
        public string[] DependencyIds = Array.Empty<string>();
        public bool InstallStarted;
        public bool Succeeded;
        public bool Failed;
    }

    /// <summary>
    /// Runs the reload-sensitive part of Update All from a persisted cursor. The caller saves the
    /// state before and after every item, so a domain reload can inspect/retry the in-flight item and
    /// continue. All installs are staged without resolving; the durable journal remains until the
    /// single final resolve completes or its result can be verified after a reload.
    /// </summary>
    internal static class PackageUpdateBatchRunner
    {
        public static async Task Run(
            PackageUpdateBatchState state,
            Func<PackageUpdateBatchItem, bool> isApplied,
            Func<PackageUpdateBatchItem, bool> isResolved,
            Func<PackageUpdateBatchItem, bool, Task<Result<bool>>> install,
            Action<PackageUpdateBatchState> persist,
            Action<int, int, PackageUpdateBatchItem> reportProgress,
            Action clearProgress,
            Action beforeResolve,
            Func<Task<Result<bool>>> resolve)
        {
            if (state?.Items == null || state.Items.Count == 0)
                return;

            state.Errors ??= new List<string>();

            if (state.Phase == PackageUpdateBatchPhase.Staging)
            {
                try
                {
                    while (state.NextIndex < state.Items.Count)
                    {
                        var item = state.Items[state.NextIndex];
                        if (item == null)
                        {
                            state.Errors.Add("Package: the saved update entry is invalid.");
                            state.NextIndex++;
                            persist?.Invoke(state);
                            continue;
                        }

                        var failedDependency = FindFailedDependency(state, item);
                        if (failedDependency != null)
                        {
                            item.Failed = true;
                            state.Errors.Add(
                                $"{GetDisplayName(item)}: skipped because dependency " +
                                $"'{GetDisplayName(failedDependency)}' failed to update.");
                        }
                        else
                        {
                            bool alreadyApplied = false;
                            try
                            {
                                alreadyApplied = isApplied?.Invoke(item) == true;
                            }
                            catch (Exception e)
                            {
                                state.Errors.Add(
                                    $"{GetDisplayName(item)}: could not verify the pending update: {e.Message}");
                            }

                            if (alreadyApplied)
                            {
                                item.Succeeded = true;
                                if (item.InstallStarted)
                                    state.ResolveRequired = true;
                            }
                            else
                            {
                                reportProgress?.Invoke(state.NextIndex, state.Items.Count, item);

                                // Persist before the awaited installer. If package code reloads the
                                // domain after committing, the next domain knows this item was in flight.
                                item.InstallStarted = true;
                                state.ResolveRequired = true;
                                persist?.Invoke(state);

                                try
                                {
                                    var result = install == null
                                        ? Result<bool>.Fail("No batch installer is available.")
                                        : await install(item, false);
                                    if (result.Success)
                                    {
                                        item.Succeeded = true;
                                    }
                                    else
                                    {
                                        item.Failed = true;
                                        state.Errors.Add($"{GetDisplayName(item)}: {result.Error}");
                                    }
                                }
                                catch (Exception e)
                                {
                                    item.Failed = true;
                                    state.Errors.Add($"{GetDisplayName(item)}: {e.Message}");
                                }
                            }
                        }

                        state.NextIndex++;
                        persist?.Invoke(state);
                    }
                }
                finally
                {
                    clearProgress?.Invoke();
                }

                state.Phase = PackageUpdateBatchPhase.Resolving;
                persist?.Invoke(state);
            }
            else
            {
                // A native progress dialog can outlive the managed domain that opened it.
                clearProgress?.Invoke();
            }

            if (!state.ResolveRequired)
                return;

            // A reload during Resolve abandons this continuation. If the new domain can see every
            // successfully staged target, resolution already completed and must not be requested again.
            if (state.ResolveStarted && AreSuccessfulItemsApplied(state, isResolved))
                return;

            state.ResolveStarted = true;
            persist?.Invoke(state);
            try
            {
                beforeResolve?.Invoke();
                var result = resolve == null
                    ? Result<bool>.Fail("No package resolver is available.")
                    : await resolve();
                if (!result.Success)
                    state.Errors.Add($"Package resolution: {result.Error}");
                else if (!AreSuccessfulItemsApplied(state, isResolved))
                    state.Errors.Add(
                        "Package resolution completed, but one or more staged updates are not visible. " +
                        "Reopen Unity to let the Package Manager refresh them.");
            }
            catch (Exception e)
            {
                state.Errors.Add($"Package resolution: {e.Message}");
            }

            persist?.Invoke(state);
        }

        private static PackageUpdateBatchItem FindFailedDependency(PackageUpdateBatchState state,
            PackageUpdateBatchItem item)
        {
            if (item.DependencyIds == null || item.DependencyIds.Length == 0)
                return null;

            foreach (var dependencyId in item.DependencyIds)
            {
                foreach (var candidate in state.Items)
                {
                    if (candidate?.Failed == true
                        && !string.IsNullOrEmpty(candidate.PackageId)
                        && string.Equals(candidate.PackageId, dependencyId,
                            StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            }

            return null;
        }

        private static bool AreSuccessfulItemsApplied(PackageUpdateBatchState state,
            Func<PackageUpdateBatchItem, bool> isApplied)
        {
            bool hasSuccessfulItem = false;
            foreach (var item in state.Items)
            {
                if (item?.Succeeded != true)
                    continue;

                hasSuccessfulItem = true;
                try
                {
                    if (isApplied?.Invoke(item) != true)
                        return false;
                }
                catch
                {
                    return false;
                }
            }

            // With no successful root item there is no target to verify. A resumed resolving phase
            // may still have staged a dependency before the root failed, so one accepted resolve is enough.
            return hasSuccessfulItem || state.ResolveStarted;
        }

        private static string GetDisplayName(PackageUpdateBatchItem item)
        {
            return string.IsNullOrEmpty(item?.DisplayName)
                ? item?.UpmName ?? "Package"
                : item.DisplayName;
        }
    }
}
