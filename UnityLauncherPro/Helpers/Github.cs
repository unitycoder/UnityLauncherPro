using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Diagnostics;

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

    public class GitHubCreateRepoResult
    {
        public bool Success { get; set; }
        public string Name { get; set; }
        //public string FullName { get; set; }
        //public string HtmlUrl { get; set; }
        //public string CloneUrl { get; set; }
        //public string SshUrl { get; set; }
        public string Error { get; set; }
        public HttpStatusCode StatusCode { get; set; }
    }

    public static class GithubActions
    {

        public static async Task<GitHubCreateRepoResult> CreateRepositoryAsync(string token, string repoName, string description = "", bool isPrivate = true, bool autoInit = false)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return new GitHubCreateRepoResult
                {
                    Success = false,
                    Error = "Token is empty."
                };
            }

            if (string.IsNullOrWhiteSpace(repoName))
            {
                return new GitHubCreateRepoResult
                {
                    Success = false,
                    Error = "Repository name is empty."
                };
            }

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(MainWindow.appName);
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token.Trim());

                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

                var json = ('{' +
                     $"\"name\": \"{repoName}\"," +
                     $"\"description\": \"{description ?? ""}\"," +
                     $"\"private\": {isPrivate.ToString().ToLower()}," +
                     $"\"auto_init\": {autoInit.ToString().ToLower()}" +
                     '}');

                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    HttpResponseMessage response;

                    try
                    {
                        response = await client.PostAsync(
                            "https://api.github.com/user/repos",
                            content);
                    }
                    catch (Exception ex)
                    {
                        return new GitHubCreateRepoResult
                        {
                            Success = false,
                            Error = "Request failed: " + ex.Message
                        };
                    }

                    string responseText = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        return new GitHubCreateRepoResult
                        {
                            Success = true,
                            StatusCode = response.StatusCode
                            //Name = (string)result["name"],
                            //FullName = (string)result["full_name"],
                            //HtmlUrl = (string)result["html_url"],
                            //CloneUrl = (string)result["clone_url"],
                            //SshUrl = (string)result["ssh_url"]
                        };
                    }

                    return new GitHubCreateRepoResult
                    {
                        Success = false,
                        StatusCode = response.StatusCode,
                        Error = "GitHub API error: " +
                                (int)response.StatusCode + " " +
                                response.ReasonPhrase + "\n" +
                                responseText
                    };
                }
            }
        }








        public static async Task<string> InitRepositoryAsync(string baseDir, string projectName, bool initGitLfs = false, string defaultBranch = "main")
        {
            string projectPath = string.IsNullOrWhiteSpace(projectName)
                ? Path.GetFullPath(baseDir)
                : Path.GetFullPath(Path.Combine(baseDir, projectName));

            if (!Directory.Exists(projectPath)) Directory.CreateDirectory(projectPath);

            // Git 2.28+ supports --initial-branch
            await RunGitAsync(projectPath, "init --initial-branch=\"" + defaultBranch + "\"");

            string gitignorePath = Path.Combine(projectPath, ".gitignore");

            if (initGitLfs)
            {
                try
                {
                    // Local repo setup. Requires Git LFS to be installed on the user's machine.
                    await RunGitAsync(projectPath, "lfs install --local");
                }
                catch
                {
                    // Git repo is still valid even if LFS init fails.
                    // Log this in your app if you have a logger.
                }
            }

            return projectPath;
        }

        public static async Task RunGitAsync(string workingDirectory, string arguments)
        {
            await Task.Run(delegate
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                Console.WriteLine("run " + startInfo.FileName + " " + startInfo.Arguments);

                using (var process = new Process())
                {
                    process.StartInfo = startInfo;

                    process.Start();

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        Console.WriteLine("failed to run git command: " + error);
                        throw new Exception(
                            "Git command failed:\n" +
                            "git " + arguments + "\n\n" +
                            "Output:\n" + output + "\n\n" +
                            "Error:\n" + error);
                    }
                }
            });
        }




        // checks if repo is valid and if it already exists in the user's account. Returns null if valid, otherwise an error message.
        public static async Task<string> ValidateRepoName(string repoName, string userName)
        {
            // validate string locally
            if (string.IsNullOrEmpty(repoName)) return "Repository name cannot be empty.";

            // regex "The repository name can only contain ASCII letters, digits, and the characters ., -, and _."
            if (!System.Text.RegularExpressions.Regex.IsMatch(repoName, @"^[a-zA-Z0-9._-]+$")) return "Repository name can only contain letters, digits, ., -, and _.";

            // check if repo already exists in user's account
            var token = GitHubTokenStore.LoadToken();
            if (string.IsNullOrWhiteSpace(token)) return "No GitHub token found. Please set a token first.";

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(MainWindow.appName);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

                HttpResponseMessage response;
                try
                {
                    response = await client.GetAsync($"https://api.github.com/repos/{userName}/{repoName}");
                }
                catch (Exception ex)
                {
                    return "Request failed: " + ex.Message;
                }

                if (response.StatusCode == HttpStatusCode.OK)
                    return $"Repository '{repoName}' already exists in your account.";

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null; // repo does not exist, name is available

                string responseText = await response.Content.ReadAsStringAsync();
                return $"Unexpected GitHub response: {(int)response.StatusCode} {response.ReasonPhrase}\n{responseText}";
            }
        }


    } // GithubActions class

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
                client.DefaultRequestHeaders.UserAgent.ParseAdd(MainWindow.appName);
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
