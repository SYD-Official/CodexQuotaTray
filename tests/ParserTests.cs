using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using CodexQuotaTray;

internal static class ParserTests
{
    private static int Main(string[] args)
    {
        var defaults = new AppSettings();
        Assert(defaults.GlassEnabled && defaults.GlassOpacityPercent == 40,
            "柔光玻璃应默认开启且不透明度为 40%");
        Assert(defaults.QuotaPrimaryEmphasis, "额度详情应默认使用主次布局");
        Assert(!defaults.CompletionReminderEnabled, "任务完成闪烁提醒应默认关闭");
        Assert(!QuotaUsageMath.IsConsumptionFast(83, 92), "额度剩余 83%、时间剩余 92% 应处于启动缓冲内");
        Assert(!QuotaUsageMath.IsConsumptionFast(70, 90), "额度消耗恰好超前 20 个百分点时仍应视为正常");
        Assert(QuotaUsageMath.IsConsumptionFast(69, 90), "额度消耗超前超过 20 个百分点时应提示偏快");
        var cycleStart = DateTimeOffset.Parse("2026-08-26T10:00:00+08:00");
        Assert(QuotaUsageMath.FiveHourBucketIndex(cycleStart, cycleStart) == 0, "周期起点应进入第一个十分钟桶");
        Assert(QuotaUsageMath.FiveHourBucketIndex(cycleStart.AddMinutes(299), cycleStart) == 29, "周期末应进入第 30 个桶");
        Assert(QuotaUsageMath.FiveHourBucketIndex(cycleStart.AddMinutes(300), cycleStart) == 30, "重置点应落在当前周期范围外");
        Assert(TokenActivityMath.CalculateDelta(null, 1200, cycleStart, cycleStart) == 1200,
            "周期内新会话的首个累计值应计入当前周期");
        Assert(TokenActivityMath.CalculateDelta(1200, 1850, cycleStart, cycleStart) == 650,
            "连续累计 token 应只统计正增量");
        Assert(TokenActivityMath.CalculateDelta(null, 1850, cycleStart.AddHours(-1), cycleStart) == 0,
            "缺少周期前基线的旧会话不应把历史累计值灌入当前周期");
        Assert(TokenActivityMath.CalculateDelta(1850, 300, cycleStart, cycleStart) == 300,
            "累计计数器重置后应从新计数重新统计");

        const string fixture = "{\"timestamp\":\"2026-08-10T10:27:30.353Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"total_tokens\":123456}},\"rate_limits\":{\"limit_id\":\"codex\",\"plan_type\":\"plus\",\"primary\":{\"used_percent\":48.0,\"window_minutes\":300,\"resets_at\":1786884758},\"secondary\":{\"used_percent\":20,\"window_minutes\":10080,\"resets_at\":1786400000}}}}";

        QuotaSnapshot snapshot;
        Assert(SessionQuotaReader.TryParseTokenCountLine(fixture, out snapshot), "应能解析 token_count 事件");
        Assert(snapshot.Windows.Count == 2, "应解析两个额度窗口");
        Assert(snapshot.MostConstrainedRemaining == 52, "最紧额度应剩余 52%");
        Assert(snapshot.Windows[0].WindowMinutes == 300, "应保留额度窗口时长");
        Assert(snapshot.FiveHourWindow != null && snapshot.FiveHourWindow.Kind == QuotaWindowKind.FiveHour,
            "300 分钟窗口应识别为 5 小时额度");
        Assert(snapshot.WeeklyWindow != null && snapshot.WeeklyWindow.Kind == QuotaWindowKind.Weekly,
            "10080 分钟窗口应识别为每周额度");
        Assert(snapshot.FiveHourWindow.RemainingPercent == 52 && snapshot.WeeklyWindow.RemainingPercent == 80,
            "主窗口与周窗口余额应分别保留");
        Assert(snapshot.PlanType == "plus", "存在套餐字段时应原样保留");

        const string reversedFixture = "{\"timestamp\":\"2026-08-10T10:27:30.353Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"limit_id\":\"codex\",\"primary\":{\"used_percent\":20,\"window_minutes\":10080},\"secondary\":{\"used_percent\":48,\"window_minutes\":300}}}}";
        QuotaSnapshot reversed;
        Assert(SessionQuotaReader.TryParseTokenCountLine(reversedFixture, out reversed), "交换窗口顺序后仍应解析");
        Assert(reversed.FiveHourWindow.WindowMinutes == 300 && reversed.WeeklyWindow.WindowMinutes == 10080,
            "额度角色不应依赖 primary/secondary 顺序");

        const string multiBucketFixture = "{\"timestamp\":\"2026-09-15T05:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"limit_id\":\"codex\",\"plan_type\":\"plus\",\"primary\":{\"used_percent\":10,\"window_minutes\":300},\"secondary\":{\"used_percent\":51,\"window_minutes\":10080},\"rate_limits_by_limit_id\":{\"base_model_inference\":{\"limit_name\":\"gpt-reserve\",\"primary\":{\"used_percent\":0,\"window_minutes\":10080}},\"codex\":{\"primary\":{\"used_percent\":10,\"window_minutes\":300},\"secondary\":{\"used_percent\":51,\"window_minutes\":10080}}}}}}";
        QuotaSnapshot multiBucket;
        Assert(SessionQuotaReader.TryParseTokenCountLine(multiBucketFixture, out multiBucket), "应能解析包含备用额度池的旧快照");
        Assert(multiBucket.Windows.Count == 2 && multiBucket.FiveHourWindow.RemainingPercent == 90 && multiBucket.WeeklyWindow.RemainingPercent == 49,
            "旧快照应优先读取 codex 常规额度而不是 gpt-reserve 备用额度");
        Assert(multiBucket.ReserveWindow != null && multiBucket.ReserveWindow.RemainingPercent == 100 && !multiBucket.IsReserveActive,
            "旧快照应识别备用额度，但未使用时不切换显示");

        const string appServerFixture = "{\"id\":2,\"result\":{\"ordinaryUsageAllowed\":true,\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":10,\"windowDurationMins\":300,\"resetsAt\":1789466019},\"secondary\":{\"usedPercent\":51,\"windowDurationMins\":10080,\"resetsAt\":1789805387},\"planType\":\"plus\"},\"rateLimitsByLimitId\":{\"base_model_inference\":{\"limitId\":\"base_model_inference\",\"limitName\":\"gpt-reserve\",\"primary\":{\"usedPercent\":0,\"windowDurationMins\":10080}},\"codex\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":10,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":51,\"windowDurationMins\":10080}}}}}";
        QuotaSnapshot appServerSnapshot;
        Assert(SessionQuotaReader.TryParseAppServerRateLimitsResponse(appServerFixture, out appServerSnapshot), "应能解析 app-server 当前额度响应");
        Assert(appServerSnapshot.Windows.Count == 2 && appServerSnapshot.FiveHourWindow.RemainingPercent == 90 && appServerSnapshot.WeeklyWindow.RemainingPercent == 49,
            "app-server 响应应只显示常规 5 小时和每周额度");
        Assert(appServerSnapshot.PlanType == "plus", "app-server 响应应保留套餐类型");
        Assert(appServerSnapshot.ReserveWindow != null && !appServerSnapshot.IsReserveActive && appServerSnapshot.DisplayWeeklyWindow == appServerSnapshot.WeeklyWindow,
            "备用额度未使用时应继续显示常规周额度");

        var activeReserveFixture = appServerFixture
            .Replace("\"ordinaryUsageAllowed\":true", "\"ordinaryUsageAllowed\":false")
            .Replace("\"usedPercent\":0,\"windowDurationMins\":10080", "\"usedPercent\":15,\"windowDurationMins\":10080");
        QuotaSnapshot activeReserveSnapshot;
        Assert(SessionQuotaReader.TryParseAppServerRateLimitsResponse(activeReserveFixture, out activeReserveSnapshot), "应能解析已启用的备用额度");
        Assert(activeReserveSnapshot.IsReserveActive && activeReserveSnapshot.DisplayWeeklyWindow == activeReserveSnapshot.ReserveWindow,
            "常规额度不可用时应自动切换显示备用额度");
        Assert(activeReserveSnapshot.DisplayWeeklyWindow.RemainingPercent == 85 && activeReserveSnapshot.DisplayWeeklyWindow.Name.Contains("备用额度"),
            "备用额度应显示正确余额和明确名称");

        const string planClaims = "{\"https://api.openai.com/auth\":{\"chatgpt_plan_type\":\"plus\"}}";
        var encodedClaims = Convert.ToBase64String(Encoding.UTF8.GetBytes(planClaims)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert(SessionQuotaReader.TryReadPlanTypeFromIdToken("e30." + encodedClaims + ".signature") == "plus",
            "应能从 ID Token 的嵌套声明中读取 ChatGPT 套餐");

        TokenActivitySample usage;
        Assert(SessionQuotaReader.TryParseTokenUsageLine(fixture, out usage), "应能解析本地 token 累计值");
        Assert(usage.Tokens == 123456, "应保留本地 token 累计值");
        Assert(usage.CapturedAt.UtcDateTime == new DateTime(2026, 8, 10, 10, 27, 30, 353, DateTimeKind.Utc), "应保留 token 活动时间");

        const string releaseJson = "{\"tag_name\":\"v1.8.0\",\"html_url\":\"https://github.com/SYD-Official/CodexQuotaTray/releases/tag/v1.8.0\",\"assets\":[{\"name\":\"CodexQuotaTray-v1.8.0.exe\",\"browser_download_url\":\"https://github.com/SYD-Official/CodexQuotaTray/releases/download/v1.8.0/CodexQuotaTray-v1.8.0.exe\",\"size\":123,\"digest\":\"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}]}";
        var release = UpdateService.ParseLatestRelease(releaseJson);
        Assert(release.Release != null && release.Release.Version == new Version(1, 8, 0), "应能解析 GitHub 最新版本");
        Assert(release.Release.AssetName == "CodexQuotaTray-v1.8.0.exe", "应选择版本号完全匹配的程序文件");
        Assert(release.Release.Digest.StartsWith("sha256:"), "应保留 GitHub 提供的 SHA-256 摘要");
        Assert(UpdateService.ParseLatestRelease(releaseJson.Replace("{\"tag_name\"", "{\"draft\":true,\"tag_name\"")).Release == null,
            "应拒绝 Draft 发布");
        Assert(UpdateService.ParseLatestRelease(releaseJson.Replace("{\"tag_name\"", "{\"prerelease\":true,\"tag_name\"")).Release == null,
            "应拒绝 Prerelease 发布");
        Assert(UpdateService.ParseLatestRelease(releaseJson.Replace("v1.8.0", "v1.8")).Release == null,
            "应拒绝不符合三段式规则的版本标签");
        Assert(UpdateService.ParseLatestRelease(releaseJson.Replace("\"size\":123", "\"size\":0")).Release == null,
            "应拒绝大小无效的发布文件");
        Assert(UpdateService.IsAllowedDownloadUri(new Uri("https://release-assets.githubusercontent.com/file")),
            "应允许 GitHub 官方资产域名");
        Assert(!UpdateService.IsAllowedDownloadUri(new Uri("https://example.com/fake.exe")),
            "应拒绝非 GitHub 下载重定向");
        var alertPalette = ThemePalette.Presets()[0];
        Assert(alertPalette.QuotaColor(21) == alertPalette.Primary, "额度高于 20% 时应保持主题色");
        Assert(alertPalette.QuotaColor(20) == alertPalette.Warning, "额度为 20% 时应切换为橙色");
        Assert(alertPalette.QuotaColor(10) == alertPalette.Danger, "额度为 10% 时应切换为红色");
        using (var normalIcon = RingIconFactory.Create(52, true, alertPalette, false))
        using (var alertIcon = RingIconFactory.Create(52, true, alertPalette, true))
        using (var normalBitmap = normalIcon.ToBitmap())
        using (var alertBitmap = alertIcon.ToBitmap())
        {
            var changedPixels = 0;
            for (var y = 0; y < normalBitmap.Height; y++)
            {
                for (var x = 0; x < normalBitmap.Width; x++)
                {
                    if (normalBitmap.GetPixel(x, y).ToArgb() != alertBitmap.GetPixel(x, y).ToArgb())
                        changedPixels++;
                }
            }
            Assert(changedPixels > 0, "任务完成状态应为托盘图标叠加独立蓝色提示效果");
        }
        Assert(UpdateService.IsNetworkFailure(WebExceptionStatus.NameResolutionFailure), "域名解析失败应归类为网络原因");
        Assert(!UpdateService.IsNetworkFailure(WebExceptionStatus.ProtocolError), "GitHub HTTP 错误不应误报为本地网络原因");
        var digestFixture = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(digestFixture, new byte[] { 97, 98, 99 });
            Assert(UpdateService.VerifyDigest(digestFixture, "sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"),
                "应能通过正确的 SHA-256 文件校验");
            Assert(!UpdateService.VerifyDigest(digestFixture, "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
                "应拒绝不匹配的 SHA-256 文件校验");
        }
        finally { File.Delete(digestFixture); }

        var firstReset = DateTimeOffset.Parse("2026-08-16T12:00:00Z");
        var nextReset = firstReset.AddDays(7);
        var before = new QuotaUsageSample { RemainingPercent = 50, WindowMinutes = 10080, ResetsAt = firstReset };
        var beforeResetEnd = new QuotaUsageSample { RemainingPercent = 20, WindowMinutes = 10080, ResetsAt = firstReset };
        var afterReset = new QuotaUsageSample { RemainingPercent = 50, WindowMinutes = 10080, ResetsAt = nextReset };
        var dayStart = new QuotaUsageSample { RemainingPercent = 70, WindowMinutes = 10080, ResetsAt = firstReset };
        var dayEnd = new QuotaUsageSample { RemainingPercent = 20, WindowMinutes = 10080, ResetsAt = firstReset };
        Assert(QuotaUsageMath.CalculateUsedPercent(dayStart, dayEnd, 10080) == 50, "同一周期内从 70% 用到 20% 应计为 50% 消耗");
        Assert(QuotaUsageMath.CalculateUsedPercent(before, beforeResetEnd, 10080) == 30, "重置前应计算 30% 消耗");
        Assert(QuotaUsageMath.CalculateUsedPercent(beforeResetEnd, afterReset, 10080) == 50, "跨重置且首个快照为 50% 时应补算 50% 消耗");
        Assert(QuotaUsageMath.CalculateUsedPercent(before, beforeResetEnd, 10080) +
            QuotaUsageMath.CalculateUsedPercent(beforeResetEnd, afterReset, 10080) == 80,
            "跨重置区间应把两段消耗相加为 80%");

        var weeklyWindow = new QuotaWindow { Key = "codex:10080", WindowMinutes = 10080 };
        var codexWeeklySample = new QuotaUsageSample { WindowKey = "codex:10080", WindowMinutes = 10080 };
        var reserveWeeklySample = new QuotaUsageSample { WindowKey = "gpt-reserve:10080", WindowMinutes = 10080 };
        Assert(QuotaUsageMath.IsSameWindow(codexWeeklySample, weeklyWindow), "周额度历史应匹配同一 codex 额度池");
        Assert(!QuotaUsageMath.IsSameWindow(reserveWeeklySample, weeklyWindow), "周额度历史不应混入 gpt-reserve 备用额度池");

        var storedSample = new QuotaUsageSample
        {
            CapturedAt = DateTimeOffset.Parse("2026-09-16T02:10:00Z"),
            WindowKey = "codex:10080",
            WindowKind = QuotaWindowKind.Weekly,
            WindowMinutes = 10080,
            RemainingPercent = 48,
            ResetsAt = DateTimeOffset.Parse("2026-09-19T06:49:47Z")
        };
        QuotaUsageSample restoredSample;
        Assert(SessionQuotaReader.TryParseQuotaHistoryLine(SessionQuotaReader.SerializeQuotaHistoryLine(storedSample), out restoredSample),
            "实时额度历史应能持久化并重新读取");
        Assert(restoredSample.WindowKey == storedSample.WindowKey && restoredSample.RemainingPercent == 48 && restoredSample.ResetsAt == storedSample.ResetsAt,
            "重新读取的实时额度历史应保持额度池、余额和重置时间");
        Assert(!SessionQuotaReader.ShouldRecordQuotaSample(storedSample, new QuotaUsageSample
        {
            CapturedAt = storedSample.CapturedAt.AddMinutes(4), WindowKey = storedSample.WindowKey,
            WindowMinutes = storedSample.WindowMinutes, RemainingPercent = storedSample.RemainingPercent, ResetsAt = storedSample.ResetsAt
        }), "余额未变化时不应高频重复写入历史");
        Assert(SessionQuotaReader.ShouldRecordQuotaSample(storedSample, new QuotaUsageSample
        {
            CapturedAt = storedSample.CapturedAt.AddMinutes(1), WindowKey = storedSample.WindowKey,
            WindowMinutes = storedSample.WindowMinutes, RemainingPercent = 47, ResetsAt = storedSample.ResetsAt
        }), "余额变化时应立即写入历史");

        QuotaSnapshot ignored;
        Assert(!SessionQuotaReader.TryParseTokenCountLine("{\"type\":\"event_msg\",\"payload\":{\"type\":\"other\"}}", out ignored),
            "非 token_count 事件应被忽略");

        var completion = new CodexCompletionMonitor(null);
        var completeA = "2026-08-25T05:07:38Z info [electron-message-handler] [desktop-notifications] show turn-complete conversationId=thread-a turnId=turn-a";
        var completeB = "2026-08-25T05:08:38Z info [electron-message-handler] [desktop-notifications] show turn-complete conversationId=thread-b turnId=turn-b";
        var resumedA = "2026-08-25T05:09:38Z info [electron-message-handler] thread_stream_view_activity_changed active=true conversationId=thread-a resumeState=resumed streamRole=owner";
        var resumedB = "2026-08-25T05:10:38Z info [electron-message-handler] thread_stream_view_activity_changed active=false conversationId=thread-b resumeState=resumed streamRole=owner";
        Assert(completion.ProcessLine(completeA) && completion.PendingCount == 1, "任务完成应加入未读集合");
        Assert(!completion.ProcessLine(completeA) && completion.PendingCount == 1, "重复 turnId 不应重复提醒");
        Assert(completion.ProcessLine(completeB) && completion.PendingCount == 2, "多个完成任务应分别跟踪");
        Assert(completion.ProcessLine(resumedA) && completion.PendingCount == 1, "查看一个任务后仍应保留其他未读任务");
        Assert(completion.ProcessLine(resumedB) && completion.PendingCount == 0, "所有任务已读后应清空提醒");
        Assert(CodexCompletionMonitor.PollInterval(false) == 20000 && CodexCompletionMonitor.PollInterval(true) == 3000,
            "提醒检查周期应按 20 秒、3 秒切换");

        var monitorDirectory = Path.Combine(Path.GetTempPath(), "CodexMeterMonitor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(monitorDirectory);
        try
        {
            var firstLog = Path.Combine(monitorDirectory, "desktop-t0.log");
            File.WriteAllText(firstLog, completeA + Environment.NewLine, Encoding.UTF8);
            var fileMonitor = new CodexCompletionMonitor(monitorDirectory);
            fileMonitor.Enable();
            Assert(!fileMonitor.Poll().HasUnread, "启用提醒时应从日志末尾开始并忽略旧事件");
            File.AppendAllText(firstLog, completeB + Environment.NewLine, Encoding.UTF8);
            var unread = fileMonitor.Poll();
            Assert(unread.HasUnread && unread.UnreadCount == 1, "增量写入的任务完成事件应触发提醒");
            File.AppendAllText(firstLog, resumedB + Environment.NewLine, Encoding.UTF8);
            Assert(!fileMonitor.Poll().HasUnread, "增量写入已读事件后应停止提醒");

            var rotatedLog = Path.Combine(monitorDirectory, "desktop-t1.log");
            File.WriteAllText(rotatedLog, completeA + Environment.NewLine, Encoding.UTF8);
            Assert(fileMonitor.Poll().HasUnread, "日志轮换后新文件中的完成事件仍应被读取");
            fileMonitor.AcknowledgeAll();
            Assert(fileMonitor.PendingCount == 0, "降级点击确认应清空未读状态");
            fileMonitor.Disable();
        }
        finally
        {
            Directory.Delete(monitorDirectory, true);
        }

        var missingMonitor = new CodexCompletionMonitor(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        missingMonitor.Enable();
        Assert(missingMonitor.Poll().ReadFailed, "日志目录不可用时应进入可点击清除的降级状态");

        var live = new SessionQuotaReader().ReadLatest();
        Console.WriteLine("夹具、额度告警与更新安全测试通过。Codex运行={0}，发现实时快照={1}，活动样本数={2}", live.IsCodexRunning, live.Snapshot != null, live.Activity.Count);
        if (live.Snapshot != null)
        {
            var liveFiveHour = live.Snapshot.FiveHourWindow;
            var liveWeekly = live.Snapshot.WeeklyWindow;
            Console.WriteLine("实时5小时额度剩余={0}% 每周额度剩余={1}% 更新时间={2:o} 套餐={3}",
                liveFiveHour == null ? "--" : liveFiveHour.RemainingPercent.ToString(),
                liveWeekly == null ? "--" : liveWeekly.RemainingPercent.ToString(),
                live.Snapshot.CapturedAt, live.Snapshot.PlanType ?? "未提供");
            Assert(liveFiveHour == null || live.QuotaHistory.Any(sample =>
                QuotaUsageMath.IsSameWindow(sample, liveFiveHour) && sample.RemainingPercent == liveFiveHour.RemainingPercent &&
                live.Snapshot.CapturedAt - sample.CapturedAt < TimeSpan.FromMinutes(5)),
                "实时 5 小时额度应进入图表历史");
            Assert(liveWeekly == null || live.QuotaHistory.Any(sample =>
                QuotaUsageMath.IsSameWindow(sample, liveWeekly) && sample.RemainingPercent == liveWeekly.RemainingPercent &&
                live.Snapshot.CapturedAt - sample.CapturedAt < TimeSpan.FromMinutes(5)),
                "实时周额度应进入图表历史");
        }

        if (args.Length == 1 && args[0] == "--live-update")
        {
            var update = UpdateService.CheckLatest();
            Console.WriteLine("GitHub检查：失败类型={0}，错误={1}", update.FailureKind, update.Error ?? "无");
            if (update.Release == null) return 2;
            Console.WriteLine("GitHub最新版本={0}，资产={1}，摘要={2}",
                update.Release.Version, update.Release.AssetName, update.Release.Digest);
        }

        if (args.Length == 1 && args[0] == "--benchmark-read")
        {
            var benchmarkReader = new SessionQuotaReader();
            var process = Process.GetCurrentProcess();
            var cpuBefore = process.TotalProcessorTime;
            var stopwatch = Stopwatch.StartNew();
            for (var index = 0; index < 20; index++) benchmarkReader.ReadLatest();
            stopwatch.Stop();
            process.Refresh();
            var cpuUsed = process.TotalProcessorTime - cpuBefore;
            Console.WriteLine("连续读取20次：墙钟={0}ms，CPU={1}ms，平均CPU={2:0.00}ms/次",
                stopwatch.ElapsedMilliseconds, cpuUsed.TotalMilliseconds, cpuUsed.TotalMilliseconds / 20d);
        }

        return 0;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            Console.Error.WriteLine("测试失败：" + message);
            Environment.Exit(1);
        }
    }
}
