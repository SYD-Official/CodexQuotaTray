using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CodexQuotaTray
{
    internal sealed class CompletionMonitorResult
    {
        public bool HasUnread { get; set; }
        public bool StateChanged { get; set; }
        public bool ReadFailed { get; set; }
        public int UnreadCount { get; set; }
    }

    internal sealed class CodexCompletionMonitor
    {
        internal const int IdleIntervalMilliseconds = 20000;
        internal const int ActiveIntervalMilliseconds = 3000;
        private const int MaximumActiveLogFiles = 12;
        private const int MaximumRememberedTurns = 256;
        private readonly string _logsRoot;
        private readonly Dictionary<string, long> _positions = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _pendingConversations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _seenTurns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _seenTurnOrder = new Queue<string>();
        private bool _enabled;

        public CodexCompletionMonitor()
            : this(ResolveLogsRoot())
        {
        }

        internal CodexCompletionMonitor(string logsRoot)
        {
            _logsRoot = logsRoot;
        }

        internal int PendingCount { get { return _pendingConversations.Count; } }

        internal static int PollInterval(bool hasUnread)
        {
            return hasUnread ? ActiveIntervalMilliseconds : IdleIntervalMilliseconds;
        }

        public void Enable()
        {
            _enabled = true;
            _positions.Clear();
            _pendingConversations.Clear();
            _seenTurns.Clear();
            _seenTurnOrder.Clear();
            if (string.IsNullOrWhiteSpace(_logsRoot) || !Directory.Exists(_logsRoot)) return;

            try
            {
                foreach (var file in Directory.EnumerateFiles(_logsRoot, "*.log", SearchOption.AllDirectories))
                {
                    try { _positions[file] = new FileInfo(file).Length; }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Disable()
        {
            _enabled = false;
            _positions.Clear();
            _pendingConversations.Clear();
            _seenTurns.Clear();
            _seenTurnOrder.Clear();
        }

        public void AcknowledgeAll()
        {
            _pendingConversations.Clear();
        }

        public CompletionMonitorResult Poll()
        {
            var before = _pendingConversations.Count;
            var failed = false;
            if (!_enabled)
            {
                return new CompletionMonitorResult();
            }

            try
            {
                if (string.IsNullOrWhiteSpace(_logsRoot) || !Directory.Exists(_logsRoot))
                {
                    failed = true;
                }
                else
                {
                    var files = Directory.EnumerateFiles(_logsRoot, "*.log", SearchOption.AllDirectories)
                        .Select(path => new FileInfo(path))
                        .OrderByDescending(file => file.LastWriteTimeUtc)
                        .Take(MaximumActiveLogFiles)
                        .ToList();
                    foreach (var file in files)
                    {
                        try { ReadAppendedLines(file.FullName); }
                        catch (IOException) { failed = true; }
                        catch (UnauthorizedAccessException) { failed = true; }
                    }
                }
            }
            catch (IOException) { failed = true; }
            catch (UnauthorizedAccessException) { failed = true; }

            return new CompletionMonitorResult
            {
                HasUnread = _pendingConversations.Count > 0,
                StateChanged = before != _pendingConversations.Count,
                ReadFailed = failed,
                UnreadCount = _pendingConversations.Count
            };
        }

        internal bool ProcessLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (line.IndexOf("[desktop-notifications] show turn-complete", StringComparison.Ordinal) >= 0)
            {
                var conversationId = ExtractValue(line, "conversationId=");
                var turnId = ExtractValue(line, "turnId=");
                if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(turnId) || !_seenTurns.Add(turnId)) return false;
                _seenTurnOrder.Enqueue(turnId);
                while (_seenTurnOrder.Count > MaximumRememberedTurns)
                    _seenTurns.Remove(_seenTurnOrder.Dequeue());
                _pendingConversations[conversationId] = turnId;
                return true;
            }

            if (line.IndexOf("thread_stream_view_activity_changed", StringComparison.Ordinal) >= 0 &&
                line.IndexOf("resumeState=resumed", StringComparison.Ordinal) >= 0)
            {
                var conversationId = ExtractValue(line, "conversationId=");
                return !string.IsNullOrWhiteSpace(conversationId) && _pendingConversations.Remove(conversationId);
            }
            return false;
        }

        private void ReadAppendedLines(string path)
        {
            long position;
            if (!_positions.TryGetValue(path, out position)) position = 0;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (position > stream.Length) position = 0;
                stream.Seek(position, SeekOrigin.Begin);
                using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null) ProcessLine(line);
                }
                _positions[path] = stream.Length;
            }
        }

        private static string ExtractValue(string line, string key)
        {
            var start = line.IndexOf(key, StringComparison.Ordinal);
            if (start < 0) return null;
            start += key.Length;
            var end = line.IndexOf(' ', start);
            return (end < 0 ? line.Substring(start) : line.Substring(start, end - start)).Trim();
        }

        private static string ResolveLogsRoot()
        {
            try
            {
                var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
                if (!Directory.Exists(packages)) return null;
                var package = Directory.EnumerateDirectories(packages, "OpenAI.Codex_*").FirstOrDefault();
                if (package == null) return null;
                return Path.Combine(package, "LocalCache", "Local", "Codex", "Logs");
            }
            catch
            {
                return null;
            }
        }
    }
}
