using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexQuotaTray
{
    internal sealed class QuotaPopupForm : Form
    {
        private sealed class ActivityBucket
        {
            public DateTimeOffset Start { get; set; }
            public long Tokens { get; set; }
            public double QuotaUsedPercent { get; set; }
            public bool HasQuotaData { get; set; }
        }

        private const int BaseWidth = 420;
        private const int BaseHeight = 590;

        [DllImport("gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        private readonly Rectangle _refreshBounds = new Rectangle(366, 20, 34, 34);
        private readonly Rectangle _settingsBounds = new Rectangle(20, 468, 380, 60);
        private readonly Rectangle _exitBounds = new Rectangle(350, 548, 50, 34);
        private readonly Rectangle _backBounds = new Rectangle(18, 18, 38, 38);
        private readonly Rectangle _widgetToggleBounds = new Rectangle(350, 101, 46, 24);
        private readonly Rectangle _appearanceSystemBounds = new Rectangle(222, 214, 56, 25);
        private readonly Rectangle _appearanceDarkBounds = new Rectangle(282, 214, 54, 25);
        private readonly Rectangle _appearanceLightBounds = new Rectangle(340, 214, 56, 25);
        private readonly Rectangle _languageSystemBounds = new Rectangle(222, 418, 56, 34);
        private readonly Rectangle _languageZhBounds = new Rectangle(282, 418, 54, 34);
        private readonly Rectangle _languageEnBounds = new Rectangle(340, 418, 56, 34);
        private readonly Rectangle _displaySettingsBounds = new Rectangle(16, 466, 388, 60);
        private readonly Rectangle _resetBounds = new Rectangle(326, 18, 70, 38);
        private readonly Rectangle _homeBounds = new Rectangle(18, 536, 96, 54);
        private readonly Rectangle _returnSettingsBounds = new Rectangle(18, 536, 120, 54);
        private readonly Rectangle _customPrimaryBounds = new Rectangle(314, 366, 28, 28);
        private readonly Rectangle _customSecondaryBounds = new Rectangle(354, 366, 28, 28);
        private readonly Rectangle _range5Bounds = new Rectangle(24, 320, 48, 27);
        private readonly Rectangle _range24Bounds = new Rectangle(76, 320, 56, 27);
        private readonly Rectangle _range7Bounds = new Rectangle(136, 320, 46, 27);
        private readonly Rectangle _range30Bounds = new Rectangle(186, 320, 50, 27);

        private readonly Rectangle _popupScaleSliderBounds = new Rectangle(176, 114, 210, 20);
        private readonly Rectangle _fontScaleSliderBounds = new Rectangle(176, 174, 210, 20);
        private readonly Rectangle _widgetScaleSliderBounds = new Rectangle(176, 234, 210, 20);
        private readonly Rectangle _glassToggleBounds = new Rectangle(350, 273, 46, 24);
        private readonly Rectangle _glassOpacitySliderBounds = new Rectangle(176, 341, 210, 20);
        private readonly Rectangle _quotaLayoutToggleBounds = new Rectangle(350, 157, 46, 24);
        private readonly Rectangle _completionReminderToggleBounds = new Rectangle(350, 385, 46, 24);
        private readonly Rectangle _bilibiliBounds = new Rectangle(200, 480, 90, 44);
        private readonly Rectangle _githubBounds = new Rectangle(300, 480, 96, 44);
        private readonly Rectangle _checkUpdateBounds = new Rectangle(24, 480, 166, 44);

        private readonly Rectangle[] _themeBounds =
        {
            new Rectangle(24, 248, 176, 44),
            new Rectangle(220, 248, 176, 44),
            new Rectangle(24, 298, 176, 44),
            new Rectangle(220, 298, 176, 44),
            new Rectangle(24, 354, 372, 52)
        };

        private const int HoverRefresh = 1;
        private const int HoverSettingsEntry = 2;
        private const int HoverExit = 5;
        private const int HoverRange24 = 6;
        private const int HoverRange7 = 7;
        private const int HoverRange30 = 8;
        private const int HoverRange5 = 50;
        private const int HoverBack = 9;
        private const int HoverWidgetToggle = 10;
        private const int HoverAppearanceSystem = 11;
        private const int HoverAppearanceDark = 12;
        private const int HoverAppearanceLight = 13;
        private const int HoverThemeFirst = 20;
        private const int HoverCustomPrimary = 25;
        private const int HoverCustomSecondary = 26;
        private const int HoverLanguageSystem = 30;
        private const int HoverLanguageZh = 31;
        private const int HoverLanguageEn = 32;
        private const int HoverDisplaySettings = 36;
        private const int HoverReset = 37;
        private const int HoverReturnSettings = 38;
        private const int HoverPopupSlider = 39;
        private const int HoverFontSlider = 40;
        private const int HoverWidgetSlider = 41;
        private const int HoverGlassToggle = 42;
        private const int HoverGlassOpacitySlider = 43;
        private const int HoverCheckUpdate = 44;
        private const int HoverBilibili = 45;
        private const int HoverGitHub = 46;
        private const int HoverHome = 47;
        private const int HoverQuotaLayoutToggle = 48;
        private const int HoverCompletionReminderToggle = 49;

        private readonly Timer _hoverTimer;
        private readonly Timer _sliderSnapTimer;

        private QuotaReadResult _result;
        private AppSettings _settings = new AppSettings();
        private ThemePalette _palette;
        private bool _showSettings;
        private bool _showDisplaySettings;
        private float _dpiScale = 1f;
        private Rectangle _lastAnchorBounds;
        private List<ActivityBucket> _currentBuckets = new List<ActivityBucket>();
        private double[] _hoverWeights = new double[0];
        private int _hoverIndex = -1;
        private int _hoverHint;
        private int _activeSlider = -1;
        private int _snapSliderIndex = -1;
        private double _snapPulse;
        private bool _quotaHistoryAvailable;
        private bool _nativeRoundedCorners;
        private bool _glassActive;
        private readonly List<DirectWriteTextRenderer.TextItem> _layeredTextItems = new List<DirectWriteTextRenderer.TextItem>();
        private string _updateStatusChinese;
        private string _updateStatusEnglish;
        private bool _updateBusy;

        public QuotaPopupForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            _palette = ThemePalette.FromSettings(_settings);
            Text = "Codex 用量";
            ClientSize = new Size(BaseWidth, BaseHeight);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = _palette.Background;
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            DoubleBuffered = true;
            TopMost = true;
            _hoverTimer = new Timer { Interval = 16 };
            _hoverTimer.Tick += HoverTimerOnTick;
            _sliderSnapTimer = new Timer { Interval = 16 };
            _sliderSnapTimer.Tick += SliderSnapTimerOnTick;
            Deactivate += delegate { ResetNavigationAndHide(); };
        }

        public event EventHandler RefreshRequested;
        public event EventHandler ExitRequested;
        public event EventHandler SettingsChanged;
        public event EventHandler UpdateCheckRequested;

        protected override bool ShowWithoutActivation { get { return false; } }

        public void ApplyResult(QuotaReadResult result)
        {
            _result = result;
            Invalidate();
        }

        public void ApplySettings(AppSettings settings)
        {
            _settings = settings ?? new AppSettings();
            _palette = ThemePalette.FromSettings(_settings);
            Text = _settings.IsEnglish ? "Codex Usage" : "Codex 用量";
            BackColor = _palette.Background;
            if (IsHandleCreated) ApplyWindowMaterial();
            ApplyScaledSize();
            Invalidate();
        }

        public void SetUpdateStatus(string chinese, string english, bool busy)
        {
            _updateStatusChinese = chinese;
            _updateStatusEnglish = english;
            _updateBusy = busy;
            Invalidate();
        }

        internal void ShowPreviewPage(int page)
        {
            _showSettings = page == 1;
            _showDisplaySettings = page == 2;
            Invalidate();
        }

        internal void SetPreviewHover(int index)
        {
            _showSettings = false;
            _showDisplaySettings = false;
            _currentBuckets = BuildActivityBuckets();
            EnsureHoverWeights(_currentBuckets.Count);
            _hoverIndex = Math.Max(-1, Math.Min(_currentBuckets.Count - 1, index));
            for (var item = 0; item < _hoverWeights.Length; item++)
            {
                var distance = _hoverIndex < 0 ? int.MaxValue : Math.Abs(item - _hoverIndex);
                _hoverWeights[item] = distance == 0 ? 1d : (distance == 1 ? 0.52d : (distance == 2 ? 0.18d : 0d));
            }
            Invalidate();
        }

        internal void SetPreviewHint(int hint)
        {
            _hoverHint = hint == 4 ? HoverGlassToggle : hint;
            Invalidate();
        }

        public void ShowCenteredAbove(Rectangle anchorBounds)
        {
            _lastAnchorBounds = anchorBounds;
            Show();
            NativeRect popupBounds;
            if (!GetWindowRect(Handle, out popupBounds)) { Activate(); return; }

            var anchorCenter = new NativePoint
            {
                X = anchorBounds.Left + anchorBounds.Width / 2,
                Y = anchorBounds.Top + anchorBounds.Height / 2
            };
            var monitor = MonitorFromPoint(anchorCenter, 2);
            var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetMonitorInfo(monitor, ref monitorInfo)) { Activate(); return; }

            var popupWidth = popupBounds.Right - popupBounds.Left;
            var popupHeight = popupBounds.Bottom - popupBounds.Top;
            var margin = (int)Math.Round(8 * _dpiScale);
            var x = anchorCenter.X - popupWidth / 2;
            var y = monitorInfo.Work.Bottom - popupHeight - margin;

            if (anchorCenter.Y < monitorInfo.Work.Top) y = monitorInfo.Work.Top + margin;
            else if (anchorCenter.X < monitorInfo.Work.Left)
            {
                x = monitorInfo.Work.Left + margin;
                y = anchorCenter.Y - popupHeight / 2;
            }
            else if (anchorCenter.X >= monitorInfo.Work.Right)
            {
                x = monitorInfo.Work.Right - popupWidth - margin;
                y = anchorCenter.Y - popupHeight / 2;
            }

            x = Math.Max(monitorInfo.Work.Left + margin, Math.Min(x, monitorInfo.Work.Right - popupWidth - margin));
            y = Math.Max(monitorInfo.Work.Top + margin, Math.Min(y, monitorInfo.Work.Bottom - popupHeight - margin));
            SetWindowPos(Handle, new IntPtr(-1), x, y, popupWidth, popupHeight, 0x0040);
            Invalidate();
            Activate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _dpiScale = GraphicsExtensions.GetDpiScale(Handle);
            ApplyWindowMaterial();
            ApplyScaledSize();
        }

        private void ResetNavigationAndHide()
        {
            SetHoverHint(0);
            SetHoverIndex(-1);
            _showSettings = false;
            _showDisplaySettings = false;
            Hide();
        }

        protected override void WndProc(ref Message message)
        {
            const int DpiChanged = 0x02E0;
            if (message.Msg == DpiChanged)
            {
                var newDpi = message.WParam.ToInt64() & 0xFFFF;
                var suggested = (NativeRect)Marshal.PtrToStructure(message.LParam, typeof(NativeRect));
                base.WndProc(ref message);
                _dpiScale = Math.Max(1f, newDpi / 96f);
                var size = GetScaledSize();
                SetWindowPos(Handle, IntPtr.Zero, suggested.Left, suggested.Top, size.Width, size.Height, 0x0010);
                Invalidate();
                if (Visible && !_lastAnchorBounds.IsEmpty)
                    BeginInvoke(new Action(delegate { ShowCenteredAbove(_lastAnchorBounds); }));
                return;
            }
            base.WndProc(ref message);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateWindowRegion();
        }

        private void ApplyWindowMaterial()
        {
            _nativeRoundedCorners = AcrylicEffect.ConfigureRoundedCorners(Handle);
            _glassActive = _settings.GlassEnabled && AcrylicEffect.Apply(Handle, true, _settings.GlassOpacityPercent, _palette.Background);
            if (!_settings.GlassEnabled) AcrylicEffect.Apply(Handle, false, _settings.GlassOpacityPercent, _palette.Background);
            UpdateWindowRegion();
        }

        private void UpdateWindowRegion()
        {
            if (_nativeRoundedCorners)
            {
                Region = null;
                return;
            }
            var radius = Math.Max(20, (int)Math.Round(20 * GetDrawingScale()));
            var regionHandle = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, radius, radius);
            try { Region = Region.FromHrgn(regionHandle); }
            finally { DeleteObject(regionHandle); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            var scale = GetDrawingScale();
            var point = new Point((int)Math.Round(e.X / scale), (int)Math.Round(e.Y / scale));
            if (_activeSlider >= 0)
            {
                UpdateActiveSlider(point.X, true);
                _activeSlider = -1;
                Capture = false;
                return;
            }
            if (_exitBounds.Contains(point)) { Raise(ExitRequested); return; }

            if (_showDisplaySettings)
            {
                HandleDisplaySettingsClick(point);
                return;
            }

            if (_showSettings)
            {
                HandleSettingsClick(point);
                return;
            }

            if (_refreshBounds.Contains(point)) Raise(RefreshRequested);
            else if (_settingsBounds.Contains(point)) { SetHoverIndex(-1); _showSettings = true; Invalidate(); }
            else if (_range5Bounds.Contains(point)) SetActivityRange(ActivityRange.Hours5);
            else if (_range24Bounds.Contains(point)) SetActivityRange(ActivityRange.Hours24);
            else if (_range7Bounds.Contains(point)) SetActivityRange(ActivityRange.Days7);
            else if (_range30Bounds.Contains(point)) SetActivityRange(ActivityRange.Days30);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !_showDisplaySettings) return;
            var scale = GetDrawingScale();
            var point = new Point((int)Math.Round(e.X / scale), (int)Math.Round(e.Y / scale));
            if (!TryBeginSlider(point)) return;
            Capture = true;
            UpdateActiveSlider(point.X, false);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var scale = GetDrawingScale();
            var point = new PointF(e.X / scale, e.Y / scale);
            var logicalPoint = Point.Round(point);

            if (_showDisplaySettings)
            {
                SetHoverIndex(-1);
                if (_activeSlider >= 0 && e.Button == MouseButtons.Left)
                {
                    UpdateActiveSlider(logicalPoint.X, false);
                    return;
                }
                SetHoverHint(HitTestDisplaySettings(logicalPoint));
                return;
            }

            if (_showSettings)
            {
                SetHoverIndex(-1);
                SetHoverHint(HitTestSettings(logicalPoint));
                return;
            }

            var mainTarget = HitTestMain(logicalPoint);
            if (mainTarget != 0)
            {
                SetHoverHint(mainTarget);
                SetHoverIndex(-1);
                return;
            }

            SetHoverHint(0);
            var chart = new RectangleF(32, 354, 356, 98);
            if (_currentBuckets.Count == 0 || !chart.Contains(point))
            {
                SetHoverIndex(-1);
                return;
            }

            var gap = ActivityBarGap(_currentBuckets.Count);
            var barWidth = Math.Max(4f, (chart.Width - gap * (_currentBuckets.Count - 1)) / _currentBuckets.Count);
            var stride = barWidth + gap;
            var index = (int)Math.Round((point.X - chart.Left - barWidth / 2f) / stride);
            SetHoverIndex(Math.Max(0, Math.Min(_currentBuckets.Count - 1, index)));
        }

        private int HitTestMain(Point point)
        {
            if (_refreshBounds.Contains(point)) return HoverRefresh;
            if (_settingsBounds.Contains(point)) return HoverSettingsEntry;
            if (_range5Bounds.Contains(point)) return HoverRange5;
            if (_range24Bounds.Contains(point)) return HoverRange24;
            if (_range7Bounds.Contains(point)) return HoverRange7;
            if (_range30Bounds.Contains(point)) return HoverRange30;
            if (_exitBounds.Contains(point)) return HoverExit;
            return 0;
        }

        private int HitTestSettings(Point point)
        {
            if (_backBounds.Contains(point)) return HoverBack;
            if (_exitBounds.Contains(point)) return HoverExit;
            if (_resetBounds.Contains(point)) return HoverReset;
            if (_homeBounds.Contains(point)) return HoverHome;
            if (_widgetToggleBounds.Contains(point)) return HoverWidgetToggle;
            if (_appearanceSystemBounds.Contains(point)) return HoverAppearanceSystem;
            if (_appearanceDarkBounds.Contains(point)) return HoverAppearanceDark;
            if (_appearanceLightBounds.Contains(point)) return HoverAppearanceLight;
            if (_customPrimaryBounds.Contains(point)) return HoverCustomPrimary;
            if (_customSecondaryBounds.Contains(point)) return HoverCustomSecondary;
            for (var index = 0; index < _themeBounds.Length; index++)
                if (_themeBounds[index].Contains(point)) return HoverThemeFirst + index;
            if (_languageSystemBounds.Contains(point)) return HoverLanguageSystem;
            if (_languageZhBounds.Contains(point)) return HoverLanguageZh;
            if (_languageEnBounds.Contains(point)) return HoverLanguageEn;
            if (_quotaLayoutToggleBounds.Contains(point)) return HoverQuotaLayoutToggle;
            if (_displaySettingsBounds.Contains(point)) return HoverDisplaySettings;
            return 0;
        }

        private int HitTestDisplaySettings(Point point)
        {
            if (_backBounds.Contains(point)) return HoverBack;
            if (_returnSettingsBounds.Contains(point)) return HoverReturnSettings;
            if (_exitBounds.Contains(point)) return HoverExit;
            if (_popupScaleSliderBounds.Contains(point)) return HoverPopupSlider;
            if (_fontScaleSliderBounds.Contains(point)) return HoverFontSlider;
            if (_widgetScaleSliderBounds.Contains(point)) return HoverWidgetSlider;
            if (_settings.GlassEnabled && _glassOpacitySliderBounds.Contains(point)) return HoverGlassOpacitySlider;
            if (_glassToggleBounds.Contains(point)) return HoverGlassToggle;
            if (_completionReminderToggleBounds.Contains(point)) return HoverCompletionReminderToggle;
            if (_bilibiliBounds.Contains(point)) return HoverBilibili;
            if (_githubBounds.Contains(point)) return HoverGitHub;
            if (_checkUpdateBounds.Contains(point) && !_updateBusy) return HoverCheckUpdate;
            return 0;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetHoverHint(0);
            SetHoverIndex(-1);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) return;
            _showSettings = false;
            _showDisplaySettings = false;
            _activeSlider = -1;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _hoverTimer.Stop();
            _hoverTimer.Dispose();
            _sliderSnapTimer.Stop();
            _sliderSnapTimer.Dispose();
            base.OnFormClosed(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (!_glassActive) base.OnPaintBackground(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            _layeredTextItems.Clear();
            if (_glassActive)
            {
                var opacity = Math.Max(5, Math.Min(95, _settings.GlassOpacityPercent));
                var alpha = (int)Math.Round((opacity - 5) * 160d / 90d);
                var shadeColor = _palette.IsLight ? Color.FromArgb(244, 249, 252) : Color.FromArgb(0, 12, 21);
                if (alpha > 0)
                    using (var shade = new SolidBrush(Color.FromArgb(alpha, shadeColor)))
                        graphics.FillRectangle(shade, ClientRectangle);
                using (var glow = new LinearGradientBrush(ClientRectangle,
                    Color.FromArgb(10, _palette.Primary), Color.FromArgb(0, _palette.Primary), 35f))
                    graphics.FillRectangle(glow, ClientRectangle);
            }
            graphics.ScaleTransform(GetDrawingScale(), GetDrawingScale());

            using (var border = new Pen(Color.FromArgb(95, _palette.Divider)))
                graphics.DrawRoundedRectangle(border, new Rectangle(0, 0, BaseWidth - 1, BaseHeight - 1), 18);

            if (_showDisplaySettings) DrawDisplaySettingsPage(graphics);
            else if (_showSettings) DrawSettingsPage(graphics);
            else DrawMainPage(graphics);

            if (_glassActive)
            {
                graphics.ResetTransform();
                using (var textBitmap = DirectWriteTextRenderer.Render(ClientSize.Width, ClientSize.Height, _layeredTextItems, GetDrawingScale()))
                    graphics.DrawImageUnscaled(textBitmap, 0, 0);
            }
        }

        private void DrawMainPage(Graphics graphics)
        {
            DrawHeader(graphics);
            DrawQuota(graphics);
            DrawActivity(graphics);
            DrawSettingsEntry(graphics);
            DrawFooter(graphics);
            DrawHoverHint(graphics);
        }

        private void DrawHeader(Graphics graphics)
        {
            DrawText(graphics, T("Codex 用量", "Codex Usage"), 14, new RectangleF(24, 18, 145, 28), _palette.Text, FontStyle.Bold);
            if (_result != null && !_result.IsCodexRunning)
            {
                var bounds = new Rectangle(145, 21, _settings.IsEnglish ? 104 : 96, 23);
                using (var background = new SolidBrush(Color.FromArgb(_palette.IsLight ? 24 : 34, _palette.Warning)))
                    graphics.FillRoundedRectangle(background, bounds, 11);
                using (var border = new Pen(Color.FromArgb(115, _palette.Warning)))
                    graphics.DrawRoundedRectangle(border, bounds, 11);
                DrawText(graphics, T("Codex 未启动", "Codex offline"), 7.8f, bounds, _palette.Warning, FontStyle.Bold, StringAlignment.Center);
            }
            var plan = GetPlanLabel();
            if (!string.IsNullOrWhiteSpace(plan))
                DrawText(graphics, plan, 9, new RectangleF(24, 47, 300, 22), _palette.Muted, FontStyle.Regular);

            DrawRefreshIcon(graphics, new PointF(383, 37));
            DrawDivider(graphics, 82);
        }

        private void DrawRefreshIcon(Graphics graphics, PointF center)
        {
            var fontScale = _settings.FontScalePercent / 100f;
            if (_hoverHint == HoverRefresh)
            {
                using (var background = new SolidBrush(Color.FromArgb(_palette.IsLight ? 22 : 34, _palette.Primary)))
                    graphics.FillEllipse(background, center.X - 15, center.Y - 15, 30, 30);
            }
            using (var font = new Font("Segoe MDL2 Assets", 20f * fontScale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(_hoverHint == HoverRefresh ? _palette.Primary : _palette.Muted))
            using (var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoClip
            })
            {
                graphics.DrawString("\uE72C", font, brush,
                    new RectangleF(center.X - 17, center.Y - 17, 34, 34), format);
            }
        }

        private void DrawQuota(Graphics graphics)
        {
            var primary = GetMainWindow();
            var weekly = _result == null || _result.Snapshot == null ? null : _result.Snapshot.DisplayWeeklyWindow;
            if (!_settings.QuotaPrimaryEmphasis && weekly != null && weekly != primary)
            {
                DrawEqualQuotaWindow(graphics, primary, 88);
                DrawEqualQuotaWindow(graphics, weekly, 180);
                DrawDivider(graphics, 276);
                return;
            }

            var remaining = primary == null ? 0 : primary.RemainingPercent;
            var timeRemaining = primary == null ? 0 : Math.Max(0, primary.TimeRemainingPercent);
            var accent = _palette.QuotaColor(remaining);
            string paceText;
            Color paceColor;
            GetPace(primary, remaining, timeRemaining, out paceText, out paceColor);

            DrawText(graphics, primary == null ? T("额度", "Quota") : GetWindowName(primary), 11,
                new RectangleF(24, 91, 210, 26), _palette.Text, FontStyle.Bold);
            DrawText(graphics, paceText, 9.2f, new RectangleF(240, 90, 156, 26), paceColor, FontStyle.Bold, StringAlignment.Far);

            DrawQuotaIcon(graphics, new Point(31, 128));
            DrawText(graphics, T("额度剩余", "Quota remaining"), 9.2f, new RectangleF(52, 115, 180, 26), _palette.Text, FontStyle.Regular);
            DrawText(graphics, primary == null ? "--" : remaining + "%", 9.8f, new RectangleF(300, 115, 96, 26), _palette.Text, FontStyle.Bold, StringAlignment.Far);
            DrawProgress(graphics, new Rectangle(24, 143, 372, 8), remaining, accent);

            DrawClockIcon(graphics, new Point(32, 171));
            DrawText(graphics, T("时间剩余", "Time remaining"), 9.2f, new RectangleF(52, 158, 180, 26), _palette.Text, FontStyle.Regular);
            DrawText(graphics, primary == null || !primary.ResetsAt.HasValue ? "--" : timeRemaining + "%", 9.8f,
                new RectangleF(300, 158, 96, 26), _palette.Text, FontStyle.Bold, StringAlignment.Far);
            DrawProgress(graphics, new Rectangle(24, 186, 372, 8), timeRemaining, _palette.Secondary);
            DrawResetDetails(graphics, primary, 199, 8.1f);

            if (weekly != null && weekly != primary)
            {
                var weeklyRemaining = weekly.RemainingPercent;
                var reserveActive = !string.IsNullOrWhiteSpace(weekly.Key) &&
                    weekly.Key.StartsWith("gpt-reserve:", StringComparison.OrdinalIgnoreCase);
                var weeklyCountdown = weekly.ResetsAt.HasValue
                    ? T("距重置 ", "Reset in ") + FormatCountdown(weekly.ResetsAt.Value - DateTimeOffset.Now)
                    : T("等待重置时间", "Waiting for reset time");
                DrawText(graphics, GetWindowName(weekly), reserveActive ? 8.6f : 9.8f,
                    new RectangleF(24, 225, reserveActive ? 180 : 140, 24), _palette.Text, FontStyle.Bold);
                DrawText(graphics, weeklyCountdown, reserveActive ? 7.7f : 8.1f,
                    new RectangleF(reserveActive ? 204 : 172, 225, reserveActive ? 142 : 174, 24),
                    _palette.Muted, FontStyle.Regular, StringAlignment.Far);
                DrawText(graphics, weeklyRemaining + "%", 9.8f, new RectangleF(352, 225, 44, 24),
                    _palette.QuotaColor(weeklyRemaining), FontStyle.Bold, StringAlignment.Far);
                DrawProgress(graphics, new Rectangle(24, 257, 372, 8), weeklyRemaining, _palette.QuotaColor(weeklyRemaining));
            }
            DrawDivider(graphics, 276);
        }

        private void DrawEqualQuotaWindow(Graphics graphics, QuotaWindow window, int top)
        {
            var remaining = window == null ? 0 : window.RemainingPercent;
            var timeRemaining = window == null ? 0 : Math.Max(0, window.TimeRemainingPercent);
            string paceText;
            Color paceColor;
            GetPace(window, remaining, timeRemaining, out paceText, out paceColor);
            DrawText(graphics, window == null ? T("额度", "Quota") : GetWindowName(window), 9.8f,
                new RectangleF(24, top, 180, 22), _palette.Text, FontStyle.Bold);
            DrawText(graphics, paceText, 8.2f, new RectangleF(210, top, 186, 22), paceColor, FontStyle.Bold, StringAlignment.Far);
            DrawText(graphics, T("额度", "Quota"), 8.2f, new RectangleF(24, top + 22, 80, 18), _palette.Muted, FontStyle.Regular);
            DrawText(graphics, window == null ? "--" : remaining + "%", 8.8f, new RectangleF(320, top + 22, 76, 18),
                _palette.QuotaColor(remaining), FontStyle.Bold, StringAlignment.Far);
            DrawProgress(graphics, new Rectangle(24, top + 43, 372, 6), remaining, _palette.QuotaColor(remaining));
            var countdown = window != null && window.ResetsAt.HasValue
                ? T("距重置 ", "Reset in ") + FormatCountdown(window.ResetsAt.Value - DateTimeOffset.Now)
                : T("等待重置时间", "Waiting for reset time");
            DrawText(graphics, countdown, 7.8f, new RectangleF(24, top + 53, 250, 18), _palette.Muted, FontStyle.Regular);
            DrawText(graphics, window == null || !window.ResetsAt.HasValue ? "--" : timeRemaining + "%", 8.2f,
                new RectangleF(320, top + 53, 76, 18), _palette.Secondary, FontStyle.Bold, StringAlignment.Far);
            DrawProgress(graphics, new Rectangle(24, top + 75, 372, 6), timeRemaining, _palette.Secondary);
        }

        private void DrawResetDetails(Graphics graphics, QuotaWindow window, int y, float fontSize)
        {
            var countdown = T("等待 Codex 写入额度快照", "Waiting for a Codex quota snapshot");
            var resetAt = "";
            if (window != null && window.ResetsAt.HasValue)
            {
                countdown = "⌛  " + T("距重置 ", "Reset in ") + FormatCountdown(window.ResetsAt.Value - DateTimeOffset.Now);
                resetAt = T("重置于 ", "Resets ") + (_settings.IsEnglish
                    ? window.ResetsAt.Value.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.GetCultureInfo("en-US"))
                    : window.ResetsAt.Value.ToLocalTime().ToString("M月d日 HH:mm"));
            }
            DrawText(graphics, countdown, fontSize, new RectangleF(24, y, 215, 20), _palette.Muted, FontStyle.Regular);
            DrawText(graphics, resetAt, fontSize, new RectangleF(230, y, 166, 20), _palette.Muted, FontStyle.Regular, StringAlignment.Far);
        }

        private void GetPace(QuotaWindow window, int remaining, int timeRemaining, out string text, out Color color)
        {
            var fast = window != null && QuotaUsageMath.IsConsumptionFast(remaining, timeRemaining);
            text = window == null ? T("等待快照", "Waiting") : (fast ? T("▲  消耗偏快", "▲  Above pace") : T("✓  节奏正常", "✓  On pace"));
            color = window == null ? _palette.Muted : (fast ? _palette.Warning : _palette.Success);
        }

        private void DrawActivity(Graphics graphics)
        {
            var buckets = BuildActivityBuckets();
            _currentBuckets = buckets;
            EnsureHoverWeights(buckets.Count);
            var total = buckets.Sum(item => item.Tokens);
            var quotaTotal = buckets.Sum(item => item.QuotaUsedPercent);
            var quotaName = ActivityQuotaName();
            DrawText(graphics, T("Token 活动", "Token activity"), 11, new RectangleF(24, 291, 190, 28), _palette.Text, FontStyle.Bold);
            DrawText(graphics, FormatTokenCount(total), 14, new RectangleF(250, 288, 146, 30), _palette.Text, FontStyle.Bold, StringAlignment.Far);
            DrawRangeChip(graphics, _range5Bounds, ActivityRange.Hours5, T("5小时", "5h"), HoverRange5);
            DrawRangeChip(graphics, _range24Bounds, ActivityRange.Hours24, T("24小时", "24h"), HoverRange24);
            DrawRangeChip(graphics, _range7Bounds, ActivityRange.Days7, T("7天", "7d"), HoverRange7);
            DrawRangeChip(graphics, _range30Bounds, ActivityRange.Days30, T("30天", "30d"), HoverRange30);
            var activityCaption = T("柱高按 token · ", "Bars: token · ") + quotaName + " " +
                (_quotaHistoryAvailable ? FormatPercent(quotaTotal) : "--");
            DrawText(graphics, activityCaption, 7.7f, new RectangleF(240, 320, 156, 27), _palette.Muted, FontStyle.Regular, StringAlignment.Far);
            DrawBars(graphics, buckets, new Rectangle(32, 358, 356, 89));
            DrawDivider(graphics, 459);
        }

        private void DrawRangeChip(Graphics graphics, Rectangle bounds, ActivityRange value, string label, int hoverTarget)
        {
            var selected = _settings.ActivityRange == value;
            var hovered = _hoverHint == hoverTarget;
            if (selected || hovered)
            {
                using (var brush = new SolidBrush(Color.FromArgb(selected ? (hovered ? 54 : 38) : 20, _palette.Primary))) graphics.FillRoundedRectangle(brush, bounds, 7);
                using (var pen = new Pen(Color.FromArgb(selected ? (hovered ? 220 : 170) : 100, _palette.Primary), hovered ? 1.4f : 1f)) graphics.DrawRoundedRectangle(pen, bounds, 7);
            }
            DrawText(graphics, label, 8.2f, bounds, selected || hovered ? _palette.Primary : _palette.Muted, selected ? FontStyle.Bold : FontStyle.Regular, StringAlignment.Center);
        }

        private void DrawBars(Graphics graphics, IList<ActivityBucket> values, Rectangle bounds)
        {
            using (var baseline = new Pen(Color.FromArgb(150, _palette.Divider))) graphics.DrawLine(baseline, bounds.Left, bounds.Bottom, bounds.Right, bounds.Bottom);
            if (values.Count == 0 || values.All(value => value.Tokens == 0))
            {
                DrawText(graphics, T("暂无本地 token 记录", "No local token activity"), 9, bounds, _palette.Faint, FontStyle.Regular, StringAlignment.Center);
                return;
            }

            var max = Math.Max(1L, values.Max(value => value.Tokens));
            var gap = ActivityBarGap(values.Count);
            var barWidth = Math.Max(4f, (bounds.Width - gap * (values.Count - 1)) / values.Count);
            for (var index = 0; index < values.Count; index++)
            {
                if (values[index].Tokens <= 0) continue;
                var weight = index < _hoverWeights.Length ? (float)_hoverWeights[index] : 0f;
                var height = Math.Max(3f, (float)(bounds.Height * values[index].Tokens / (double)max));
                height *= 1f + 0.14f * weight;
                var animatedWidth = barWidth * (1f + 0.16f * weight);
                var centerX = bounds.Left + index * (barWidth + gap) + barWidth / 2f;
                var bar = new RectangleF(centerX - animatedWidth / 2f, bounds.Bottom - height, animatedWidth, height);
                using (var brush = new SolidBrush(Blend(_palette.Primary, Color.White, weight * 0.16f)))
                using (var path = GraphicsExtensions.CreateRoundedPath(bar, Math.Min(3.5f, animatedWidth / 2f)))
                    graphics.FillPath(brush, path);
            }

            if (_hoverIndex >= 0 && _hoverIndex < values.Count && values[_hoverIndex].Tokens > 0)
                DrawActivityTooltip(graphics, values, bounds, barWidth, gap);
        }

        private void DrawActivityTooltip(Graphics graphics, IList<ActivityBucket> values, Rectangle bounds, float barWidth, float gap)
        {
            var bucket = values[_hoverIndex];
            var centerX = bounds.Left + _hoverIndex * (barWidth + gap) + barWidth / 2f;
            var width = _settings.IsEnglish ? 230 : 235;
            var x = Math.Max(bounds.Left, Math.Min(centerX - width / 2f, bounds.Right - width));
            var tooltip = new RectangleF(x, 350, width, 34);

            using (var shadow = new SolidBrush(Color.FromArgb(65, Color.Black)))
            using (var background = new SolidBrush(_palette.Surface))
            using (var border = new Pen(Color.FromArgb(180, _palette.Primary)))
            {
                graphics.FillRoundedRectangle(shadow, Rectangle.Round(new RectangleF(tooltip.X + 2, tooltip.Y + 3, tooltip.Width, tooltip.Height)), 8);
                graphics.FillRoundedRectangle(background, Rectangle.Round(tooltip), 8);
                graphics.DrawRoundedRectangle(border, Rectangle.Round(tooltip), 8);
            }

            var local = bucket.Start.ToLocalTime();
            var period = _settings.ActivityRange == ActivityRange.Hours5 || _settings.ActivityRange == ActivityRange.Hours24
                ? local.ToString("HH:mm")
                : (_settings.IsEnglish
                    ? local.ToString("MMM d", CultureInfo.GetCultureInfo("en-US"))
                    : local.ToString("M月d日"));
            var label = period + "  ·  " + FormatTokenCount(bucket.Tokens) + " token";
            label += "  ·  " + ActivityQuotaName() + T("变化 ", " change ") +
                (bucket.HasQuotaData ? FormatPercent(bucket.QuotaUsedPercent) : "--");
            DrawText(graphics, label, 7.8f, tooltip, _palette.Text, FontStyle.Regular, StringAlignment.Center);
        }

        private float ActivityBarGap(int bucketCount)
        {
            if (_settings.ActivityRange == ActivityRange.Hours5) return 5f;
            return bucketCount >= 24 ? 3f : 9f;
        }

        private string ActivityQuotaName()
        {
            return _settings.ActivityRange == ActivityRange.Days7 || _settings.ActivityRange == ActivityRange.Days30
                ? T("周额度", "weekly quota")
                : T("5小时额度", "5h quota");
        }

        private void DrawSettingsEntry(Graphics graphics)
        {
            if (_hoverHint == HoverSettingsEntry)
            {
                using (var background = new SolidBrush(Color.FromArgb(_palette.IsLight ? 18 : 28, _palette.Primary)))
                    graphics.FillRoundedRectangle(background, _settingsBounds, 9);
            }
            DrawGearIcon(graphics, new Point(38, 498));
            DrawText(graphics, T("设置", "Settings"), 10.5f, new RectangleF(61, 477, 210, 28), _palette.Text, FontStyle.Regular);
            DrawText(graphics, T("配色、语言、数字条与更多设置", "Color, language, meter and more"), 8.5f,
                new RectangleF(61, 501, 290, 22), _palette.Muted, FontStyle.Regular);
            DrawText(graphics, "›", 17, new RectangleF(374, 482, 20, 30), _palette.Muted, FontStyle.Regular, StringAlignment.Far);
            DrawDivider(graphics, 536);
        }

        private void DrawHoverHint(Graphics graphics)
        {
            if (_hoverHint != HoverRefresh) return;
            var bounds = new RectangleF(_settings.IsEnglish ? 226 : 238, 52, _settings.IsEnglish ? 170 : 158, 25);
            DrawContextHint(graphics, bounds, T("刷新最新本地额度快照", "Refresh local quota snapshot"));
        }

        private void DrawContextHint(Graphics graphics, RectangleF bounds, string text)
        {
            using (var shadow = new SolidBrush(Color.FromArgb(48, Color.Black)))
            using (var background = new SolidBrush(_palette.Surface))
            using (var border = new Pen(Color.FromArgb(150, _palette.Primary)))
            {
                graphics.FillRoundedRectangle(shadow, Rectangle.Round(new RectangleF(bounds.X + 2, bounds.Y + 2, bounds.Width, bounds.Height)), 7);
                graphics.FillRoundedRectangle(background, Rectangle.Round(bounds), 7);
                graphics.DrawRoundedRectangle(border, Rectangle.Round(bounds), 7);
            }
            DrawText(graphics, text, 8.2f, bounds, _palette.Text, FontStyle.Regular, StringAlignment.Center);
        }

        private void DrawSettingsPage(Graphics graphics)
        {
            DrawBackIcon(graphics);
            DrawText(graphics, T("设置", "Settings"), 14, new RectangleF(60, 18, 250, 28), _palette.Text, FontStyle.Bold);
            DrawText(graphics, T("更改会自动保存", "Changes save automatically"), 8.5f, new RectangleF(60, 47, 260, 22), _palette.Muted, FontStyle.Regular);
            DrawText(graphics, T("恢复默认", "Defaults"), 8.8f, _resetBounds,
                InteractiveTextColor(HoverReset, _palette.Muted), FontStyle.Regular, StringAlignment.Far);
            DrawDivider(graphics, 82);

            DrawText(graphics, T("任务栏数字条", "Taskbar meter"), 10, new RectangleF(24, 92, 210, 28), _palette.Text, FontStyle.Regular);
            DrawText(graphics, _settings.WidgetVisible ? T("显示完整百分比", "Show full percentage") : T("仅保留托盘图标", "Tray icon only"), 8.2f,
                new RectangleF(24, 114, 250, 20), _palette.Muted, FontStyle.Regular);
            DrawToggle(graphics, _widgetToggleBounds, _settings.WidgetVisible, HoverWidgetToggle);
            DrawDivider(graphics, 142);

            DrawText(graphics, T("5小时额度突出显示", "Emphasize 5-hour quota"), 10, new RectangleF(24, 148, 260, 28), _palette.Text, FontStyle.Regular);
            DrawText(graphics, _settings.QuotaPrimaryEmphasis ? T("主进度条 + 周额度小条", "Large primary + compact weekly") : T("两条进度并列等大", "Two equal quota rows"), 8.2f,
                new RectangleF(24, 170, 300, 20), _palette.Muted, FontStyle.Regular);
            DrawToggle(graphics, _quotaLayoutToggleBounds, _settings.QuotaPrimaryEmphasis, HoverQuotaLayoutToggle);
            DrawDivider(graphics, 208);

            DrawText(graphics, T("配色", "Color theme"), 10, new RectangleF(24, 215, 180, 24), _palette.Text, FontStyle.Bold);
            DrawSegment(graphics, _appearanceSystemBounds, _settings.AppearanceMode == "system", T("跟随", "Auto"), HoverAppearanceSystem);
            DrawSegment(graphics, _appearanceDarkBounds, _settings.AppearanceMode == "dark", T("深色", "Dark"), HoverAppearanceDark);
            DrawSegment(graphics, _appearanceLightBounds, _settings.AppearanceMode == "light", T("浅色", "Light"), HoverAppearanceLight);
            var presets = ThemePalette.Presets();
            for (var index = 0; index < presets.Count; index++) DrawThemeChoice(graphics, _themeBounds[index], presets[index], HoverThemeFirst + index);
            DrawCustomThemeChoice(graphics, _themeBounds[4]);

            DrawText(graphics, T("语言", "Language"), 10, new RectangleF(24, 417, 150, 34), _palette.Text, FontStyle.Regular);
            DrawSegment(graphics, _languageSystemBounds, _settings.Language == "system", T("跟随", "Auto"), HoverLanguageSystem);
            DrawSegment(graphics, _languageZhBounds, _settings.Language == "zh", "中文", HoverLanguageZh);
            DrawSegment(graphics, _languageEnBounds, _settings.Language == "en", "English", HoverLanguageEn);
            DrawDivider(graphics, 462);

            if (_hoverHint == HoverDisplaySettings)
            {
                using (var background = new SolidBrush(Color.FromArgb(_palette.IsLight ? 18 : 28, _palette.Primary)))
                    graphics.FillRoundedRectangle(background, _displaySettingsBounds, 6);
            }
            DrawText(graphics, T("更多设置", "More settings"), 10.5f, new RectangleF(24, 467, 180, 29), _palette.Text, FontStyle.Regular);
            DrawText(graphics, T("尺寸、玻璃、提醒与版本", "Size, glass, alerts and version"), 7.6f, new RectangleF(24, 496, 300, 20), _palette.Muted, FontStyle.Regular);
            DrawText(graphics, "›", 15, new RectangleF(372, 480, 20, 28), _palette.Muted, FontStyle.Regular, StringAlignment.Far);
            DrawDivider(graphics, 536);
            DrawText(graphics, T("返回首页", "Back to home"), 8.8f, _homeBounds,
                InteractiveTextColor(HoverHome, _palette.Muted), FontStyle.Regular);
            DrawText(graphics, T("退出", "Quit"), 10, _exitBounds, InteractiveTextColor(HoverExit, _palette.Text), FontStyle.Regular, StringAlignment.Far);
        }

        private void DrawDisplaySettingsPage(Graphics graphics)
        {
            DrawBackIcon(graphics);
            DrawText(graphics, T("更多设置", "More settings"), 14, new RectangleF(60, 18, 250, 28), _palette.Text, FontStyle.Bold);
            DrawText(graphics, T("尺寸、玻璃、提醒与版本", "Size, glass, alerts and version"), 8.5f,
                new RectangleF(60, 47, 300, 22), _palette.Muted, FontStyle.Regular);
            DrawDivider(graphics, 82);

            DrawSizeSliderRow(graphics, 0, 82, 142, T("悬浮窗", "Popup"), _popupScaleSliderBounds,
                _settings.PopupScalePercent, 80, 120, new[] { 80, 90, 100, 110, 120 });
            DrawDivider(graphics, 142);
            DrawSizeSliderRow(graphics, 1, 142, 202, T("字体", "Font"), _fontScaleSliderBounds,
                _settings.FontScalePercent, 80, 120, new[] { 80, 90, 100, 110, 120 });
            DrawDivider(graphics, 202);
            DrawSizeSliderRow(graphics, 2, 202, 262, T("任务栏数字条", "Taskbar meter"), _widgetScaleSliderBounds,
                _settings.WidgetScalePercent, 70, 130, new[] { 70, 80, 90, 100, 110, 120, 130 });
            DrawDivider(graphics, 262);

            DrawText(graphics, T("柔光玻璃", "Soft glass"), 9.4f,
                new RectangleF(24, 266, 260, 24), _palette.Text, FontStyle.Regular);
            DrawText(graphics, T("动态桌面背景效果", "Dynamic desktop backdrop"), 7.8f,
                new RectangleF(24, 285, 300, 18), _palette.Muted, FontStyle.Regular);
            DrawToggle(graphics, _glassToggleBounds, _settings.GlassEnabled, HoverGlassToggle);
            DrawDivider(graphics, 306);

            DrawSizeSliderRow(graphics, 3, 306, 372, T("柔光玻璃不透明度", "Glass opacity"), _glassOpacitySliderBounds,
                _settings.GlassOpacityPercent, 5, 95, new[] { 5, 20, 40, 60, 80, 95 }, _settings.GlassEnabled);
            if (!_settings.GlassEnabled)
                DrawText(graphics, T("开启柔光玻璃后可调整", "Enable soft glass to adjust"), 7.5f,
                    new RectangleF(176, 353, 210, 17), _palette.Faint, FontStyle.Regular, StringAlignment.Center);
            DrawDivider(graphics, 372);

            DrawText(graphics, T("任务完成闪烁提醒", "Task completion flash"), 9.4f,
                new RectangleF(24, 376, 260, 24), _palette.Text, FontStyle.Regular);
            DrawText(graphics, T("闪烁时点击数字条可清除提醒", "Click the taskbar meter to clear a flash"), 7.8f,
                new RectangleF(24, 395, 300, 18), _palette.Muted, FontStyle.Regular);
            DrawToggle(graphics, _completionReminderToggleBounds, _settings.CompletionReminderEnabled, HoverCompletionReminderToggle);
            DrawDivider(graphics, 416);

            DrawText(graphics, T("版本与更新", "Version & update"), 10, new RectangleF(24, 420, 180, 26), _palette.Text, FontStyle.Bold);
            DrawText(graphics, "v" + UpdateService.CurrentVersion().ToString(3), 9,
                new RectangleF(300, 420, 96, 26), _palette.Primary, FontStyle.Bold, StringAlignment.Far);
            var updateHovered = _hoverHint == HoverCheckUpdate && !_updateBusy;
            var updateColor = _updateBusy ? _palette.Muted : _palette.Primary;
            using (var updateBackground = new SolidBrush(updateHovered
                ? Blend(_palette.Surface, _palette.Primary, _palette.IsLight ? 0.08f : 0.13f)
                : _palette.Surface)) graphics.FillRoundedRectangle(updateBackground, _checkUpdateBounds, 9);
            using (var updateBorder = new Pen(updateColor, updateHovered ? 1.6f : 1f))
                graphics.DrawRoundedRectangle(updateBorder, _checkUpdateBounds, 9);
            DrawText(graphics, _updateBusy ? T("正在处理…", "Working…") : T("检查更新", "Check for updates"), 9.2f,
                _checkUpdateBounds, updateColor, FontStyle.Bold, StringAlignment.Center);

            var status = _settings.IsEnglish ? _updateStatusEnglish : _updateStatusChinese;
            if (string.IsNullOrWhiteSpace(status)) status = T("尚未检查更新", "Not checked yet");
            DrawText(graphics, status, 8, new RectangleF(160, 447, 236, 25), _palette.Muted, FontStyle.Regular, StringAlignment.Far);
            DrawText(graphics, T("作者：渡缘笙SYD", "Author: 渡缘笙SYD"), 8.2f,
                new RectangleF(24, 447, 130, 25), _palette.Muted, FontStyle.Regular);
            DrawSegment(graphics, _bilibiliBounds, false, T("B站主页", "Bilibili"), HoverBilibili);
            DrawSegment(graphics, _githubBounds, false, "GitHub", HoverGitHub);

            DrawDivider(graphics, 536);
            DrawText(graphics, T("返回设置", "Back to settings"), 8.8f, _returnSettingsBounds, InteractiveTextColor(HoverReturnSettings, _palette.Muted), FontStyle.Regular);
            DrawText(graphics, T("退出", "Quit"), 10, _exitBounds, InteractiveTextColor(HoverExit, _palette.Text), FontStyle.Regular, StringAlignment.Far);
        }

        private void DrawSizeSliderRow(
            Graphics graphics,
            int sliderIndex,
            int rowTop,
            int rowBottom,
            string label,
            Rectangle bounds,
            int current,
            int minimum,
            int maximum,
            int[] recommended,
            bool enabled = true)
        {
            var hoverTarget = sliderIndex == 3 ? HoverGlassOpacitySlider : HoverPopupSlider + sliderIndex;
            var hovered = enabled && _hoverHint == hoverTarget;
            var accent = enabled ? _palette.Primary : _palette.Faint;
            DrawText(graphics, label, 9.8f, new RectangleF(24, rowTop, 145, rowBottom - rowTop), enabled ? _palette.Text : _palette.Faint, FontStyle.Regular);

            var trackY = bounds.Y + 6;
            using (var track = new Pen(hovered ? Blend(_palette.Track, _palette.Primary, 0.10f) : _palette.Track, 4f))
            using (var fill = new Pen(hovered ? Blend(accent, Color.White, 0.05f) : accent, 4f))
            {
                track.StartCap = track.EndCap = LineCap.Round;
                fill.StartCap = fill.EndCap = LineCap.Round;
                graphics.DrawLine(track, bounds.Left, trackY, bounds.Right, trackY);
                var valueX = SliderValueToX(bounds, current, minimum, maximum);
                graphics.DrawLine(fill, bounds.Left, trackY, valueX, trackY);
            }

            foreach (var value in recommended)
            {
                var x = SliderValueToX(bounds, value, minimum, maximum);
                var active = value == current;
                var diameter = active ? 9f : 7f;
                using (var dot = new SolidBrush(active ? accent : _palette.Background))
                using (var outline = new Pen(active ? Color.FromArgb(120, accent) : (enabled ? _palette.Muted : _palette.Faint), active ? 2.2f : 1.4f))
                {
                    graphics.FillEllipse(dot, x - diameter / 2f, trackY - diameter / 2f, diameter, diameter);
                    graphics.DrawEllipse(outline, x - diameter / 2f, trackY - diameter / 2f, diameter, diameter);
                }
            }

            var knobX = SliderValueToX(bounds, current, minimum, maximum);
            using (var knob = new SolidBrush(hovered ? Blend(accent, Color.White, 0.05f) : accent)) graphics.FillEllipse(knob, knobX - 5, trackY - 5, 10, 10);
            using (var center = new SolidBrush(_palette.Surface)) graphics.FillEllipse(center, knobX - 2, trackY - 2, 4, 4);

            var onSnapPoint = recommended.Contains(current);
            var pulse = sliderIndex == _snapSliderIndex ? (float)_snapPulse : 0f;
            var bubbleWidth = 44f + (onSnapPoint ? 2f : 0f) + pulse * 5f;
            var bubbleHeight = 23f + (onSnapPoint ? 1f : 0f) + pulse * 3f;
            var bubbleX = Math.Max(bounds.Left, Math.Min(knobX - bubbleWidth / 2f, bounds.Right - bubbleWidth));
            var bubbleY = trackY - 32f - (onSnapPoint ? 2f : 0f) - pulse * 4f;
            var bubbleBounds = new RectangleF(bubbleX, bubbleY, bubbleWidth, bubbleHeight);
            using (var bubble = new SolidBrush(accent))
            using (var path = GraphicsExtensions.CreateRoundedPath(bubbleBounds, 7))
                graphics.FillPath(bubble, path);
            using (var pointer = new GraphicsPath())
            using (var pointerBrush = new SolidBrush(accent))
            {
                var pointerX = Math.Max(bubbleBounds.Left + 8, Math.Min(knobX, bubbleBounds.Right - 8));
                pointer.AddPolygon(new[]
                {
                    new PointF(pointerX - 4, bubbleBounds.Bottom - 1),
                    new PointF(pointerX + 4, bubbleBounds.Bottom - 1),
                    new PointF(pointerX, bubbleBounds.Bottom + 4)
                });
                graphics.FillPath(pointerBrush, pointer);
            }
            DrawText(graphics, current + "%", 8.2f, bubbleBounds, ContrastingText(accent),
                FontStyle.Bold, StringAlignment.Center);
        }

        private void DrawThemeChoice(Graphics graphics, Rectangle bounds, ThemePalette theme, int hoverTarget)
        {
            var selected = string.Equals(_settings.ThemeId, theme.Id, StringComparison.OrdinalIgnoreCase);
            var hovered = _hoverHint == hoverTarget;
            var background = selected ? Color.FromArgb(hovered ? 54 : 36, theme.Primary) : (hovered ? Blend(_palette.Surface, theme.Primary, _palette.IsLight ? 0.05f : 0.09f) : _palette.Surface);
            using (var brush = new SolidBrush(background)) graphics.FillRoundedRectangle(brush, bounds, 9);
            using (var pen = new Pen(selected || hovered ? theme.Primary : _palette.Divider, hovered ? 1.8f : (selected ? 1.4f : 1f))) graphics.DrawRoundedRectangle(pen, bounds, 9);
            using (var first = new SolidBrush(theme.Primary)) graphics.FillEllipse(first, bounds.X + 12, bounds.Y + 12, 18, 18);
            using (var second = new SolidBrush(theme.Secondary)) graphics.FillEllipse(second, bounds.X + 24, bounds.Y + 12, 18, 18);
            DrawText(graphics, theme.Name(_settings.IsEnglish), 8.7f, new RectangleF(bounds.X + 50, bounds.Y + 5, bounds.Width - 58, bounds.Height - 10),
                selected || hovered ? (hovered ? Blend(theme.Primary, Color.White, 0.12f) : theme.Primary) : _palette.Text, selected ? FontStyle.Bold : FontStyle.Regular);
        }

        private void DrawCustomThemeChoice(Graphics graphics, Rectangle bounds)
        {
            var selected = string.Equals(_settings.ThemeId, "custom", StringComparison.OrdinalIgnoreCase);
            var hovered = _hoverHint == HoverThemeFirst + 4;
            var background = selected ? Color.FromArgb(hovered ? 54 : 36, _palette.Primary) : (hovered ? Blend(_palette.Surface, _palette.Primary, _palette.IsLight ? 0.05f : 0.09f) : _palette.Surface);
            using (var brush = new SolidBrush(background)) graphics.FillRoundedRectangle(brush, bounds, 9);
            using (var pen = new Pen(selected || hovered ? _palette.Primary : _palette.Divider, hovered ? 1.8f : (selected ? 1.4f : 1f))) graphics.DrawRoundedRectangle(pen, bounds, 9);
            DrawText(graphics, T("自定义配色", "Custom colors"), 9, new RectangleF(bounds.X + 14, bounds.Y + 5, 210, 25), _palette.Text, FontStyle.Regular);
            DrawText(graphics, T("点击色块修改主色与辅色", "Click swatches to edit"), 7.8f, new RectangleF(bounds.X + 14, bounds.Y + 27, 250, 18), _palette.Muted, FontStyle.Regular);
            DrawColorSwatch(graphics, _customPrimaryBounds, _settings.CustomPrimary, HoverCustomPrimary);
            DrawColorSwatch(graphics, _customSecondaryBounds, _settings.CustomSecondary, HoverCustomSecondary);
        }

        private void DrawColorSwatch(Graphics graphics, Rectangle bounds, Color color, int hoverTarget)
        {
            var hovered = _hoverHint == hoverTarget;
            using (var brush = new SolidBrush(hovered ? Blend(color, Color.White, 0.12f) : color)) graphics.FillRoundedRectangle(brush, bounds, 7);
            using (var pen = new Pen(Color.FromArgb(hovered ? 235 : 150, Color.White), hovered ? 1.5f : 1f)) graphics.DrawRoundedRectangle(pen, bounds, 7);
        }

        private void DrawToggle(Graphics graphics, Rectangle bounds, bool enabled, int hoverTarget)
        {
            var hovered = _hoverHint == hoverTarget;
            var color = enabled ? _palette.Primary : _palette.Track;
            if (hovered) color = Blend(color, enabled ? Color.White : _palette.Primary, enabled ? 0.12f : 0.22f);
            using (var brush = new SolidBrush(color)) graphics.FillRoundedRectangle(brush, bounds, bounds.Height / 2);
            var diameter = bounds.Height - 8;
            var x = enabled ? bounds.Right - diameter - 4 : bounds.Left + 4;
            using (var knob = new SolidBrush(Color.White)) graphics.FillEllipse(knob, x, bounds.Y + 4, diameter, diameter);
        }

        private void DrawSegment(Graphics graphics, Rectangle bounds, bool selected, string label, int hoverTarget)
        {
            var hovered = _hoverHint == hoverTarget;
            var background = selected ? Color.FromArgb(hovered ? 58 : 40, _palette.Primary) : (hovered ? Blend(_palette.Surface, _palette.Primary, _palette.IsLight ? 0.06f : 0.11f) : _palette.Surface);
            using (var brush = new SolidBrush(background)) graphics.FillRoundedRectangle(brush, bounds, 7);
            using (var pen = new Pen(selected || hovered ? _palette.Primary : _palette.Divider, hovered ? 1.4f : 1f)) graphics.DrawRoundedRectangle(pen, bounds, 7);
            DrawText(graphics, label, 8.3f, bounds, selected || hovered ? (hovered ? Blend(_palette.Primary, Color.White, 0.12f) : _palette.Primary) : _palette.Muted, selected ? FontStyle.Bold : FontStyle.Regular, StringAlignment.Center);
        }

        private void DrawBackIcon(Graphics graphics)
        {
            if (_hoverHint == HoverBack)
            {
                using (var background = new SolidBrush(Color.FromArgb(_palette.IsLight ? 18 : 28, _palette.Primary)))
                    graphics.FillEllipse(background, 23, 22, 30, 30);
            }
            using (var pen = new Pen(_hoverHint == HoverBack ? _palette.Primary : _palette.Muted, 1.8f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                graphics.DrawLine(pen, 42, 28, 32, 37);
                graphics.DrawLine(pen, 32, 37, 42, 46);
            }
        }

        private void HandleSettingsClick(Point point)
        {
            if (_backBounds.Contains(point) || _homeBounds.Contains(point)) { _showSettings = false; Invalidate(); return; }
            if (_widgetToggleBounds.Contains(point)) { _settings.WidgetVisible = !_settings.WidgetVisible; CommitSettings(); return; }
            if (_quotaLayoutToggleBounds.Contains(point)) { _settings.QuotaPrimaryEmphasis = !_settings.QuotaPrimaryEmphasis; CommitSettings(); return; }
            if (_appearanceSystemBounds.Contains(point)) { _settings.AppearanceMode = "system"; CommitSettings(); return; }
            if (_appearanceDarkBounds.Contains(point)) { _settings.AppearanceMode = "dark"; CommitSettings(); return; }
            if (_appearanceLightBounds.Contains(point)) { _settings.AppearanceMode = "light"; CommitSettings(); return; }

            var presets = ThemePalette.Presets();
            for (var index = 0; index < presets.Count; index++)
            {
                if (_themeBounds[index].Contains(point))
                {
                    _settings.ThemeId = presets[index].Id;
                    CommitSettings();
                    return;
                }
            }

            if (_customPrimaryBounds.Contains(point)) { EditCustomColor(true); return; }
            if (_customSecondaryBounds.Contains(point)) { EditCustomColor(false); return; }
            if (_themeBounds[4].Contains(point)) { _settings.ThemeId = "custom"; CommitSettings(); return; }
            if (_languageSystemBounds.Contains(point)) { _settings.Language = "system"; CommitSettings(); return; }
            if (_languageZhBounds.Contains(point)) { _settings.Language = "zh"; CommitSettings(); return; }
            if (_languageEnBounds.Contains(point)) { _settings.Language = "en"; CommitSettings(); return; }
            if (_displaySettingsBounds.Contains(point)) { _showDisplaySettings = true; Invalidate(); return; }
            if (_resetBounds.Contains(point)) { _settings.Reset(); CommitSettings(); return; }
        }

        private void HandleDisplaySettingsClick(Point point)
        {
            if (_backBounds.Contains(point) || _returnSettingsBounds.Contains(point)) { _showDisplaySettings = false; Invalidate(); return; }
            if (_glassToggleBounds.Contains(point)) { _settings.GlassEnabled = !_settings.GlassEnabled; CommitSettings(); return; }
            if (_completionReminderToggleBounds.Contains(point)) { _settings.CompletionReminderEnabled = !_settings.CompletionReminderEnabled; CommitSettings(); return; }
            if (_checkUpdateBounds.Contains(point) && !_updateBusy) { Raise(UpdateCheckRequested); return; }
            if (_bilibiliBounds.Contains(point)) { OpenUrl("https://space.bilibili.com/1266346466"); return; }
            if (_githubBounds.Contains(point)) OpenUrl(UpdateService.RepositoryUrl);
        }

        private void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch
            {
                MessageBox.Show(this, T("无法打开网页：", "Could not open the page: ") + url,
                    T("打开链接", "Open link"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private bool TryBeginSlider(Point point)
        {
            if (_popupScaleSliderBounds.Contains(point)) _activeSlider = 0;
            else if (_fontScaleSliderBounds.Contains(point)) _activeSlider = 1;
            else if (_widgetScaleSliderBounds.Contains(point)) _activeSlider = 2;
            else if (_settings.GlassEnabled && _glassOpacitySliderBounds.Contains(point)) _activeSlider = 3;
            else return false;
            return true;
        }

        private void UpdateActiveSlider(int x, bool commit)
        {
            Rectangle bounds;
            int minimum;
            int maximum;
            int[] recommended;
            if (_activeSlider == 0)
            {
                bounds = _popupScaleSliderBounds;
                minimum = 80;
                maximum = 120;
                recommended = new[] { 80, 90, 100, 110, 120 };
            }
            else if (_activeSlider == 1)
            {
                bounds = _fontScaleSliderBounds;
                minimum = 80;
                maximum = 120;
                recommended = new[] { 80, 90, 100, 110, 120 };
            }
            else if (_activeSlider == 2)
            {
                bounds = _widgetScaleSliderBounds;
                minimum = 70;
                maximum = 130;
                recommended = new[] { 70, 80, 90, 100, 110, 120, 130 };
            }
            else
            {
                bounds = _glassOpacitySliderBounds;
                minimum = 5;
                maximum = 95;
                recommended = new[] { 5, 20, 40, 60, 80, 95 };
            }

            var value = SliderXToValue(bounds, x, minimum, maximum);
            var snapped = false;
            foreach (var snap in recommended)
            {
                if (Math.Abs(value - snap) > 3) continue;
                value = snap;
                snapped = true;
                break;
            }

            if (_activeSlider == 0) _settings.PopupScalePercent = value;
            else if (_activeSlider == 1) _settings.FontScalePercent = value;
            else if (_activeSlider == 2) _settings.WidgetScalePercent = value;
            else
            {
                _settings.GlassOpacityPercent = value;
                if (IsHandleCreated) ApplyWindowMaterial();
            }

            if (commit)
            {
                if (snapped)
                {
                    _snapSliderIndex = _activeSlider;
                    _snapPulse = 1d;
                    _sliderSnapTimer.Start();
                }
                CommitSettings();
            }
            else Invalidate();
        }

        private static int SliderXToValue(Rectangle bounds, int x, int minimum, int maximum)
        {
            var ratio = (Math.Max(bounds.Left, Math.Min(bounds.Right, x)) - bounds.Left) / (double)bounds.Width;
            return minimum + (int)Math.Round((maximum - minimum) * ratio);
        }

        private static int SliderValueToX(Rectangle bounds, int value, int minimum, int maximum)
        {
            var clamped = Math.Max(minimum, Math.Min(maximum, value));
            return bounds.Left + (int)Math.Round(bounds.Width * (clamped - minimum) / (double)(maximum - minimum));
        }

        private void EditCustomColor(bool primary)
        {
            using (var dialog = new ColorDialog
            {
                FullOpen = true,
                AnyColor = true,
                Color = primary ? _settings.CustomPrimary : _settings.CustomSecondary
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (primary) _settings.CustomPrimary = dialog.Color;
                else _settings.CustomSecondary = dialog.Color;
                _settings.ThemeId = "custom";
                CommitSettings();
            }
        }

        private void SetActivityRange(ActivityRange range)
        {
            if (_settings.ActivityRange == range) return;
            _settings.ActivityRange = range;
            CommitSettings();
        }

        private void CommitSettings()
        {
            _palette = ThemePalette.FromSettings(_settings);
            BackColor = _palette.Background;
            if (IsHandleCreated) ApplyWindowMaterial();
            ApplyScaledSize();
            Invalidate();
            Raise(SettingsChanged);
            if (Visible && !_lastAnchorBounds.IsEmpty)
                BeginInvoke(new Action(delegate { ShowCenteredAbove(_lastAnchorBounds); }));
        }

        private List<ActivityBucket> BuildActivityBuckets()
        {
            _quotaHistoryAvailable = false;
            var samples = _result == null || _result.Activity == null ? new List<TokenActivitySample>() : _result.Activity;
            var localNow = DateTimeOffset.Now;
            if (_settings.ActivityRange == ActivityRange.Hours5)
            {
                var window = GetMainWindow();
                if (window == null || window.WindowMinutes != 300 || !window.ResetsAt.HasValue)
                    return new List<ActivityBucket>();

                var start = window.ResetsAt.Value.AddMinutes(-window.WindowMinutes);
                var fiveHourBucketCount = window.WindowMinutes / QuotaUsageMath.FiveHourBucketMinutes;
                var buckets = Enumerable.Range(0, fiveHourBucketCount)
                    .Select(index => new ActivityBucket { Start = start.AddMinutes(index * QuotaUsageMath.FiveHourBucketMinutes) })
                    .ToList();
                var timeline = _result == null || _result.ActivityTimeline == null
                    ? new List<TokenActivitySample>()
                    : _result.ActivityTimeline;
                foreach (var source in timeline.GroupBy(sample => sample.SourceKey ?? ""))
                {
                    var ordered = source.OrderBy(sample => sample.CapturedAt).ToList();
                    var previous = ordered.LastOrDefault(sample => sample.CapturedAt < start);
                    long? previousTokens = previous == null ? (long?)null : previous.Tokens;
                    foreach (var sample in ordered.Where(sample => sample.CapturedAt >= start && sample.CapturedAt < window.ResetsAt.Value))
                    {
                        var delta = TokenActivityMath.CalculateDelta(previousTokens, sample.Tokens, sample.SourceStartedAt, start);
                        var index = QuotaUsageMath.FiveHourBucketIndex(sample.CapturedAt, start);
                        if (delta > 0 && index >= 0 && index < buckets.Count)
                            buckets[index].Tokens += delta;
                        previousTokens = sample.Tokens;
                    }
                }
                PopulateQuotaUsage(buckets, start);
                return buckets;
            }

            if (_settings.ActivityRange == ActivityRange.Hours24)
            {
                var start = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, localNow.Hour, 0, 0, localNow.Offset).AddHours(-23);
                var buckets = Enumerable.Range(0, 24)
                    .Select(index => new ActivityBucket { Start = start.AddHours(index) })
                    .ToList();
                foreach (var sample in samples)
                {
                    var local = sample.CapturedAt.ToLocalTime();
                    var hour = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset);
                    var index = (int)Math.Floor((hour - start).TotalHours);
                    if (index >= 0 && index < buckets.Count) buckets[index].Tokens += sample.Tokens;
                }
                PopulateQuotaUsage(buckets, start);
                return buckets;
            }

            var count = _settings.ActivityRange == ActivityRange.Days7 ? 7 : 30;
            var firstDay = DateTime.Today.AddDays(-(count - 1));
            var daily = Enumerable.Range(0, count)
                .Select(index => new ActivityBucket
                {
                    Start = new DateTimeOffset(firstDay.AddDays(index))
                })
                .ToList();
            foreach (var sample in samples)
            {
                var index = (sample.CapturedAt.ToLocalTime().Date - firstDay).Days;
                if (index >= 0 && index < daily.Count) daily[index].Tokens += sample.Tokens;
            }
            PopulateQuotaUsage(daily, daily[0].Start);
            return daily;
        }

        private void PopulateQuotaUsage(List<ActivityBucket> buckets, DateTimeOffset start)
        {
            _quotaHistoryAvailable = false;
            var window = GetActivityQuotaWindow();
            if (window == null || _result == null || _result.QuotaHistory == null || buckets.Count == 0) return;

            var matching = _result.QuotaHistory
                .Where(sample => QuotaUsageMath.IsSameWindow(sample, window))
                .OrderBy(sample => sample.CapturedAt)
                .ToList();
            if (matching.Count < 2) return;

            var previous = matching.LastOrDefault(sample => sample.CapturedAt.ToLocalTime() < start);
            var inRange = matching
                .Where(sample => sample.CapturedAt.ToLocalTime() >= start && sample.CapturedAt <= DateTimeOffset.Now)
                .ToList();
            if (previous == null && inRange.Count > 0)
            {
                previous = inRange[0];
                inRange.RemoveAt(0);
            }
            if (previous == null || inRange.Count == 0) return;

            foreach (var current in inRange)
            {
                var used = QuotaUsageMath.CalculateUsedPercent(previous, current, window.WindowMinutes);
                var local = current.CapturedAt.ToLocalTime();
                int index;
                if (_settings.ActivityRange == ActivityRange.Hours5)
                {
                    index = QuotaUsageMath.FiveHourBucketIndex(current.CapturedAt, start);
                }
                else if (_settings.ActivityRange == ActivityRange.Hours24)
                {
                    var hour = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset);
                    index = (int)Math.Floor((hour - start).TotalHours);
                }
                else index = (local.Date - start.LocalDateTime.Date).Days;

                if (index >= 0 && index < buckets.Count)
                {
                    buckets[index].HasQuotaData = true;
                    if (used > 0) buckets[index].QuotaUsedPercent += used;
                }
                previous = current;
            }
            _quotaHistoryAvailable = buckets.Any(bucket => bucket.HasQuotaData);
        }

        private QuotaWindow GetActivityQuotaWindow()
        {
            if (_result == null || _result.Snapshot == null) return null;
            return _settings.ActivityRange == ActivityRange.Days7 || _settings.ActivityRange == ActivityRange.Days30
                ? _result.Snapshot.WeeklyWindow
                : _result.Snapshot.FiveHourWindow;
        }

        private void SetHoverIndex(int index)
        {
            if (_hoverIndex == index) return;
            _hoverIndex = index;
            _hoverTimer.Start();
        }

        private void SetHoverHint(int value)
        {
            if (_hoverHint == value) return;
            _hoverHint = value;
            Cursor = value > 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        private Color InteractiveTextColor(int target, Color normal)
        {
            return _hoverHint == target ? _palette.Primary : normal;
        }

        private void HoverTimerOnTick(object sender, EventArgs e)
        {
            var settled = true;
            for (var index = 0; index < _hoverWeights.Length; index++)
            {
                var distance = _hoverIndex < 0 ? int.MaxValue : Math.Abs(index - _hoverIndex);
                var target = distance == 0 ? 1d : (distance == 1 ? 0.52d : (distance == 2 ? 0.18d : 0d));
                var next = _hoverWeights[index] + (target - _hoverWeights[index]) * 0.24d;
                if (Math.Abs(next - target) < 0.008d) next = target;
                else settled = false;
                _hoverWeights[index] = next;
            }

            Invalidate();
            if (settled) _hoverTimer.Stop();
        }

        private void SliderSnapTimerOnTick(object sender, EventArgs e)
        {
            _snapPulse *= 0.78d;
            if (_snapPulse < 0.015d)
            {
                _snapPulse = 0d;
                _sliderSnapTimer.Stop();
            }
            Invalidate();
        }

        private void EnsureHoverWeights(int count)
        {
            if (_hoverWeights.Length == count) return;
            _hoverWeights = new double[count];
            _hoverIndex = -1;
            _hoverTimer.Stop();
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                (int)Math.Round(from.A + (to.A - from.A) * amount),
                (int)Math.Round(from.R + (to.R - from.R) * amount),
                (int)Math.Round(from.G + (to.G - from.G) * amount),
                (int)Math.Round(from.B + (to.B - from.B) * amount));
        }

        private static Color ContrastingText(Color background)
        {
            var brightness = (background.R * 299 + background.G * 587 + background.B * 114) / 1000;
            return brightness >= 155 ? Color.FromArgb(18, 38, 44) : Color.White;
        }

        private string GetPlanLabel()
        {
            if (_result == null || _result.Snapshot == null || string.IsNullOrWhiteSpace(_result.Snapshot.PlanType)) return null;
            var value = _result.Snapshot.PlanType.Trim();
            if (value.Equals("plus", StringComparison.OrdinalIgnoreCase)) return "ChatGPT Plus";
            if (value.Equals("pro", StringComparison.OrdinalIgnoreCase)) return "ChatGPT Pro";
            if (value.Equals("team", StringComparison.OrdinalIgnoreCase)) return "ChatGPT Team";
            if (value.Equals("enterprise", StringComparison.OrdinalIgnoreCase)) return "ChatGPT Enterprise";
            return "ChatGPT " + value;
        }

        private string GetWindowName(QuotaWindow window)
        {
            if (!_settings.IsEnglish) return window.Name;
            if (!string.IsNullOrWhiteSpace(window.Key) && window.Key.StartsWith("gpt-reserve:", StringComparison.OrdinalIgnoreCase))
                return "gpt-reserve · Reserve quota";
            if (window.Kind == QuotaWindowKind.FiveHour || window.WindowMinutes == 300) return "5-hour quota";
            if (window.Kind == QuotaWindowKind.Weekly || window.WindowMinutes == 10080) return "Weekly quota";
            if (window.WindowMinutes >= 10080) return "Weekly quota";
            if (window.WindowMinutes >= 1440) return "Long-term quota";
            if (window.WindowMinutes > 0) return "Short-term quota";
            return "Quota";
        }

        private QuotaWindow GetMainWindow()
        {
            if (_result == null || _result.Snapshot == null || _result.Snapshot.Windows.Count == 0) return null;
            return _result.Snapshot.FiveHourWindow;
        }

        private void DrawProgress(Graphics graphics, Rectangle bounds, int percent, Color color)
        {
            using (var track = new SolidBrush(_palette.Track))
            using (var fill = new SolidBrush(color))
            {
                graphics.FillRoundedRectangle(track, bounds, 5);
                var width = (int)Math.Round(bounds.Width * Math.Max(0, Math.Min(100, percent)) / 100d);
                if (width > 0) graphics.FillRoundedRectangle(fill, new Rectangle(bounds.X, bounds.Y, Math.Max(bounds.Height, width), bounds.Height), bounds.Height / 2);
            }
        }

        private void DrawQuotaIcon(Graphics graphics, Point center)
        {
            using (var pen = new Pen(_palette.Muted, 1.4f))
            {
                graphics.DrawRoundedRectangle(pen, new Rectangle(center.X - 9, center.Y - 6, 17, 11), 2);
                graphics.DrawLine(pen, center.X + 9, center.Y - 2, center.X + 9, center.Y + 2);
            }
        }

        private void DrawClockIcon(Graphics graphics, Point center)
        {
            using (var pen = new Pen(_palette.Muted, 1.5f))
            {
                graphics.DrawEllipse(pen, center.X - 8, center.Y - 8, 16, 16);
                graphics.DrawLine(pen, center.X, center.Y, center.X, center.Y - 5);
                graphics.DrawLine(pen, center.X, center.Y, center.X - 4, center.Y + 2);
            }
        }

        private void DrawGearIcon(Graphics graphics, Point center)
        {
            using (var pen = new Pen(_palette.Muted, 1.6f))
            {
                graphics.DrawEllipse(pen, center.X - 8, center.Y - 8, 16, 16);
                graphics.DrawEllipse(pen, center.X - 3, center.Y - 3, 6, 6);
                for (var index = 0; index < 8; index++)
                {
                    var angle = Math.PI * index / 4d;
                    graphics.DrawLine(pen,
                        center.X + (float)Math.Cos(angle) * 9, center.Y + (float)Math.Sin(angle) * 9,
                        center.X + (float)Math.Cos(angle) * 12, center.Y + (float)Math.Sin(angle) * 12);
                }
            }
        }

        private void DrawDivider(Graphics graphics, int y)
        {
            using (var pen = new Pen(_palette.Divider)) graphics.DrawLine(pen, 24, y, 396, y);
        }

        private void DrawFooter(Graphics graphics)
        {
            var updated = _result != null && _result.Snapshot != null
                ? T("更新 ", "Updated ") + _result.Snapshot.CapturedAt.ToLocalTime().ToString("HH:mm")
                : T("等待更新", "Waiting");
            DrawText(graphics, updated, 8.5f, new RectangleF(24, 549, 180, 30), _palette.Faint, FontStyle.Regular);
            DrawText(graphics, T("退出", "Quit"), 10, _exitBounds, InteractiveTextColor(HoverExit, _palette.Text), FontStyle.Regular, StringAlignment.Far);
        }

        private string FormatCountdown(TimeSpan remaining)
        {
            if (remaining < TimeSpan.Zero) return T("即将更新", "soon");
            if (_settings.IsEnglish)
            {
                if (remaining.TotalDays >= 1) return ((int)remaining.TotalDays) + "d " + remaining.Hours + "h";
                if (remaining.TotalHours >= 1) return ((int)remaining.TotalHours) + "h " + remaining.Minutes + "m";
                return Math.Max(1, remaining.Minutes) + "m";
            }
            if (remaining.TotalDays >= 1) return ((int)remaining.TotalDays) + "天 " + remaining.Hours + "小时";
            if (remaining.TotalHours >= 1) return ((int)remaining.TotalHours) + "小时 " + remaining.Minutes + "分钟";
            return Math.Max(1, remaining.Minutes) + "分钟";
        }

        private static string FormatTokenCount(long tokens)
        {
            if (tokens >= 1000000000L) return (tokens / 1000000000d).ToString("0.#") + "B";
            if (tokens >= 1000000L) return (tokens / 1000000d).ToString("0.#") + "M";
            if (tokens >= 1000L) return (tokens / 1000d).ToString("0.#") + "K";
            return tokens.ToString();
        }

        private static string FormatPercent(double value)
        {
            return value.ToString(value >= 100 ? "0.#" : "0.#") + "%";
        }

        private string T(string chinese, string english) { return _settings.IsEnglish ? english : chinese; }

        private void Raise(EventHandler handler) { if (handler != null) handler(this, EventArgs.Empty); }

        private void DrawText(Graphics graphics, string text, float size, RectangleF bounds, Color color, FontStyle style, StringAlignment alignment)
        {
            var fontScale = _settings.FontScalePercent / 100f;
            if (_glassActive)
            {
                _layeredTextItems.Add(new DirectWriteTextRenderer.TextItem
                {
                    Text = text,
                    Size = size * fontScale,
                    Bounds = bounds,
                    Color = color,
                    Style = style,
                    Alignment = alignment
                });
                return;
            }
            using (var font = new Font("Microsoft YaHei UI", size * 96f / 72f * fontScale, style, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat
            {
                Alignment = alignment,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            }) graphics.DrawString(text, font, brush, bounds, format);
        }

        private void DrawText(Graphics graphics, string text, float size, RectangleF bounds, Color color, FontStyle style)
        {
            DrawText(graphics, text, size, bounds, color, style, StringAlignment.Near);
        }

        private float GetDrawingScale()
        {
            return _dpiScale * _settings.PopupScalePercent / 100f;
        }

        private Size GetScaledSize()
        {
            return GraphicsExtensions.ScaleSize(new Size(BaseWidth, BaseHeight), GetDrawingScale());
        }

        private void ApplyScaledSize()
        {
            if (!IsHandleCreated) return;
            Size = GetScaledSize();
        }

    }

    internal static class GraphicsExtensions
    {
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);

        public static float GetDpiScale(IntPtr window)
        {
            try
            {
                var dpi = GetDpiForWindow(window);
                return dpi <= 0 ? 1f : dpi / 96f;
            }
            catch (EntryPointNotFoundException) { return 1f; }
        }

        public static Size ScaleSize(Size size, float scale)
        {
            return new Size((int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale));
        }

        public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
        {
            using (var path = CreateRoundedPath(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), radius)) graphics.FillPath(brush, path);
        }

        public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle bounds, int radius)
        {
            using (var path = CreateRoundedPath(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), radius)) graphics.DrawPath(pen, path);
        }

        public static GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
        {
            radius = Math.Max(0f, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f));
            if (radius <= 0f)
            {
                var rectanglePath = new GraphicsPath();
                rectanglePath.AddRectangle(bounds);
                return rectanglePath;
            }
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
