using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CodexQuotaTray
{
    // AIHOT · Codex 重置监控窗口(简单列表形态,改动最小、稳定优先)
    internal sealed class AihotResetsForm : Form
    {
        private readonly AihotService _service;
        private AppSettings _settings;
        private readonly ListView _listView;
        private readonly Button _refreshButton;
        private readonly Button _openButton;
        private readonly Button _termsButton;
        private readonly Label _statusLabel;
        private readonly Timer _pollTimer;
        private Color _pendingColor = Color.FromArgb(244, 164, 73);
        private int _lastVersion = -1;

        public AihotResetsForm(AihotService service, AppSettings settings)
        {
            _service = service;
            _settings = settings;

            Text = T("AIHOT · Codex 重置监控", "AIHOT · Codex resets");
            ShowIcon = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(600, 420);
            MinimumSize = new Size(480, 280);
            Font = SystemFonts.MessageBoxFont;

            _listView = new ListView();
            _listView.Dock = DockStyle.Fill;
            _listView.View = View.Details;
            _listView.FullRowSelect = true;
            _listView.MultiSelect = false;
            _listView.HideSelection = false;
            _listView.Columns.Add(T("类型", "Type"), 170);
            _listView.Columns.Add(T("时间", "Time"), 110);
            _listView.Columns.Add(T("说明", "Details"), 280);
            _listView.SelectedIndexChanged += delegate { UpdateButtons(); };
            _listView.DoubleClick += delegate { OpenSelected(); };

            var bottomPanel = new Panel();
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.Height = 76;

            var buttonFlow = new FlowLayoutPanel();
            buttonFlow.Dock = DockStyle.Top;
            buttonFlow.Height = 40;
            buttonFlow.FlowDirection = FlowDirection.LeftToRight;
            buttonFlow.WrapContents = false;
            buttonFlow.Padding = new Padding(10, 8, 10, 0);

            _refreshButton = new Button();
            _refreshButton.Text = T("立即刷新", "Refresh now");
            _refreshButton.AutoSize = true;
            _refreshButton.Click += delegate
            {
                _service.RequestManualRefresh();
                Render(_service.GetSnapshot());
            };

            _openButton = new Button();
            _openButton.Text = T("打开原帖", "Open source post");
            _openButton.AutoSize = true;
            _openButton.Enabled = false;
            _openButton.Click += delegate { OpenSelected(); };

            _termsButton = new Button();
            _termsButton.Text = T("来源与规则", "Source & terms");
            _termsButton.AutoSize = true;
            _termsButton.Click += delegate { OpenUrl(AihotService.TermsUrl); };

            buttonFlow.Controls.Add(_refreshButton);
            buttonFlow.Controls.Add(_openButton);
            buttonFlow.Controls.Add(_termsButton);

            _statusLabel = new Label();
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Padding = new Padding(12, 0, 12, 6);
            _statusLabel.AutoEllipsis = true;

            bottomPanel.Controls.Add(_statusLabel);
            bottomPanel.Controls.Add(buttonFlow);

            Controls.Add(_listView);
            Controls.Add(bottomPanel);

            _pollTimer = new Timer { Interval = 1500 };
            _pollTimer.Tick += delegate { Poll(); };

            ApplySettings(settings);
        }

        public void ApplySettings(AppSettings settings)
        {
            _settings = settings;
            var palette = ThemePalette.FromSettings(settings);
            _pendingColor = palette.Warning;

            Text = T("AIHOT · Codex 重置监控", "AIHOT · Codex resets");
            _listView.Columns[0].Text = T("类型", "Type");
            _listView.Columns[1].Text = T("时间", "Time");
            _listView.Columns[2].Text = T("说明", "Details");
            _refreshButton.Text = T("立即刷新", "Refresh now");
            _openButton.Text = T("打开原帖", "Open source post");
            _termsButton.Text = T("来源与规则", "Source & terms");

            BackColor = palette.Background;
            ForeColor = palette.Text;
            _listView.BackColor = palette.Surface;
            _listView.ForeColor = palette.Text;
            _statusLabel.BackColor = palette.Background;
            _statusLabel.ForeColor = palette.Muted;

            Render(_service.GetSnapshot());
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _service.RequestManualRefresh();
            Render(_service.GetSnapshot());
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) _pollTimer.Start();
            else _pollTimer.Stop();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 用户点 X:隐藏窗口而不是销毁(与主程序单例模式一致;退出应用时由主程序释放)
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pollTimer.Stop();
                _pollTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private void Poll()
        {
            var snapshot = _service.GetSnapshot();
            if (snapshot.Version != _lastVersion)
            {
                Render(snapshot);
            }
            else
            {
                // 状态行中的"剩余等待"随时间变化,轻量刷新
                UpdateStatusText(snapshot);
            }
        }

        private void Render(AihotResetsSnapshot snapshot)
        {
            _lastVersion = snapshot.Version;
            _listView.BeginUpdate();
            try
            {
                _listView.Items.Clear();
                foreach (var item in snapshot.Events)
                {
                    var row = new ListViewItem(BuildTypeText(item));
                    row.SubItems.Add(item.TimeText ?? "");
                    row.SubItems.Add(BuildDetailText(item));
                    row.Tag = item;
                    if (item.IsPending) row.ForeColor = _pendingColor;
                    _listView.Items.Add(row);
                }
            }
            finally
            {
                _listView.EndUpdate();
            }
            UpdateStatusText(snapshot);
            UpdateButtons();
        }

        private string BuildTypeText(AihotResetEventItem item)
        {
            var label = item.DisplayLabel;
            if (string.IsNullOrWhiteSpace(label)) label = item.Title;
            if (string.IsNullOrWhiteSpace(label)) label = T("重置事件", "Reset event");
            return item.IsPending ? T("预告 · ", "Upcoming · ") + label : label;
        }

        private string BuildDetailText(AihotResetEventItem item)
        {
            var text = "";
            if (!string.IsNullOrWhiteSpace(item.ScheduleText)) text += "【" + T("预告", "Upcoming") + "】" + item.ScheduleText + "  ";
            if (!string.IsNullOrWhiteSpace(item.SummaryText)) text += item.SummaryText;
            return text.Trim();
        }

        private void UpdateStatusText(AihotResetsSnapshot snapshot)
        {
            string text;
            var fetched = snapshot.FetchedAtLocal.HasValue ? snapshot.FetchedAtLocal.Value.ToString("HH:mm") : null;
            var checkedAt = snapshot.LastCheckLocal.HasValue ? snapshot.LastCheckLocal.Value.ToString("HH:mm") : null;
            switch (snapshot.StatusKey)
            {
                case "ok":
                    text = T("已更新 " + (fetched ?? "") + " · 数据每 10 分钟自动刷新", "Updated " + (fetched ?? "") + " · auto refresh every 10 min");
                    break;
                case "not-modified":
                    text = T("已检查 " + (checkedAt ?? "") + " · 暂无变化", "Checked " + (checkedAt ?? "") + " · no changes");
                    break;
                case "cache":
                    text = T("显示本地缓存(" + (fetched ?? "?") + "),等待首次更新…", "Showing local cache (" + (fetched ?? "?") + "), waiting for first update…");
                    break;
                case "ratelimited":
                    text = T("请求被限流,将于约 " + RemainingText(snapshot) + " 后重试(已遵循 Retry-After)", "Rate limited, retrying in about " + RemainingText(snapshot) + " (Retry-After respected)");
                    break;
                case "network":
                    text = fetched != null
                        ? T("网络异常,继续显示 " + fetched + " 的数据", "Network error, still showing data from " + fetched)
                        : T("网络异常,暂时无法获取数据(不影响其它功能)", "Network error, no data yet (other features unaffected)");
                    break;
                case "http":
                    text = T("接口异常(" + (snapshot.StatusDetail ?? "HTTP") + "),稍后自动重试", "Service error (" + (snapshot.StatusDetail ?? "HTTP") + "), will retry later");
                    break;
                case "parse":
                    text = T("数据解析失败,保留上次数据,稍后自动重试", "Failed to parse data; keeping last data, will retry later");
                    break;
                default:
                    text = T("正在获取…", "Fetching…");
                    break;
            }
            text += T(" ｜ 数据来源:AIHOT · aihot.news(只读)", " ｜ Source: AIHOT · aihot.news (read-only)");
            if (snapshot.PendingCount > 0 && snapshot.StatusKey != "init")
            {
                text += T(" ｜ 待落地预告:" + snapshot.PendingCount + " 条", " ｜ Upcoming:" + snapshot.PendingCount);
            }
            if (_statusLabel.Text != text) _statusLabel.Text = text;
        }

        private string RemainingText(AihotResetsSnapshot snapshot)
        {
            if (!snapshot.NextAttemptUtc.HasValue) return T("几分钟", "a few minutes");
            var remaining = snapshot.NextAttemptUtc.Value - DateTimeOffset.UtcNow;
            if (remaining.TotalSeconds <= 0) return T("数秒", "moments");
            if (remaining.TotalMinutes < 1) return Math.Ceiling(remaining.TotalSeconds) + T(" 秒", "s");
            return Math.Ceiling(remaining.TotalMinutes) + T(" 分钟", " min");
        }

        private void UpdateButtons()
        {
            var item = SelectedEvent();
            _openButton.Enabled = item != null && !string.IsNullOrWhiteSpace(item.PrimaryUrl);
        }

        private AihotResetEventItem SelectedEvent()
        {
            if (_listView.SelectedItems.Count == 0) return null;
            return _listView.SelectedItems[0].Tag as AihotResetEventItem;
        }

        private void OpenSelected()
        {
            var item = SelectedEvent();
            if (item == null || string.IsNullOrWhiteSpace(item.PrimaryUrl)) return;
            OpenUrl(item.PrimaryUrl);
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                MessageBox.Show(this, T("无法打开网页:", "Could not open the page: ") + url,
                    T("打开链接", "Open link"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private string T(string chinese, string english)
        {
            return _settings.IsEnglish ? english : chinese;
        }
    }
}
