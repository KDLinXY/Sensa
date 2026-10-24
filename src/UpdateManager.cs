using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Sensa
{
    internal sealed class GitHubRelease
    {
        public string tag_name { get; set; }
        public string name { get; set; }
        public string body { get; set; }
        public string html_url { get; set; }
        public bool draft { get; set; }
        public bool prerelease { get; set; }
        public GitHubAsset[] assets { get; set; }
    }

    internal sealed class GitHubAsset
    {
        public string name { get; set; }
        public string browser_download_url { get; set; }
    }

    internal static class UpdateManager
    {
        internal const string Repository = "KDLinXY/Sensa";
        private const string ApiUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";

        internal static string CurrentVersion
        {
            get
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return version.Major + "." + version.Minor + "." + version.Build;
            }
        }

        internal static GitHubRelease CheckLatest()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            var request = (HttpWebRequest)WebRequest.Create(ApiUrl);
            request.UserAgent = "Sensa-Updater/" + CurrentVersion;
            request.Accept = "application/vnd.github+json";
            request.Timeout = 12000;
            request.ReadWriteTimeout = 12000;
            using (var response = request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var release = new JavaScriptSerializer().Deserialize<GitHubRelease>(reader.ReadToEnd());
                if (release == null || release.draft || release.prerelease || release.assets == null)
                    throw new InvalidOperationException("未找到有效的正式发布版本");
                return release;
            }
        }

        internal static bool IsNewer(string remoteTag, string currentVersion)
        {
            Version remote;
            Version current;
            return TryParseVersion(remoteTag, out remote) && TryParseVersion(currentVersion, out current)
                && remote > current;
        }

        internal static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            if (String.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text.Substring(1);
            int suffix = text.IndexOfAny(new[] { '-', '+' });
            if (suffix >= 0) text = text.Substring(0, suffix);
            return Version.TryParse(text, out version);
        }

        internal static string DownloadVerifiedPackage(GitHubRelease release)
        {
            var exe = release.assets.FirstOrDefault(a => a.name == "Sensa.exe");
            var hash = release.assets.FirstOrDefault(a => a.name == "Sensa.exe.sha256");
            if (exe == null || hash == null)
                throw new InvalidOperationException("发布版本缺少程序或 SHA-256 校验文件");

            string directory = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "Sensa", "updates", release.tag_name);
            Directory.CreateDirectory(directory);
            string package = Path.Combine(directory, "Sensa-new.exe");
            string hashFile = Path.Combine(directory, "Sensa.exe.sha256");
            using (var client = new WebClient())
            {
                client.Headers["User-Agent"] = "Sensa-Updater/" + CurrentVersion;
                client.DownloadFile(exe.browser_download_url, package);
                client.DownloadFile(hash.browser_download_url, hashFile);
            }

            VerifyPackage(package, hashFile);
            return package;
        }

        internal static void VerifyPackage(string package, string hashFile)
        {
            string expected = File.ReadAllText(hashFile).Trim().Split(new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
            string actual;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(package))
                actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            if (expected.Length != 64 || actual != expected)
            {
                File.Delete(package);
                throw new InvalidDataException("更新包 SHA-256 校验失败，已阻止安装");
            }
        }

        internal static void LaunchUpdater(string package)
        {
            string target = Assembly.GetExecutingAssembly().Location;
            string script = Path.Combine(Path.GetDirectoryName(package), "apply-update.ps1");
            string content = "$processId = " + Process.GetCurrentProcess().Id + "\n" +
                "while (Get-Process -Id $processId -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 300 }\n" +
                "Copy-Item -LiteralPath " + Quote(package) + " -Destination " + Quote(target) + " -Force\n" +
                "Start-Process -FilePath " + Quote(target) + "\n" +
                "Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force\n";
            File.WriteAllText(script, content, new UTF8Encoding(true));
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(script),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        private static string Quote(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }
    }
}
