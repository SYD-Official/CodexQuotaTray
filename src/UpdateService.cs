using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CodexQuotaTray
{
    internal enum UpdateFailureKind
    {
        None,
        Network,
        Service,
        InvalidResponse,
        Download
    }

    internal sealed class UpdateReleaseInfo
    {
        public Version Version { get; set; }
        public string TagName { get; set; }
        public string AssetName { get; set; }
        public string DownloadUrl { get; set; }
        public string Digest { get; set; }
        public long Size { get; set; }
        public string PageUrl { get; set; }
    }

    internal sealed class UpdateCheckResult
    {
        public UpdateReleaseInfo Release { get; set; }
        public UpdateFailureKind FailureKind { get; set; }
        public string Error { get; set; }
    }

    internal sealed class UpdateDownloadResult
    {
        public string FilePath { get; set; }
        public UpdateFailureKind FailureKind { get; set; }
        public string Error { get; set; }
    }

    internal static class UpdateService
    {
        private const long MaximumUpdateBytes = 128L * 1024L * 1024L;
        internal const string RepositoryUrl = "https://github.com/SYD-Official/CodexQuotaTray";
        internal const string LatestReleaseApi = "https://api.github.com/repos/SYD-Official/CodexQuotaTray/releases/latest";

        internal static UpdateCheckResult CheckLatest()
        {
            try
            {
                EnableTls12();
                var request = CreateRequest(LatestReleaseApi);
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return ParseLatestRelease(reader.ReadToEnd());
                }
            }
            catch (WebException exception)
            {
                return FromWebException(exception);
            }
            catch (Exception exception)
            {
                return new UpdateCheckResult { FailureKind = UpdateFailureKind.InvalidResponse, Error = exception.Message };
            }
        }

        internal static UpdateCheckResult ParseLatestRelease(string json)
        {
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (AsBool(Get(root, "draft")) || AsBool(Get(root, "prerelease")))
                    return Invalid("更新器只接受 GitHub 正式发布版本。");

                var tagName = AsString(Get(root, "tag_name"));
                Version version;
                var normalizedTag = string.IsNullOrWhiteSpace(tagName) ? null : tagName.Trim();
                if (normalizedTag == null || !Regex.IsMatch(normalizedTag, @"^v\d+\.\d+\.\d+$", RegexOptions.CultureInvariant) ||
                    !Version.TryParse(normalizedTag.Substring(1), out version))
                    return Invalid("GitHub 发布信息中没有有效版本号。");

                var expectedName = "CodexQuotaTray-v" + version.ToString(3) + ".exe";
                Dictionary<string, object> selected = null;
                var assets = Get(root, "assets") as object[];
                if (assets != null)
                {
                    foreach (var value in assets)
                    {
                        var asset = value as Dictionary<string, object>;
                        if (asset == null || !string.Equals(AsString(Get(asset, "name")), expectedName, StringComparison.OrdinalIgnoreCase)) continue;
                        selected = asset;
                        break;
                    }
                }

                if (selected == null) return Invalid("没有找到与版本号匹配的 Windows 程序文件。");
                var downloadUrl = AsString(Get(selected, "browser_download_url"));
                Uri uri;
                if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out uri) ||
                    uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                    return Invalid("GitHub 返回了无效的下载地址。");

                var digest = AsString(Get(selected, "digest"));
                if (!IsValidSha256Digest(digest)) return Invalid("GitHub 发布文件没有可用的 SHA-256 摘要。");
                var size = AsLong(Get(selected, "size"));
                if (size <= 0 || size > MaximumUpdateBytes) return Invalid("GitHub 发布文件大小无效或超过安全限制。");

                return new UpdateCheckResult
                {
                    Release = new UpdateReleaseInfo
                    {
                        Version = version,
                        TagName = tagName,
                        AssetName = expectedName,
                        DownloadUrl = downloadUrl,
                        Digest = digest,
                        Size = size,
                        PageUrl = AsString(Get(root, "html_url"))
                    }
                };
            }
            catch (Exception exception)
            {
                return Invalid(exception.Message);
            }
        }

        internal static UpdateDownloadResult Download(UpdateReleaseInfo release)
        {
            if (release == null) return DownloadError(UpdateFailureKind.Download, "没有可下载的版本信息。");
            var updatesDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexMeter", "Updates");
            var targetPath = Path.Combine(updatesDirectory, Path.GetFileName(release.AssetName));
            var temporaryPath = targetPath + ".download";

            try
            {
                Directory.CreateDirectory(updatesDirectory);
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                EnableTls12();
                var request = CreateRequest(release.DownloadUrl);
                request.ReadWriteTimeout = 30000;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var input = response.GetResponseStream())
                using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    if (!IsAllowedDownloadUri(response.ResponseUri))
                        throw new InvalidDataException("GitHub 下载发生了不安全的重定向。");
                    if (response.ContentLength > 0 && response.ContentLength != release.Size)
                        throw new InvalidDataException("下载响应大小与 GitHub 发布信息不一致。");

                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += read;
                        if (total > release.Size || total > MaximumUpdateBytes)
                            throw new InvalidDataException("下载文件超过 GitHub 声明大小或安全限制。");
                        output.Write(buffer, 0, read);
                    }
                }

                var file = new FileInfo(temporaryPath);
                if (release.Size > 0 && file.Length != release.Size)
                    throw new InvalidDataException("下载文件大小与 GitHub 发布信息不一致。");
                if (!VerifyDigest(temporaryPath, release.Digest))
                    throw new InvalidDataException("下载文件的 SHA-256 校验失败。");

                if (File.Exists(targetPath)) File.Delete(targetPath);
                File.Move(temporaryPath, targetPath);
                return new UpdateDownloadResult { FilePath = targetPath };
            }
            catch (WebException exception)
            {
                TryDelete(temporaryPath);
                var check = FromWebException(exception);
                return DownloadError(check.FailureKind, check.Error);
            }
            catch (Exception exception)
            {
                TryDelete(temporaryPath);
                return DownloadError(UpdateFailureKind.Download, exception.Message);
            }
        }

        internal static bool VerifyDigest(string filePath, string digest)
        {
            if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return false;
            var expected = digest.Substring("sha256:".Length).Trim();
            if (expected.Length != 64) return false;
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var actual = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static bool IsValidSha256Digest(string digest)
        {
            if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return false;
            var hash = digest.Substring("sha256:".Length).Trim();
            if (hash.Length != 64) return false;
            foreach (var character in hash)
                if (!Uri.IsHexDigit(character)) return false;
            return true;
        }

        internal static bool IsAllowedDownloadUri(Uri uri)
        {
            if (uri == null || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
            return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
        }

        internal static Version CurrentVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version;
        }

        private static HttpWebRequest CreateRequest(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Accept = "application/vnd.github+json";
            request.UserAgent = "CodexQuotaTray/" + CurrentVersion();
            request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
            request.Timeout = 15000;
            request.AllowAutoRedirect = true;
            return request;
        }

        private static void EnableTls12()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        private static UpdateCheckResult FromWebException(WebException exception)
        {
            var response = exception.Response as HttpWebResponse;
            if (IsNetworkFailure(exception.Status))
                return new UpdateCheckResult { FailureKind = UpdateFailureKind.Network, Error = exception.Message };
            if (response != null && ((int)response.StatusCode == 403 || (int)response.StatusCode == 429))
                return new UpdateCheckResult { FailureKind = UpdateFailureKind.Service, Error = "GitHub API 请求受到频率限制。" };
            return new UpdateCheckResult { FailureKind = UpdateFailureKind.Service, Error = exception.Message };
        }

        internal static bool IsNetworkFailure(WebExceptionStatus status)
        {
            return status == WebExceptionStatus.NameResolutionFailure ||
                   status == WebExceptionStatus.ProxyNameResolutionFailure ||
                   status == WebExceptionStatus.ConnectFailure ||
                   status == WebExceptionStatus.Timeout ||
                   status == WebExceptionStatus.SecureChannelFailure ||
                   status == WebExceptionStatus.TrustFailure ||
                   status == WebExceptionStatus.ReceiveFailure ||
                   status == WebExceptionStatus.ConnectionClosed;
        }

        private static UpdateCheckResult Invalid(string error)
        {
            return new UpdateCheckResult { FailureKind = UpdateFailureKind.InvalidResponse, Error = error };
        }

        private static UpdateDownloadResult DownloadError(UpdateFailureKind kind, string error)
        {
            return new UpdateDownloadResult { FailureKind = kind, Error = error };
        }

        private static object Get(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value : null;
        }

        private static string AsString(object value)
        {
            return value == null ? null : Convert.ToString(value);
        }

        private static long AsLong(object value)
        {
            if (value == null) return 0;
            long parsed;
            return long.TryParse(Convert.ToString(value), out parsed) ? parsed : 0;
        }

        private static bool AsBool(object value)
        {
            if (value is bool) return (bool)value;
            bool parsed;
            return value != null && bool.TryParse(Convert.ToString(value), out parsed) && parsed;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }
}
