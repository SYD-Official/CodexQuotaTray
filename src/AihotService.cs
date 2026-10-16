using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CodexQuotaTray
{
    // AIHOT Codex 重置监控 —— 展示用事件条目
    internal sealed class AihotResetEventItem
    {
        public string Id;
        public string DisplayLabel;   // 类型标签,如"重置（形式未明确）"
        public string Title;          // 标题,如"Tibo 确认重置"
        public string Status;         // confirmed / predicted / ...
        public bool IsPending;        // 尚未落地(预告)
        public string TimeText;       // 已格式化的时间(MM-dd HH:mm)
        public string ScheduleText;   // 预告文案(如"北京时间预计 10月13日 15:00 前")
        public string SummaryText;    // 摘要(取最新一条帖子的正文)
        public string PrimaryUrl;     // 原帖链接(取最新一条帖子)

        // ---- 以下为「新通知 fingerprint」专用原始字段,不参与界面显示 ----
        public string Type;                // 事件类型,如 direct_reset
        public string OccurredOnRaw;       // occurredOn 原始值(可能为空)
        public string SchedulePrecision;   // schedule.precision
        public string ScheduleFromRaw;     // schedule.from
        public string ScheduleThroughRaw;  // schedule.through
        public List<string> PostIds;       // posts[].id(仅指纹使用,顺序不参与比较)
    }

    // AIHOT Codex 重置监控 —— 一次数据快照(UI 只读使用)
    internal sealed class AihotResetsSnapshot
    {
        public int Version;                                        // 每次状态变化 +1,供 UI 判断是否需要重绘
        public List<AihotResetEventItem> Events = new List<AihotResetEventItem>();
        public int EventCount;
        public int PendingCount;                                   // 尚未落地的预告数量(来自 monitor.pendingCount)
        public string DataCheckedAtText;                           // AIHOT 侧的数据校验时间(MM-dd HH:mm)
        public DateTimeOffset? FetchedAtLocal;                     // 上次成功获取(200)时间
        public DateTimeOffset? LastCheckLocal;                     // 上次检查(含 304)时间
        public string StatusKey = "init";                          // init/cache/ok/not-modified/network/http/ratelimited/parse
        public string StatusDetail;                                // 详细(错误码等,供状态行补充说明)
        public DateTimeOffset? NextAttemptUtc;                     // 退避/等待中的下次可尝试时间(UTC)

        public AihotResetsSnapshot Clone()
        {
            var copy = new AihotResetsSnapshot
            {
                Version = Version,
                EventCount = EventCount,
                PendingCount = PendingCount,
                DataCheckedAtText = DataCheckedAtText,
                FetchedAtLocal = FetchedAtLocal,
                LastCheckLocal = LastCheckLocal,
                StatusKey = StatusKey,
                StatusDetail = StatusDetail,
                NextAttemptUtc = NextAttemptUtc
            };
            copy.Events = new List<AihotResetEventItem>(Events);
            return copy;
        }
    }

    /// <summary>
    /// AIHOT「已见通知」轻量持久化:每行一个 fingerprint,纯文本,上限 400 行(超出裁掉最旧的)。
    /// 用途:程序重启后不把已见过的通知当成新通知重复提醒。
    /// </summary>
    internal sealed class AihotSeenStore
    {
        internal const int MaxEntries = 400;

        private readonly string _path;
        private readonly List<string> _ordered = new List<string>();
        private readonly HashSet<string> _lookup = new HashSet<string>(StringComparer.Ordinal);

        public AihotSeenStore(string path)
        {
            _path = path;
        }

        public static string DefaultPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexMeter",
                "aihot-seen-v1.txt");
        }

        /// <summary>加载时「已见库文件是否存在」——用于区分首次安装/文件被删的冷启动。</summary>
        public bool FileExistedAtLoad { get; private set; }

        public int Count { get { return _ordered.Count; } }

        public void Load()
        {
            FileExistedAtLoad = false;
            try
            {
                if (!File.Exists(_path)) return;
                FileExistedAtLoad = true;
                foreach (var line in File.ReadAllLines(_path, Encoding.UTF8))
                {
                    var value = line == null ? null : line.Trim();
                    if (!string.IsNullOrEmpty(value)) Add(value);
                }
                TrimToLimit();
            }
            catch
            {
                // 读取失败按「没有已见库」继续,不抛出
            }
        }

        public bool Contains(string fingerprint)
        {
            return !string.IsNullOrEmpty(fingerprint) && _lookup.Contains(fingerprint);
        }

        /// <summary>加入一条指纹;返回是否为「新增」(此前从未见过)。</summary>
        public bool Add(string fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint)) return false;
            if (!_lookup.Add(fingerprint)) return false;
            _ordered.Add(fingerprint);
            TrimToLimit();
            return true;
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllLines(_path, _ordered.ToArray(), new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        private void TrimToLimit()
        {
            while (_ordered.Count > MaxEntries)
            {
                _lookup.Remove(_ordered[0]);
                _ordered.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// AIHOT 公开 API(只读、无需密钥)的 Codex 重置监控旁路服务。
    /// 设计原则:任何网络/解析失败都只影响本模块自身状态,不抛出、不影响主程序;
    /// 遵守官方要求:10 分钟轮询、If-None-Match/ETag、gzip、Retry-After、不并发重试。
    /// </summary>
    internal sealed class AihotService : IDisposable
    {
        internal const string ResetsRecentUrl = "https://aihot.news/api/v1/codex-resets/recent";
        internal const string SiteUrl = "https://aihot.news/";
        internal const string TermsUrl = "https://aihot.news/terms";
        internal const string AllowedHost = "aihot.news";

        internal const int AutoRefreshSeconds = 600;     // 官方建议:10 分钟
        internal const int ManualMinSeconds = 60;        // 手动刷新最小间隔(对齐 Cache-Control max-age=60)
        private const int StartupDelaySeconds = 15;      // 启动后延迟首次获取
        private const int MaxBackoffSeconds = 3600;      // 退避上限 60 分钟
        private const int RateLimitMaxSeconds = 1800;    // 限流等待上限 30 分钟
        private const int RequestTimeoutMs = 15000;
        private const long MaxBodyBytes = 1048576;       // 响应体上限(正常仅约 13KB)

        private readonly object _gate = new object();
        private readonly string _cachePath;
        private readonly AihotSeenStore _seen;
        private readonly Timer _timer;
        private AihotResetsSnapshot _snapshot = new AihotResetsSnapshot();
        private string _etag;
        private string _actorId;
        private string _lastBody;
        private DateTimeOffset _nextAutoUtc;
        private DateTimeOffset _nextAllowedUtc;
        private int _failureStreak;
        private int _busy;
        private bool _started;
        private bool _disposed;
        private bool _firstSuccessHandled;   // 本次会话是否已处理过首个成功获取
        private bool _coldStart;             // 首次安装/已见库文件缺失:首个成功获取静默建立基线

        /// <summary>
        /// 检测到「本地从未见过」的新重置事件时触发(参数 = 本次新增指纹数)。
        /// 在后台线程触发,订阅方需自行切回 UI 线程;订阅方异常不会影响本服务。
        /// </summary>
        public event Action<int> NewResetsDetected;

        public AihotService()
        {
            _cachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexMeter", "aihot-resets-cache-v1.json");
            _seen = new AihotSeenStore(AihotSeenStore.DefaultPath());
            _seen.Load();
            _coldStart = !_seen.FileExistedAtLoad;
            LoadCache();
            MergeCacheFingerprintsIntoSeen();
            _timer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>把缓存快照中已有事件的指纹静默并入已见库(不触发提醒,覆盖「缓存里的旧通知」)。</summary>
        private void MergeCacheFingerprintsIntoSeen()
        {
            try
            {
                var changed = false;
                foreach (var item in _snapshot.Events)
                {
                    var fingerprint = ComputeFingerprint(item);
                    if (fingerprint != null && _seen.Add(fingerprint)) changed = true;
                }
                if (changed) _seen.Save();
            }
            catch
            {
            }
        }

        /// <summary>启动后台调度(首次约 15 秒后获取,此后每 10 分钟一次)。</summary>
        public void Start()
        {
            lock (_gate)
            {
                if (_disposed || _started) return;
                _started = true;
                _nextAutoUtc = DateTimeOffset.UtcNow.AddSeconds(StartupDelaySeconds);
                _nextAllowedUtc = _nextAutoUtc;
            }
            try { _timer.Change(StartupDelaySeconds * 1000, 60000); }
            catch { }
        }

        /// <summary>手动刷新(尊重 60 秒下限与退避等待;不满足条件时静默忽略,只更新等待状态)。</summary>
        public void RequestManualRefresh()
        {
            try
            {
                if (_disposed) return;
                var now = DateTimeOffset.UtcNow;
                bool allowed;
                lock (_gate) { allowed = now >= _nextAllowedUtc; }
                if (allowed) TryStartFetch();
            }
            catch
            {
            }
        }

        /// <summary>线程安全地获取当前快照副本(UI 定时拉取)。</summary>
        public AihotResetsSnapshot GetSnapshot()
        {
            lock (_gate)
            {
                return _snapshot.Clone();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            try { _timer.Dispose(); } catch { }
        }

        // ---------- 调度 ----------

        private void OnTick(object state)
        {
            try
            {
                if (_disposed) return;
                var now = DateTimeOffset.UtcNow;
                bool due;
                lock (_gate) { due = now >= _nextAutoUtc && now >= _nextAllowedUtc; }
                if (due) TryStartFetch();
            }
            catch
            {
            }
        }

        private void TryStartFetch()
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;   // 单飞:绝不并发
            lock (_gate)
            {
                if (_disposed)
                {
                    Interlocked.Exchange(ref _busy, 0);
                    return;
                }
                // 立即占住下一分钟,防止连点造成重复请求;结束后按实际结果重设
                _nextAllowedUtc = DateTimeOffset.UtcNow.AddSeconds(ManualMinSeconds);
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Fetch(); }
                catch { }
                finally { Interlocked.Exchange(ref _busy, 0); }
            });
        }

        // ---------- 网络与状态 ----------

        private void Fetch()
        {
            var now = DateTimeOffset.UtcNow;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(ResetsRecentUrl);
                request.Method = "GET";
                request.UserAgent = "CodexQuotaTray/" + CurrentVersion() + " aihot-actor/" + _actorId;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                request.Timeout = RequestTimeoutMs;
                request.ReadWriteTimeout = RequestTimeoutMs;
                request.AllowAutoRedirect = true;

                string etag;
                lock (_gate) { etag = _etag; }
                if (!string.IsNullOrEmpty(etag)) request.Headers["If-None-Match"] = etag;

                HttpWebResponse response = null;
                try
                {
                    response = (HttpWebResponse)request.GetResponse();
                }
                catch (WebException exception)
                {
                    response = exception.Response as HttpWebResponse;
                    if (response == null)
                    {
                        ApplyFailure("network", exception.Status.ToString(), 0, 0, now);
                        return;
                    }
                }

                using (response)
                {
                    var code = (int)response.StatusCode;
                    if (code == 304)
                    {
                        ApplyNotModified(now);
                        return;
                    }
                    if (code != 200)
                    {
                        ApplyFailure("http", "HTTP " + code, code, ParseRetryAfter(response), now);
                        return;
                    }
                    if (!IsAllowedFinalUri(response.ResponseUri))
                    {
                        ApplyFailure("http", "unexpected redirect", 0, 0, now);
                        return;
                    }
                    if (response.ContentLength > MaxBodyBytes)
                    {
                        ApplyFailure("parse", "response too large", 0, 0, now);
                        return;
                    }
                    string body;
                    using (var stream = response.GetResponseStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        body = reader.ReadToEnd();
                    }
                    var parsed = ParseResetsJson(body);
                    if (parsed == null)
                    {
                        ApplyFailure("parse", "invalid json", 0, 0, now);
                        return;
                    }
                    ApplySuccess(response, body, parsed, now);
                }
            }
            catch (Exception exception)
            {
                ApplyFailure("network", exception.Message, 0, 0, now);
            }
        }

        private void ApplySuccess(HttpWebResponse response, string body, AihotResetsSnapshot parsed, DateTimeOffset now)
        {
            var notifyCount = 0;
            lock (_gate)
            {
                _failureStreak = 0;
                var newEtag = response.Headers[HttpResponseHeader.ETag];
                if (!string.IsNullOrEmpty(newEtag)) _etag = newEtag;
                _lastBody = body;
                parsed.FetchedAtLocal = now.ToLocalTime();
                parsed.LastCheckLocal = now.ToLocalTime();
                parsed.StatusKey = "ok";
                parsed.StatusDetail = null;
                parsed.NextAttemptUtc = null;
                parsed.Version = _snapshot.Version + 1;
                _snapshot = parsed;
                _nextAutoUtc = now.AddSeconds(AutoRefreshSeconds);
                _nextAllowedUtc = now.AddSeconds(ManualMinSeconds);
                SaveCache();

                // ---- 新通知检测(旁路;任何异常都不影响主流程) ----
                // 与已见库求差,只有「本地从未见过」的 fingerprint 才算新通知。
                // 首次成功获取:已见库存在时允许提醒(程序关闭期间新增的事件);
                // 冷启动(首次安装/已见库缺失)时静默建立基线,不提醒。
                try
                {
                    var added = 0;
                    foreach (var item in parsed.Events)
                    {
                        var fingerprint = ComputeFingerprint(item);
                        if (fingerprint != null && _seen.Add(fingerprint)) added++;
                    }
                    if (added > 0) _seen.Save();
                    var isFirstSuccess = !_firstSuccessHandled;
                    if (added > 0 && (!isFirstSuccess || !_coldStart)) notifyCount = added;
                    _firstSuccessHandled = true;
                }
                catch
                {
                    notifyCount = 0;
                }
            }
            if (notifyCount > 0)
            {
                var handler = NewResetsDetected;
                if (handler != null)
                {
                    try { handler(notifyCount); }
                    catch { }
                }
            }
        }

        private void ApplyNotModified(DateTimeOffset now)
        {
            lock (_gate)
            {
                _failureStreak = 0;
                var current = _snapshot.Clone();
                current.LastCheckLocal = now.ToLocalTime();
                current.StatusKey = "not-modified";
                current.StatusDetail = null;
                current.NextAttemptUtc = null;
                current.Version = _snapshot.Version + 1;
                _snapshot = current;
                _nextAutoUtc = now.AddSeconds(AutoRefreshSeconds);
                _nextAllowedUtc = now.AddSeconds(ManualMinSeconds);
                SaveCache();
            }
        }

        private void ApplyFailure(string key, string detail, int httpCode, int retryAfterSeconds, DateTimeOffset now)
        {
            lock (_gate)
            {
                _failureStreak++;
                int waitSeconds;
                if ((httpCode == 429 || httpCode == 503) && retryAfterSeconds > 0)
                {
                    waitSeconds = Math.Min(Math.Max(retryAfterSeconds, ManualMinSeconds), RateLimitMaxSeconds);
                }
                else if (httpCode == 429 || httpCode == 503)
                {
                    waitSeconds = 300;
                }
                else if (httpCode == 400 || httpCode == 403 || httpCode == 405)
                {
                    waitSeconds = 1800;
                }
                else
                {
                    // 网络/5xx:指数退避 5 → 10 → 20 → 40 → 60(封顶)分钟
                    var power = Math.Min(_failureStreak - 1, 4);
                    waitSeconds = Math.Min(MaxBackoffSeconds, 300 * (1 << power));
                }
                var current = _snapshot.Clone();
                current.StatusKey = (httpCode == 429 || httpCode == 503) ? "ratelimited" : key;
                current.StatusDetail = detail;
                current.LastCheckLocal = now.ToLocalTime();
                current.NextAttemptUtc = now.AddSeconds(waitSeconds);
                current.Version = _snapshot.Version + 1;
                _snapshot = current;
                _nextAutoUtc = now.AddSeconds(waitSeconds);
                _nextAllowedUtc = _nextAutoUtc;
            }
        }

        private static bool IsAllowedFinalUri(Uri uri)
        {
            return uri != null
                && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && string.Equals(uri.Host, AllowedHost, StringComparison.OrdinalIgnoreCase);
        }

        private static int ParseRetryAfter(HttpWebResponse response)
        {
            try
            {
                var value = response.Headers[HttpResponseHeader.RetryAfter];
                int seconds;
                if (!string.IsNullOrEmpty(value)
                    && int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds)
                    && seconds > 0)
                {
                    return seconds;
                }
            }
            catch
            {
            }
            return 0;
        }

        private static string CurrentVersion()
        {
            try
            {
                return Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
            catch
            {
                return "0.0.0.0";
            }
        }

        // ---------- 解析(供单元测试直接调用;任何异常都返回 null) ----------

        internal static AihotResetsSnapshot ParseResetsJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) return null;

                var snapshot = new AihotResetsSnapshot();
                snapshot.EventCount = ToInt(Get(root, "count"));
                snapshot.DataCheckedAtText = FormatApiTime(ToStr(Get(root, "checkedAt")));

                var monitor = AsDict(Get(root, "monitor"));
                if (monitor != null)
                {
                    snapshot.PendingCount = ToInt(Get(monitor, "pendingCount"));
                }

                var events = Get(root, "events") as object[];
                if (events != null)
                {
                    foreach (var value in events)
                    {
                        var dict = value as Dictionary<string, object>;
                        if (dict == null) continue;
                        var item = new AihotResetEventItem();
                        item.Id = ToStr(Get(dict, "id"));
                        item.DisplayLabel = ToStr(Get(dict, "displayLabel"));
                        if (string.IsNullOrWhiteSpace(item.DisplayLabel)) item.DisplayLabel = ToStr(Get(dict, "label"));
                        item.Title = ToStr(Get(dict, "title"));
                        item.Status = ToStr(Get(dict, "status"));
                        item.IsPending = !string.Equals(item.Status, "confirmed", StringComparison.OrdinalIgnoreCase);
                        item.Type = ToStr(Get(dict, "type"));
                        item.OccurredOnRaw = ToStr(Get(dict, "occurredOn"));

                        var schedule = AsDict(Get(dict, "schedule"));
                        if (schedule != null)
                        {
                            item.ScheduleText = ToStr(Get(schedule, "label"));
                            item.SchedulePrecision = ToStr(Get(schedule, "precision"));
                            item.ScheduleFromRaw = ToStr(Get(schedule, "from"));
                            item.ScheduleThroughRaw = ToStr(Get(schedule, "through"));
                        }

                        item.TimeText = FormatApiTime(ToStr(Get(dict, "occurredOn")));
                        if (string.IsNullOrWhiteSpace(item.TimeText)) item.TimeText = FormatApiTime(ToStr(Get(dict, "confirmedAt")));
                        if (string.IsNullOrWhiteSpace(item.TimeText)) item.TimeText = FormatApiTime(ToStr(Get(dict, "updatedAt")));
                        if (string.IsNullOrWhiteSpace(item.TimeText)) item.TimeText = FormatApiTime(ToStr(Get(dict, "createdAt")));

                        var posts = Get(dict, "posts") as object[];
                        if (posts != null)
                        {
                            foreach (var postValue in posts)
                            {
                                var post = postValue as Dictionary<string, object>;
                                if (post == null) continue;
                                if (string.IsNullOrWhiteSpace(item.SummaryText)) item.SummaryText = ToStr(Get(post, "text"));
                                if (string.IsNullOrWhiteSpace(item.PrimaryUrl)) item.PrimaryUrl = ToStr(Get(post, "url"));
                                var postId = ToStr(Get(post, "id"));
                                if (!string.IsNullOrWhiteSpace(postId))
                                {
                                    if (item.PostIds == null) item.PostIds = new List<string>();
                                    item.PostIds.Add(postId);
                                }
                            }
                        }
                        snapshot.Events.Add(item);
                    }
                }

                if (snapshot.EventCount <= 0) snapshot.EventCount = snapshot.Events.Count;
                return snapshot;
            }
            catch
            {
                return null;
            }
        }

        internal static string FormatApiTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            DateTimeOffset parsed;
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) return null;
            // 接口时间为 +08:00(北京时间),直接按原时区重排格式展示
            return value.Length == 10
                ? parsed.ToString("MM-dd", CultureInfo.InvariantCulture)
                : parsed.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        // ---------- 新通知 fingerprint(供差异检测/去重;任何异常返回 null) ----------

        /// <summary>
        /// 计算事件的稳定指纹。只包含「实质性」字段,刻意忽略:
        /// checkedAt / today / updatedAt / posts 数组顺序 / JSON 键顺序 / ETag ——
        /// 这些变化不表示通知内容有实际变化,不应触发提醒。
        /// 组成: id|type|status|title|occurredOn|schedule(precision|from|through|label)|排序后的帖子 id 集合
        /// </summary>
        internal static string ComputeFingerprint(AihotResetEventItem item)
        {
            try
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Id)) return null;
                var postIds = new List<string>();
                if (item.PostIds != null)
                {
                    foreach (var postId in item.PostIds)
                    {
                        if (!string.IsNullOrWhiteSpace(postId)) postIds.Add(postId);
                    }
                }
                postIds.Sort(StringComparer.Ordinal);   // 顺序无关:排序后再比较

                var parts = new List<string>();
                parts.Add(SanitizeFingerprintPart(item.Id));
                parts.Add(SanitizeFingerprintPart(item.Type));
                parts.Add(SanitizeFingerprintPart(item.Status));
                parts.Add(SanitizeFingerprintPart(item.Title));
                parts.Add(SanitizeFingerprintPart(item.OccurredOnRaw));
                parts.Add(SanitizeFingerprintPart(item.SchedulePrecision));
                parts.Add(SanitizeFingerprintPart(item.ScheduleFromRaw));
                parts.Add(SanitizeFingerprintPart(item.ScheduleThroughRaw));
                parts.Add(SanitizeFingerprintPart(item.ScheduleText));
                parts.Add(SanitizeFingerprintPart(string.Join(",", postIds.ToArray())));
                return string.Join("|", parts.ToArray());
            }
            catch
            {
                return null;
            }
        }

        private static string SanitizeFingerprintPart(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            // 换行必须替换,保证 fingerprint 能按「每行一条」持久化
            return value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        // ---------- 缓存(%LOCALAPPDATA%\CodexMeter\aihot-resets-cache-v1.json) ----------

        private void LoadCache()
        {
            try
            {
                if (File.Exists(_cachePath))
                {
                    var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    var root = serializer.DeserializeObject(File.ReadAllText(_cachePath, Encoding.UTF8)) as Dictionary<string, object>;
                    if (root != null)
                    {
                        var actor = ToStr(Get(root, "actorId"));
                        if (!string.IsNullOrWhiteSpace(actor)) _actorId = actor;
                        _etag = ToStr(Get(root, "etag"));

                        var parsed = ParseResetsJson(ToStr(Get(root, "body")));
                        if (parsed != null)
                        {
                            parsed.StatusKey = "cache";
                            DateTimeOffset fetched;
                            if (DateTimeOffset.TryParse(ToStr(Get(root, "fetchedAt")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out fetched))
                                parsed.FetchedAtLocal = fetched.ToLocalTime();
                            DateTimeOffset checkedAt;
                            if (DateTimeOffset.TryParse(ToStr(Get(root, "lastChecked")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out checkedAt))
                                parsed.LastCheckLocal = checkedAt.ToLocalTime();
                            _lastBody = ToStr(Get(root, "body"));
                            _snapshot = parsed;
                        }
                    }
                }
            }
            catch
            {
            }
            if (string.IsNullOrWhiteSpace(_actorId)) _actorId = Guid.NewGuid().ToString();
        }

        private void SaveCache()
        {
            try
            {
                if (string.IsNullOrEmpty(_lastBody)) return;
                var payload = new Dictionary<string, object>();
                payload["actorId"] = _actorId;
                payload["etag"] = _etag;
                payload["fetchedAt"] = _snapshot.FetchedAtLocal.HasValue ? _snapshot.FetchedAtLocal.Value.ToString("o") : null;
                payload["lastChecked"] = _snapshot.LastCheckLocal.HasValue ? _snapshot.LastCheckLocal.Value.ToString("o") : null;
                payload["body"] = _lastBody;
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var directory = Path.GetDirectoryName(_cachePath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(_cachePath, serializer.Serialize(payload), new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        // ---------- 小工具 ----------

        private static object Get(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value : null;
        }

        private static Dictionary<string, object> AsDict(object value)
        {
            return value as Dictionary<string, object>;
        }

        private static string ToStr(object value)
        {
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static int ToInt(object value)
        {
            int parsed;
            return int.TryParse(ToStr(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }
    }
}
