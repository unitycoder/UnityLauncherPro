using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace UnityLauncherPro.Helpers
{
    public class GitHubTokenValidationResult
    {
        public bool IsValid { get; set; }
        public string Login { get; set; }
        public string Name { get; set; }
        public string Error { get; set; }
        public HttpStatusCode StatusCode { get; set; }
    }

    public static class GitHubAuth
    {
        public static async Task<GitHubTokenValidationResult> ValidateTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return new GitHubTokenValidationResult
                {
                    IsValid = false,
                    Error = "Token is empty"
                };
            }

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MyWpfApp");
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token.Trim());

                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

                HttpResponseMessage response;

                try
                {
                    response = await client.GetAsync("https://api.github.com/user");
                }
                catch (Exception ex)
                {
                    return new GitHubTokenValidationResult
                    {
                        IsValid = false,
                        Error = "Request failed: " + ex.Message
                    };
                }

                string responseText = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return new GitHubTokenValidationResult
                    {
                        IsValid = true,
                        Login = JsonParser.GetStringValue(responseText, "login"),
                        Name = JsonParser.GetStringValue(responseText, "name"),
                        StatusCode = response.StatusCode
                    };
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return new GitHubTokenValidationResult
                    {
                        IsValid = false,
                        StatusCode = response.StatusCode,
                        Error = "Invalid, expired, or revoked GitHub token"
                    };
                }

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    return new GitHubTokenValidationResult
                    {
                        IsValid = false,
                        StatusCode = response.StatusCode,
                        Error = "GitHub rejected the request. This may be a rate limit or permission issue."
                    };
                }

                return new GitHubTokenValidationResult
                {
                    IsValid = false,
                    StatusCode = response.StatusCode,
                    Error = "Unexpected GitHub response: " +
                            (int)response.StatusCode + " " +
                            response.ReasonPhrase + "\n" +
                            responseText
                };
            }
        }
    } // GitHubAuth class


    public static class GitHubTokenStore
    {
        private static readonly string FolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), MainWindow.appName);
        private static readonly string FilePath = Path.Combine(FolderPath, MainWindow.appName + ".dat");

        public static void SaveToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Token is empty.", "token");

            if (!Directory.Exists(FolderPath)) Directory.CreateDirectory(FolderPath);

            byte[] plainBytes = Encoding.UTF8.GetBytes(token);
            byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath, encryptedBytes);
        }

        public static string LoadToken()
        {
            if (!File.Exists(FilePath))
                return null;

            try
            {
                byte[] encryptedBytes = File.ReadAllBytes(FilePath);

                byte[] plainBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    null,
                    DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return null;
            }
        }

        public static void DeleteToken()
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }

        public static bool HasToken()
        {
            return !string.IsNullOrWhiteSpace(LoadToken());
        }
    }

} // namespace
