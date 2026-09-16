using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CodexQuotaTray
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private const int RefreshIntervalMilliseconds = 20000;
        private const int CompletionFlashIntervalMilliseconds = 600;
        private readonly SessionQuotaReader _reader;
        private readonly CodexCompletionMonitor _completionMonitor;
        private readonly NotifyIcon _notifyIcon;
        private readonly QuotaPopupForm _popup;
        private readonly TaskbarWidgetForm _widget;
        private readonly Timer _refreshTimer;
        private readonly Timer _widgetGuardTimer;
        private readonly Timer _notificationTimer;
        private readonly Timer _completionFlashTimer;
        private readonly ToolStripMenuItem _showMenuItem;
        private readonly ToolStripMenuItem _refreshMenuItem;
        private readonly ToolStripMenuItem _exitMenuItem;
        private readonly AppSettings _settings;
        private Icon _currentIcon;
        private Rectangle _lastTrayAnchor;
        private QuotaReadResult _lastResult;
        private bool _lastSystemLightAppearance;
        private string _lastSystemLanguage;
        private bool _updateBusy;
        private bool _completionMonitorEnabled;
        private bool _hasCompletionUnread;
        private bool _completionFlashPhase;
        private int _completionUnreadCount;
        private int _refreshBusy;

        public TrayApplicationContext()
        {
            _settings = AppSettings.Load();
            _lastSystemLightAppearance = ThemePalette.SystemUsesLightAppearance();
            _lastSystemLanguage = AppSettings.SystemLanguageName();
            _reader = new SessionQuotaReader();
            _completionMonitor = new CodexCompletionMonitor();

            _popup = new QuotaPopupForm();
            _popup.ApplySettings(_settings);
            _popup.RefreshRequested += delegate { RefreshInBackground(); };
            _popup.ExitRequested += delegate { ExitThread(); };
            _popup.SettingsChanged += delegate { ApplySettingsChange(); };
            _popup.UpdateCheckRequested += delegate { CheckForUpdates(); };

            _widget = new TaskbarWidgetForm();
            _widget.ApplySettings(_settings);
            _lastTrayAnchor = new Rectangle(Cursor.Position.X - 1, Cursor.Position.Y - 1, 3, 3);
            _widget.Click += delegate { AcknowledgeCompletionReminder(); TogglePopup(_widget.GetAnchorBounds()); };

            var menu = new ContextMenuStrip();
            _showMenuItem = new ToolStripMenuItem();
            _showMenuItem.Click += delegate { TogglePopup(_lastTrayAnchor); };
            _refreshMenuItem = new ToolStripMenuItem();
            _refreshMenuItem.Click += delegate { RefreshInBackground(); };
            _exitMenuItem = new ToolStripMenuItem();
            _exitMenuItem.Click += delegate { ExitThread(); };
            menu.Items.Add(_showMenuItem);
            menu.Items.Add(_refreshMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_exitMenuItem);
            _widget.ContextMenuStrip = menu;

            var palette = ThemePalette.FromSettings(_settings);
            _currentIcon = RingIconFactory.Create(0, false, palette);
            _notifyIcon = new NotifyIcon
            {
                Icon = _currentIcon,
                Visible = true,
                ContextMenuStrip = menu
            };
            _notifyIcon.MouseClick += NotifyIconOnMouseClick;

            _refreshTimer = new Timer { Interval = RefreshIntervalMilliseconds };
            _refreshTimer.Tick += delegate { RefreshInBackground(); };
            _refreshTimer.Start();

            _notificationTimer = new Timer { Interval = CodexCompletionMonitor.IdleIntervalMilliseconds };
            _notificationTimer.Tick += delegate { PollCompletionNotifications(); };

            _completionFlashTimer = new Timer { Interval = CompletionFlashIntervalMilliseconds };
            _completionFlashTimer.Tick += delegate
            {
                _completionFlashPhase = !_completionFlashPhase;
                ApplyVisualResult(_lastResult);
            };

            _widgetGuardTimer = new Timer { Interval = 1000 };
            _widgetGuardTimer.Tick += delegate
            {
                if (_settings.WidgetVisible) _widget.EnsureOnTop();
                var systemLight = ThemePalette.SystemUsesLightAppearance();
                if (string.Equals(_settings.AppearanceMode, "system", StringComparison.OrdinalIgnoreCase) &&
                    systemLight != _lastSystemLightAppearance)
                {
                    _lastSystemLightAppearance = systemLight;
                    ApplySettingsVisuals();
                }
                var systemLanguage = AppSettings.SystemLanguageName();
                if (string.Equals(_settings.Language, "system", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(systemLanguage, _lastSystemLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    _lastSystemLanguage = systemLanguage;
                    ApplySettingsVisuals();
                }
            };
            _widgetGuardTimer.Start();

            UpdateLanguage();
            ApplyCompletionReminderSetting();
            RefreshNow();
            _widget.SetVisible(_settings.WidgetVisible);
        }

        private void ApplySettingsChange()
        {
            try { _settings.Save(); }
            catch { }

            _lastSystemLightAppearance = ThemePalette.SystemUsesLightAppearance();
            _lastSystemLanguage = AppSettings.SystemLanguageName();
            ApplySettingsVisuals();
            ApplyCompletionReminderSetting();
        }

        private void ApplySettingsVisuals()
        {
            _popup.ApplySettings(_settings);
            _widget.ApplySettings(_settings);
            _widget.SetVisible(_settings.WidgetVisible);
            UpdateLanguage();
            ApplyVisualResult(_lastResult);
        }

        private void UpdateLanguage()
        {
            _showMenuItem.Text = T("显示详情", "Show details");
            _refreshMenuItem.Text = T("立即刷新", "Refresh now");
            _exitMenuItem.Text = T("退出", "Quit");
        }

        private void NotifyIconOnMouseClick(object sender, MouseEventArgs e)
        {
            _lastTrayAnchor = TrayIconAnchorResolver.Resolve(Cursor.Position);
            if (e.Button == MouseButtons.Left)
            {
                AcknowledgeCompletionReminder();
                TogglePopup(_lastTrayAnchor);
            }
        }

        private void TogglePopup(Rectangle anchorBounds)
        {
            if (_popup.Visible) _popup.Hide();
            else
            {
                _popup.ShowCenteredAbove(anchorBounds);
                RefreshInBackground();
            }
        }

        private void RefreshNow()
        {
            ApplyReadResult(_reader.ReadLatest());
        }

        private void RefreshInBackground()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _refreshBusy, 1, 0) != 0) return;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                var result = _reader.ReadLatest();
                try
                {
                    if (_widget.IsDisposed || !_widget.IsHandleCreated)
                    {
                        System.Threading.Interlocked.Exchange(ref _refreshBusy, 0);
                        return;
                    }
                    _widget.BeginInvoke(new Action(delegate
                    {
                        System.Threading.Interlocked.Exchange(ref _refreshBusy, 0);
                        ApplyReadResult(result);
                    }));
                }
                catch
                {
                    System.Threading.Interlocked.Exchange(ref _refreshBusy, 0);
                }
            });
        }

        private void ApplyReadResult(QuotaReadResult result)
        {
            _lastResult = result;
            _popup.ApplyResult(_lastResult);
            _widget.ApplyResult(_lastResult);
            _widget.RefreshDynamicAnchor();
            ApplyVisualResult(_lastResult);
        }

        private void ApplyVisualResult(QuotaReadResult result)
        {
            if (result == null) return;
            var palette = ThemePalette.FromSettings(_settings);
            var active = result.IsCodexRunning && result.Snapshot != null;
            var window = result.Snapshot == null
                ? null
                : (result.Snapshot.IsReserveActive
                    ? result.Snapshot.ReserveWindow
                    : result.Snapshot.Windows.OrderBy(item => item.RemainingPercent).FirstOrDefault());
            var remaining = window == null ? 0 : window.RemainingPercent;
            ReplaceIcon(RingIconFactory.Create(remaining, active, palette, _hasCompletionUnread && _completionFlashPhase));

            if (!result.IsCodexRunning)
                SetTooltip(T("Codex 未运行 · 不会自动启动 Codex", "Codex is not running · It will not be started"));
            else if (result.Snapshot == null)
                SetTooltip(T("Codex 已运行 · 等待额度快照", "Codex is running · Waiting for quota data"));
            else
            {
                var fiveHour = result.Snapshot.FiveHourWindow;
                var weekly = result.Snapshot.DisplayWeeklyWindow;
                var tooltip = (fiveHour == null ? "5H --" : "5H " + fiveHour.RemainingPercent + "%") +
                    (weekly == null || weekly == fiveHour ? "" :
                        (result.Snapshot.IsReserveActive ? " · Reserve " : " · 7D ") + weekly.RemainingPercent + "%");
                if (_hasCompletionUnread) tooltip += T(" · 新任务 ", " · New ") + _completionUnreadCount;
                SetTooltip(tooltip);
            }
        }

        private void ApplyCompletionReminderSetting()
        {
            if (_settings.CompletionReminderEnabled == _completionMonitorEnabled) return;
            _completionMonitorEnabled = _settings.CompletionReminderEnabled;
            if (_completionMonitorEnabled)
            {
                _completionMonitor.Enable();
                _notificationTimer.Interval = CodexCompletionMonitor.IdleIntervalMilliseconds;
                _notificationTimer.Start();
            }
            else
            {
                _notificationTimer.Stop();
                _completionMonitor.Disable();
                StopCompletionReminder();
            }
        }

        private void PollCompletionNotifications()
        {
            if (!_completionMonitorEnabled) return;
            var result = _completionMonitor.Poll();
            _completionUnreadCount = result.UnreadCount;
            if (result.HasUnread)
            {
                _hasCompletionUnread = true;
                _notificationTimer.Interval = CodexCompletionMonitor.PollInterval(true);
                if (!_completionFlashTimer.Enabled)
                {
                    _completionFlashPhase = true;
                    _completionFlashTimer.Start();
                }
            }
            else
            {
                StopCompletionReminder();
                _notificationTimer.Interval = CodexCompletionMonitor.PollInterval(false);
            }
            if (result.StateChanged) ApplyVisualResult(_lastResult);
        }

        private void AcknowledgeCompletionReminder()
        {
            if (!_hasCompletionUnread) return;
            _completionMonitor.AcknowledgeAll();
            StopCompletionReminder();
            _notificationTimer.Interval = CodexCompletionMonitor.PollInterval(false);
            ApplyVisualResult(_lastResult);
        }

        private void StopCompletionReminder()
        {
            _hasCompletionUnread = false;
            _completionUnreadCount = 0;
            _completionFlashPhase = false;
            _completionFlashTimer.Stop();
        }

        private void CheckForUpdates()
        {
            if (_updateBusy) return;
            _updateBusy = true;
            _popup.SetUpdateStatus("正在连接 GitHub…", "Connecting to GitHub…", true);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                var result = UpdateService.CheckLatest();
                InvokePopup(delegate { HandleUpdateCheck(result); });
            });
        }

        private void HandleUpdateCheck(UpdateCheckResult result)
        {
            if (result == null || result.Release == null)
            {
                _updateBusy = false;
                var network = result != null && result.FailureKind == UpdateFailureKind.Network;
                _popup.SetUpdateStatus(
                    network ? "网络原因：无法连接 GitHub" : "检查更新失败",
                    network ? "Network error: GitHub is unreachable" : "Update check failed",
                    false);
                MessageBox.Show(
                    network
                        ? T("由于网络连接、DNS 或代理问题，当前无法访问 GitHub。请检查网络后重试。", "GitHub could not be reached because of a network, DNS, or proxy problem. Check the connection and try again.")
                        : T("无法读取 GitHub 发布信息。", "Could not read the GitHub release information.") + Environment.NewLine + (result == null ? "" : result.Error),
                    T("检查更新", "Check for updates"), MessageBoxButtons.OK,
                    network ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                return;
            }

            var current = UpdateService.CurrentVersion();
            if (result.Release.Version <= current)
            {
                _updateBusy = false;
                _popup.SetUpdateStatus("当前已是最新版本 v" + current.ToString(3), "You are up to date · v" + current.ToString(3), false);
                return;
            }

            _updateBusy = false;
            _popup.SetUpdateStatus("发现新版本 v" + result.Release.Version.ToString(3), "New version v" + result.Release.Version.ToString(3) + " is available", false);
            var answer = MessageBox.Show(
                T("发现新版本 v", "Version v") + result.Release.Version.ToString(3) +
                T("，当前版本为 v", " is available. Current version: v") + current.ToString(3) +
                T("。\r\n\r\n是否现在下载，并在下载完成后自动重启？", ".\r\n\r\nDownload it now and restart automatically when complete?"),
                T("发现更新", "Update available"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            _updateBusy = true;
            _popup.SetUpdateStatus("正在下载并校验更新…", "Downloading and verifying the update…", true);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                var download = UpdateService.Download(result.Release);
                InvokePopup(delegate { HandleUpdateDownload(download); });
            });
        }

        private void HandleUpdateDownload(UpdateDownloadResult result)
        {
            _updateBusy = false;
            if (result == null || string.IsNullOrWhiteSpace(result.FilePath))
            {
                var network = result != null && result.FailureKind == UpdateFailureKind.Network;
                _popup.SetUpdateStatus(
                    network ? "网络原因：更新下载失败" : "下载或文件校验失败",
                    network ? "Network error: download failed" : "Download or verification failed",
                    false);
                MessageBox.Show(
                    network
                        ? T("由于网络连接、DNS 或代理问题，无法从 GitHub 下载安装包。", "The update could not be downloaded from GitHub because of a network, DNS, or proxy problem.")
                        : T("更新未安装：", "The update was not installed: ") + (result == null ? "" : result.Error),
                    T("更新失败", "Update failed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _popup.SetUpdateStatus("更新已下载，正在重启…", "Update downloaded; restarting…", false);
            try
            {
                var startInfo = new ProcessStartInfo(result.FilePath, "--wait-for-pid " + Process.GetCurrentProcess().Id)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(result.FilePath)
                };
                Process.Start(startInfo);
                ExitThread();
            }
            catch (Exception exception)
            {
                _popup.SetUpdateStatus("已下载，请手动运行新版本", "Downloaded; start the new version manually", false);
                MessageBox.Show(
                    T("更新已下载，但无法自动启动。请退出本程序后手动运行：", "The update was downloaded but could not be started automatically. Quit this app, then run:") +
                    Environment.NewLine + result.FilePath + Environment.NewLine + Environment.NewLine + exception.Message,
                    T("请手动重启", "Manual restart required"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void InvokePopup(Action action)
        {
            try
            {
                if (!_popup.IsDisposed && _popup.IsHandleCreated) _popup.BeginInvoke(action);
            }
            catch
            {
            }
        }

        private string T(string chinese, string english) { return _settings.IsEnglish ? english : chinese; }

        private void ReplaceIcon(Icon icon)
        {
            var previous = _currentIcon;
            _currentIcon = icon;
            _notifyIcon.Icon = icon;
            if (previous != null) previous.Dispose();
        }

        private void SetTooltip(string value)
        {
            _notifyIcon.Text = value.Length <= 63 ? value : value.Substring(0, 63);
        }

        protected override void ExitThreadCore()
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _widgetGuardTimer.Stop();
            _widgetGuardTimer.Dispose();
            _notificationTimer.Stop();
            _notificationTimer.Dispose();
            _completionFlashTimer.Stop();
            _completionFlashTimer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _popup.Dispose();
            _widget.Dispose();
            if (_currentIcon != null) _currentIcon.Dispose();
            base.ExitThreadCore();
        }
    }
}
