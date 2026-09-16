using System;
using System.Collections.Generic;

namespace CodexQuotaTray
{
    internal enum QuotaWindowKind
    {
        Other,
        FiveHour,
        Weekly
    }

    internal sealed class QuotaWindow
    {
        public string Name { get; set; }
        public string Key { get; set; }
        public QuotaWindowKind Kind { get; set; }
        public double UsedPercent { get; set; }
        public int WindowMinutes { get; set; }
        public DateTimeOffset? ResetsAt { get; set; }

        public int RemainingPercent
        {
            get
            {
                var remaining = (int)Math.Round(100d - UsedPercent);
                return Math.Max(0, Math.Min(100, remaining));
            }
        }

        public int TimeRemainingPercent
        {
            get
            {
                if (!ResetsAt.HasValue || WindowMinutes <= 0) return -1;
                var percent = (int)Math.Round((ResetsAt.Value - DateTimeOffset.Now).TotalMinutes * 100d / WindowMinutes);
                return Math.Max(0, Math.Min(100, percent));
            }
        }
    }

    internal sealed class QuotaSnapshot
    {
        public QuotaSnapshot()
        {
            Windows = new List<QuotaWindow>();
            OrdinaryUsageAllowed = true;
        }

        public DateTimeOffset CapturedAt { get; set; }
        public string LimitId { get; set; }
        public string PlanType { get; set; }
        public List<QuotaWindow> Windows { get; private set; }
        public bool OrdinaryUsageAllowed { get; set; }
        public QuotaWindow ReserveWindow { get; set; }

        public bool IsReserveActive
        {
            get { return ReserveWindow != null && (!OrdinaryUsageAllowed || ReserveWindow.UsedPercent > 0); }
        }

        public QuotaWindow DisplayWeeklyWindow
        {
            get { return IsReserveActive ? ReserveWindow : WeeklyWindow; }
        }

        public int MostConstrainedRemaining
        {
            get
            {
                if (Windows.Count == 0)
                {
                    return 0;
                }

                var value = 100;
                foreach (var window in Windows)
                {
                    value = Math.Min(value, window.RemainingPercent);
                }

                return value;
            }
        }

        public QuotaWindow FiveHourWindow
        {
            get
            {
                foreach (var window in Windows)
                {
                    if (window.Kind == QuotaWindowKind.FiveHour) return window;
                }

                QuotaWindow shortest = null;
                foreach (var window in Windows)
                {
                    if (window.WindowMinutes <= 0) continue;
                    if (shortest == null || window.WindowMinutes < shortest.WindowMinutes) shortest = window;
                }
                return shortest ?? (Windows.Count > 0 ? Windows[0] : null);
            }
        }

        public QuotaWindow WeeklyWindow
        {
            get
            {
                foreach (var window in Windows)
                {
                    if (window.Kind == QuotaWindowKind.Weekly) return window;
                }
                return null;
            }
        }
    }

    internal sealed class TokenActivitySample
    {
        public DateTimeOffset CapturedAt { get; set; }
        public long Tokens { get; set; }
        public string SourceKey { get; set; }
        public DateTimeOffset SourceStartedAt { get; set; }
    }

    internal sealed class QuotaUsageSample
    {
        public DateTimeOffset CapturedAt { get; set; }
        public string WindowKey { get; set; }
        public QuotaWindowKind WindowKind { get; set; }
        public int WindowMinutes { get; set; }
        public int RemainingPercent { get; set; }
        public DateTimeOffset? ResetsAt { get; set; }
    }

    internal static class QuotaUsageMath
    {
        internal const int PaceLeadTolerancePercent = 20;
        internal const int FiveHourBucketMinutes = 10;

        public static bool IsConsumptionFast(int remainingPercent, int timeRemainingPercent)
        {
            return timeRemainingPercent - remainingPercent > PaceLeadTolerancePercent;
        }

        public static int FiveHourBucketIndex(DateTimeOffset capturedAt, DateTimeOffset cycleStart)
        {
            return (int)Math.Floor((capturedAt - cycleStart).TotalMinutes / FiveHourBucketMinutes);
        }

        public static double CalculateUsedPercent(QuotaUsageSample previous, QuotaUsageSample current, int windowMinutes)
        {
            if (previous == null || current == null) return 0;
            var resetChanged = previous.ResetsAt.HasValue && current.ResetsAt.HasValue &&
                (current.ResetsAt.Value - previous.ResetsAt.Value).TotalMinutes > Math.Max(1, windowMinutes * 0.5d);
            if (resetChanged) return Math.Max(0, 100 - current.RemainingPercent);
            return Math.Max(0, previous.RemainingPercent - current.RemainingPercent);
        }

        public static bool IsSameWindow(QuotaUsageSample sample, QuotaWindow window)
        {
            if (sample == null || window == null || sample.WindowMinutes != window.WindowMinutes) return false;
            if (string.IsNullOrWhiteSpace(sample.WindowKey) || string.IsNullOrWhiteSpace(window.Key)) return true;
            return string.Equals(sample.WindowKey, window.Key, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class TokenActivityMath
    {
        public static long CalculateDelta(
            long? previousTokens,
            long currentTokens,
            DateTimeOffset sourceStartedAt,
            DateTimeOffset rangeStart)
        {
            if (currentTokens <= 0) return 0;
            if (previousTokens.HasValue)
                return currentTokens >= previousTokens.Value ? currentTokens - previousTokens.Value : currentTokens;
            return sourceStartedAt >= rangeStart ? currentTokens : 0;
        }
    }

    internal sealed class QuotaReadResult
    {
        public QuotaReadResult()
        {
            Activity = new List<TokenActivitySample>();
            ActivityTimeline = new List<TokenActivitySample>();
            QuotaHistory = new List<QuotaUsageSample>();
        }

        public bool IsCodexRunning { get; set; }
        public QuotaSnapshot Snapshot { get; set; }
        public List<TokenActivitySample> Activity { get; set; }
        public List<TokenActivitySample> ActivityTimeline { get; set; }
        public List<QuotaUsageSample> QuotaHistory { get; set; }
        public string Error { get; set; }
    }
}
