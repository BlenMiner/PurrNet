using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace PurrNet.Editor
{
    public static class PurrPackageManagerAPI
    {
        private const string BaseUrl = "https://purrnet.dev/api";

        public static async Task<Result<PackagesResponse>> GetPackages(string apiKey)
        {
            return await SendRequest<PackagesResponse>($"{BaseUrl}/packages", apiKey);
        }

        public static async Task<Result<EntitlementsResponse>> GetEntitlements(string apiKey)
        {
            return await SendRequest<EntitlementsResponse>($"{BaseUrl}/entitlements", apiKey);
        }

        public static async Task<Result<UserInfo>> GetMe(string apiKey)
        {
            return await SendRequest<UserInfo>($"{BaseUrl}/me", apiKey);
        }

        internal static async Task<Result<UserInfo>> GetMe(string apiKey, int timeoutSeconds)
        {
            return await SendRequest<UserInfo>($"{BaseUrl}/me", apiKey, timeoutSeconds);
        }

        public static async Task<Result<PackageRegistrationResponse>> RegisterPackage(
            string apiKey, PackageRegistrationRequest registration)
        {
            return await SendJsonRequest<PackageRegistrationResponse>(
                $"{BaseUrl}/admin/packages", UnityWebRequest.kHttpVerbPOST, apiKey, registration);
        }

        public static async Task<Result<DownloadResponse>> GetDownloadUrl(string apiKey, string packageId, string versionId)
        {
            var url = $"{BaseUrl}/packages/{packageId}/download?versionId={versionId}";
            return await SendRequest<DownloadResponse>(url, apiKey);
        }

        public static async Task<Result<string>> DownloadFile(string url, string destPath)
        {
            try
            {
                using var request = UnityWebRequest.Get(url);
                request.downloadHandler = new DownloadHandlerFile(destPath)
                {
                    removeFileOnAbort = true
                };
                await SendWebRequestAsync(request);

                if (request.result != UnityWebRequest.Result.Success)
                    return Result<string>.Fail(request.error);

                return Result<string>.Ok(destPath);
            }
            catch (Exception e)
            {
                return Result<string>.Fail(e.Message);
            }
        }

        private static Task SendWebRequestAsync(UnityWebRequest request)
        {
            var tcs = new TaskCompletionSource<bool>();
            var operation = request.SendWebRequest();
            operation.completed += _ => tcs.TrySetResult(true);
            return tcs.Task;
        }

        private static async Task<Result<T>> SendRequest<T>(string url, string apiKey, int timeoutSeconds = 0)
        {
            try
            {
                using var request = UnityWebRequest.Get(url);
                request.timeout = timeoutSeconds;

                if (!string.IsNullOrEmpty(apiKey))
                    request.SetRequestHeader("Authorization", "Bearer " + apiKey);

                await SendWebRequestAsync(request);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string errorMsg = request.error;
                    try
                    {
                        var apiError = JsonConvert.DeserializeObject<ApiError>(request.downloadHandler.text);
                        string serverError = apiError?.Error ?? apiError?.Message;
                        if (!string.IsNullOrEmpty(serverError))
                            errorMsg = serverError;
                    }
                    catch
                    {
                        // use the original error
                    }

                    return Result<T>.Fail(errorMsg);
                }

                var result = JsonConvert.DeserializeObject<T>(request.downloadHandler.text);
                return Result<T>.Ok(result);
            }
            catch (Exception e)
            {
                return Result<T>.Fail(e.Message);
            }
        }

        private static async Task<Result<T>> SendJsonRequest<T>(string url, string method, string apiKey, object body)
        {
            try
            {
                using var request = new UnityWebRequest(url, method)
                {
                    downloadHandler = new DownloadHandlerBuffer(),
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body)))
                };
                request.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(apiKey))
                    request.SetRequestHeader("Authorization", "Bearer " + apiKey);

                await SendWebRequestAsync(request);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string errorMsg = request.error;
                    try
                    {
                        var apiError = JsonConvert.DeserializeObject<ApiError>(request.downloadHandler.text);
                        string serverError = apiError?.Error ?? apiError?.Message;
                        if (!string.IsNullOrEmpty(serverError))
                            errorMsg = serverError;
                    }
                    catch
                    {
                        // use the original error
                    }

                    return Result<T>.Fail(errorMsg);
                }

                var result = JsonConvert.DeserializeObject<T>(request.downloadHandler.text);
                return Result<T>.Ok(result);
            }
            catch (Exception e)
            {
                return Result<T>.Fail(e.Message);
            }
        }
    }
}
