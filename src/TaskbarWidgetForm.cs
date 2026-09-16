using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Automation;

namespace CodexQuotaTray
{
    internal sealed class TaskbarWidgetForm : Form
    {
        private const int SingleBaseWidth = 97;
        private const int DualBaseWidth = 122;
        private const int BaseHeight = 38;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr window, int index, int value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        private int _fiveHourRemainingPercent;
        private int _fiveHourTimeRemainingPercent = -1;
        private int _weeklyRemainingPercent;
        private DateTimeOffset? _fiveHourResetsAt;
        private bool _hasWeeklyWindow;
        private bool _active;
        private float _dpiScale = 1f;
        private AppSettings _settings = new AppSettings();
        private ThemePalette _palette;
        private IntPtr _taskbarHandle;
        private bool _hovered;
        private NativeRect _refreshedTaskButtonBounds;
        private NativeRect _refreshedNotificationBounds;
        private IntPtr _refreshedTaskbarHandle;
        private bool _hasRefreshedTaskButtonBounds;
        private bool _hasRefreshedNotificationBounds;
        private int _dynamicAnchorRefreshRunning;

        public TaskbarWidgetForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            _palette = ThemePalette.FromSettings(_settings);
            ClientSize = new Size(SingleBaseWidth, BaseHeight);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = false;
            DoubleBuffered = true;
            BackColor = _palette.Background;
            Cursor = Cursors.Hand;
            SetRoundedRegion();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WsExToolWindow = 0x00000080;
                const int WsExNoActivate = 0x08000000;
                var parameters = base.CreateParams;
                parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
                return parameters;
            }
        }

        public void ApplyResult(QuotaReadResult result)
        {
            _active = result != null && result.IsCodexRunning && result.Snapshot != null;
            var primary = result == null || result.Snapshot == null ? null : result.Snapshot.FiveHourWindow;
            var weekly = result == null || result.Snapshot == null ? null : result.Snapshot.DisplayWeeklyWindow;
            _fiveHourRemainingPercent = primary == null ? 0 : primary.RemainingPercent;
            _fiveHourTimeRemainingPercent = primary == null ? -1 : primary.TimeRemainingPercent;
            _weeklyRemainingPercent = weekly == null ? 0 : weekly.RemainingPercent;
            _fiveHourResetsAt = primary == null ? null : primary.ResetsAt;
            _hasWeeklyWindow = weekly != null && weekly != primary;
            ApplyScaledSize();
            Reposition();
            Invalidate();
        }

        public void ApplySettings(AppSettings settings)
        {
            _settings = settings ?? new AppSettings();
            _palette = ThemePalette.FromSettings(_settings);
            BackColor = _palette.Background;
            ApplyScaledSize();
            Reposition();
            Invalidate();
        }

        public void SetVisible(bool visible)
        {
            if (visible)
            {
                EnsureTaskbarAttachment();
                if (!Visible) Show();
                EnsureOnTop();
            }
            else
            {
                Hide();
            }
        }

        public void EnsureOnTop()
        {
            if (!EnsureTaskbarAttachment()) return;
            if (!Visible) Show();
            Reposition(true);
        }

        public void RefreshDynamicAnchor()
        {
            if (Interlocked.Exchange(ref _dynamicAnchorRefreshRunning, 1) != 0) return;
            var thread = new Thread(new ThreadStart(delegate
            {
                try
                {
                    var taskbar = FindWindow("Shell_TrayWnd", null);
                    NativeRect taskbarBounds;
                    if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out taskbarBounds)) return;
                    var horizontal = taskbarBounds.Right - taskbarBounds.Left >= taskbarBounds.Bottom - taskbarBounds.Top;
                    NativeRect refreshedTaskButtons;
                    NativeRect refreshedNotifications;
                    var hasTaskButtons = TryGetTaskButtonBounds(taskbar, taskbarBounds, horizontal, out refreshedTaskButtons);
                    var hasNotifications = TryGetVisibleNotificationBounds(taskbar, taskbarBounds, horizontal, out refreshedNotifications);
                    if (!hasTaskButtons && !hasNotifications) return;
                    if (IsDisposed || !IsHandleCreated) return;
                    BeginInvoke(new Action(delegate
                    {
                        _refreshedTaskbarHandle = taskbar;
                        if (hasTaskButtons)
                        {
                            _refreshedTaskButtonBounds = refreshedTaskButtons;
                            _hasRefreshedTaskButtonBounds = true;
                        }
                        if (hasNotifications)
                        {
                            _refreshedNotificationBounds = refreshedNotifications;
                            _hasRefreshedNotificationBounds = true;
                        }
                        Reposition(true);
                    }));
                }
                catch (InvalidOperationException) { }
                finally { Interlocked.Exchange(ref _dynamicAnchorRefreshRunning, 0); }
            }));
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        public Rectangle GetAnchorBounds()
        {
            NativeRect bounds;
            if (IsHandleCreated && GetWindowRect(Handle, out bounds))
            {
                return Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            }

            return new Rectangle(Left, Top, Width, Height);
        }

        public void Reposition(bool forceZOrder = false)
        {
            var taskbar = FindWindow("Shell_TrayWnd", null);
            NativeRect taskbarBounds;
            if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out taskbarBounds))
            {
                return;
            }

            if (!EnsureTaskbarAttachment()) return;

            var taskbarWidth = taskbarBounds.Right - taskbarBounds.Left;
            var taskbarHeight = taskbarBounds.Bottom - taskbarBounds.Top;
            var tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            var hasTray = _hasRefreshedNotificationBounds && _refreshedTaskbarHandle == taskbar &&
                IntersectsTaskbar(_refreshedNotificationBounds, taskbarBounds);
            var trayBounds = hasTray ? _refreshedNotificationBounds : new NativeRect();
            if (!hasTray) hasTray = tray != IntPtr.Zero && GetWindowRect(tray, out trayBounds);
            NativeRect visibleNotificationBounds;
            if (!hasTray &&
                TryGetVisibleNotificationBounds(taskbar, taskbarBounds, taskbarWidth >= taskbarHeight, out visibleNotificationBounds))
            {
                trayBounds = visibleNotificationBounds;
                hasTray = true;
            }
            NativeRect taskButtonBounds;
            var hasTaskButtons = _hasRefreshedTaskButtonBounds && _refreshedTaskbarHandle == taskbar &&
                IntersectsTaskbar(_refreshedTaskButtonBounds, taskbarBounds);
            taskButtonBounds = hasTaskButtons ? _refreshedTaskButtonBounds : new NativeRect();
            if (!hasTaskButtons)
                hasTaskButtons = TryGetTaskButtonBounds(taskbar, taskbarBounds, taskbarWidth >= taskbarHeight, out taskButtonBounds);

            int x;
            int y;
            if (taskbarWidth >= taskbarHeight)
            {
                var taskButtonX = hasTaskButtons ? taskButtonBounds.Left - Width - 8 : int.MinValue;
                x = taskButtonX >= taskbarBounds.Left + 4
                    ? CalculateLeadingPlacement(taskbarBounds.Left, taskbarBounds.Right, Width, taskButtonBounds.Left, 8)
                    : (hasTray ? trayBounds.Left - Width - 8 : taskbarBounds.Right - Width - 190);
                y = taskbarBounds.Top + Math.Max(0, (taskbarHeight - Height) / 2) + Math.Max(1, (int)Math.Round(_dpiScale));
                y = Math.Min(y, taskbarBounds.Bottom - Height - 1);
                x = Math.Max(taskbarBounds.Left + 4, Math.Min(x, taskbarBounds.Right - Width - 4));
            }
            else
            {
                x = taskbarBounds.Left + Math.Max(0, (taskbarWidth - Width) / 2);
                var taskButtonY = hasTaskButtons ? taskButtonBounds.Top - Height - 8 : int.MinValue;
                y = taskButtonY >= taskbarBounds.Top + 4
                    ? CalculateLeadingPlacement(taskbarBounds.Top, taskbarBounds.Bottom, Height, taskButtonBounds.Top, 8)
                    : (hasTray ? trayBounds.Top - Height - 8 : taskbarBounds.Bottom - Height - 190);
                y = Math.Max(taskbarBounds.Top + 4, Math.Min(y, taskbarBounds.Bottom - Height - 4));
            }

            NativeRect currentBounds;
            const int extendedStyle = -20;
            const int topMostStyle = 0x00000008;
            var correctlyPlaced = GetWindowRect(Handle, out currentBounds) &&
                currentBounds.Left == x && currentBounds.Top == y &&
                currentBounds.Right - currentBounds.Left == Width &&
                currentBounds.Bottom - currentBounds.Top == Height;
            var alreadyTopMost = (GetWindowLong(Handle, extendedStyle) & topMostStyle) != 0;
            if (correctlyPlaced && alreadyTopMost && !forceZOrder) return;

            const uint noActivate = 0x0010;
            const uint showWindow = 0x0040;
            var topMost = new IntPtr(-1);
            SetWindowPos(
                Handle,
                topMost,
                x,
                y,
                Width,
                Height,
                noActivate | showWindow);
        }

        internal static int CalculateLeadingPlacement(int taskbarStart, int taskbarEnd, int widgetExtent, int leadingEdge, int gap)
        {
            return Math.Max(taskbarStart + 4, Math.Min(leadingEdge - widgetExtent - gap, taskbarEnd - widgetExtent - 4));
        }

        private static bool TryGetTaskButtonBounds(
            IntPtr taskbar,
            NativeRect taskbarBounds,
            bool horizontal,
            out NativeRect result)
        {
            result = new NativeRect();
            try
            {
                var root = FindTaskbarAutomationRoot(taskbar);
                if (root == null) return false;

                var buttons = root.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                var found = false;
                var leadingEdge = int.MaxValue;
                for (var index = 0; index < buttons.Count; index++)
                {
                    var current = buttons[index].Current;
                    if (current.IsOffscreen || !IsTaskButton(current.AutomationId)) continue;
                    var bounds = current.BoundingRectangle;
                    if (bounds.IsEmpty || bounds.Width < 4 || bounds.Height < 4 || bounds.Width > 240 || bounds.Height > 240) continue;
                    var candidate = ToNativeRect(bounds);
                    if (!IntersectsTaskbar(candidate, taskbarBounds)) continue;

                    var edge = horizontal ? candidate.Left : candidate.Top;
                    if (edge >= leadingEdge) continue;
                    leadingEdge = edge;
                    result = candidate;
                    found = true;
                }
                return found;
            }
            catch (ElementNotAvailableException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (COMException) { return false; }
        }

        private static bool TryGetVisibleNotificationBounds(
            IntPtr taskbar,
            NativeRect taskbarBounds,
            bool horizontal,
            out NativeRect result)
        {
            result = new NativeRect();
            try
            {
                var root = FindTaskbarAutomationRoot(taskbar);
                if (root == null) return false;

                var buttons = root.FindAll(
                    TreeScope.Descendants,
                    new OrCondition(
                        new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon"),
                        new PropertyCondition(AutomationElement.AutomationIdProperty, "NotifyItemIcon")));
                var found = false;
                var leadingEdge = int.MaxValue;
                for (var index = 0; index < buttons.Count; index++)
                {
                    var current = buttons[index].Current;
                    if (current.IsOffscreen) continue;
                    var bounds = current.BoundingRectangle;
                    if (bounds.IsEmpty || bounds.Width < 4 || bounds.Height < 4 || bounds.Width > 200 || bounds.Height > 200) continue;
                    var candidate = ToNativeRect(bounds);
                    if (!IntersectsTaskbar(candidate, taskbarBounds)) continue;

                    var edge = horizontal ? candidate.Left : candidate.Top;
                    if (edge >= leadingEdge) continue;
                    leadingEdge = edge;
                    result = candidate;
                    found = true;
                }
                return found;
            }
            catch (ElementNotAvailableException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (COMException) { return false; }
        }

        private static AutomationElement FindTaskbarAutomationRoot(IntPtr taskbar)
        {
            var root = AutomationElement.RootElement.FindFirst(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd"));
            return root ?? AutomationElement.FromHandle(taskbar);
        }

        private static bool IsTaskButton(string automationId)
        {
            return string.Equals(automationId, "StartButton", StringComparison.Ordinal) ||
                string.Equals(automationId, "SearchButton", StringComparison.Ordinal) ||
                string.Equals(automationId, "TaskViewButton", StringComparison.Ordinal) ||
                string.Equals(automationId, "WidgetsButton", StringComparison.Ordinal) ||
                (!string.IsNullOrEmpty(automationId) && automationId.StartsWith("Appid:", StringComparison.Ordinal));
        }

        private static NativeRect ToNativeRect(System.Windows.Rect bounds)
        {
            return new NativeRect
            {
                Left = (int)Math.Round(bounds.Left),
                Top = (int)Math.Round(bounds.Top),
                Right = (int)Math.Round(bounds.Right),
                Bottom = (int)Math.Round(bounds.Bottom)
            };
        }

        private static bool IntersectsTaskbar(NativeRect candidate, NativeRect taskbar)
        {
            return candidate.Right > taskbar.Left && candidate.Left < taskbar.Right &&
                candidate.Bottom > taskbar.Top && candidate.Top < taskbar.Bottom;
        }

        private bool EnsureTaskbarAttachment()
        {
            const int ownerIndex = -8;
            const uint ownerWindow = 4;

            var taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return false;

            var handle = Handle;
            if (GetWindow(handle, ownerWindow) != taskbar)
            {
                SetWindowLongPtr(handle, ownerIndex, taskbar);
                _taskbarHandle = taskbar;
                _dpiScale = GraphicsExtensions.GetDpiScale(handle);
                ApplyScaledSize();
            }
            else _taskbarHandle = taskbar;

            return GetWindow(handle, ownerWindow) == _taskbarHandle;
        }

        private static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value)
        {
            if (IntPtr.Size == 8) return SetWindowLongPtr64(window, index, value);
            return new IntPtr(SetWindowLong(window, index, value.ToInt32()));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            SetRoundedRegion();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _dpiScale = GraphicsExtensions.GetDpiScale(Handle);
            ApplyScaledSize();
            if (_settings.WidgetVisible)
                BeginInvoke(new Action(delegate { EnsureOnTop(); }));
        }

        protected override void WndProc(ref Message message)
        {
            const int DpiChanged = 0x02E0;
            if (message.Msg == DpiChanged)
            {
                var newDpi = message.WParam.ToInt64() & 0xFFFF;
                base.WndProc(ref message);
                _dpiScale = Math.Max(1f, newDpi / 96f);
                ApplyScaledSize();
                BeginInvoke(new Action(delegate { Reposition(); }));
                Invalidate();
                return;
            }
            base.WndProc(ref message);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var scale = GetDrawingScale();
            graphics.ScaleTransform(scale, scale);

            var accent = _active ? _palette.QuotaColor(_fiveHourRemainingPercent) : _palette.Faint;
            var weeklyAccent = _active ? _palette.QuotaColor(_weeklyRemainingPercent) : _palette.Faint;
            var baseWidth = GetBaseWidth();
            var backgroundColor = _hovered ? Blend(_palette.Background, _palette.Primary, _palette.IsLight ? 0.06f : 0.09f) : _palette.Background;
            var borderColor = _hovered ? Blend(_palette.Divider, _palette.Primary, 0.45f) : _palette.Divider;
            using (var background = new SolidBrush(backgroundColor))
            using (var border = new Pen(borderColor))
            {
                graphics.FillRoundedRectangle(background, new Rectangle(0, 0, baseWidth - 1, BaseHeight - 1), 8);
                graphics.DrawRoundedRectangle(border, new Rectangle(0, 0, baseWidth - 1, BaseHeight - 1), 8);
            }

            using (var trackPen = new Pen(_palette.Track, 3f))
            using (var fillPen = new Pen(accent, 3f))
            {
                trackPen.StartCap = trackPen.EndCap = LineCap.Round;
                fillPen.StartCap = fillPen.EndCap = LineCap.Round;
                var ring = new RectangleF(9, 8, 25, 25);
                graphics.DrawArc(trackPen, ring, -90, 359.9f);
                if (_active && _fiveHourRemainingPercent > 0)
                {
                    graphics.DrawArc(fillPen, ring, -90, 360f * _fiveHourRemainingPercent / 100f);
                }
            }

            if (_active && _fiveHourTimeRemainingPercent >= 0)
            {
                using (var trackPen = new Pen(_palette.Faint, 1.6f))
                using (var fillPen = new Pen(_palette.Secondary, 1.6f))
                {
                    trackPen.StartCap = trackPen.EndCap = LineCap.Round;
                    fillPen.StartCap = fillPen.EndCap = LineCap.Round;
                    var ring = new RectangleF(13, 12, 17, 17);
                    graphics.DrawArc(trackPen, ring, -90, 359.9f);
                    if (_fiveHourTimeRemainingPercent > 0)
                        graphics.DrawArc(fillPen, ring, -90, 360f * _fiveHourTimeRemainingPercent / 100f);
                }
            }

            if (_hasWeeklyWindow)
            {
                using (var trackPen = new Pen(_palette.Track, 2.2f))
                using (var fillPen = new Pen(weeklyAccent, 2.2f))
                {
                    trackPen.StartCap = trackPen.EndCap = LineCap.Round;
                    fillPen.StartCap = fillPen.EndCap = LineCap.Round;
                    var ring = new RectangleF(96, 10, 17, 17);
                    graphics.DrawArc(trackPen, ring, -90, 359.9f);
                    if (_active && _weeklyRemainingPercent > 0)
                        graphics.DrawArc(fillPen, ring, -90, 360f * _weeklyRemainingPercent / 100f);
                }
            }

            var value = _active ? _fiveHourRemainingPercent + "%" : "--%";
            var resetHours = _active ? FormatResetHours(_fiveHourResetsAt, DateTimeOffset.Now) : "--H";
            var fontScale = _settings.FontScalePercent / 100f;
            using (var valueFont = new Font("Segoe UI", 12.5f * 96f / 72f * fontScale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var labelFont = new Font("Segoe UI", 6.8f * 96f / 72f * fontScale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var weeklyFont = new Font("Segoe UI", 5.8f * 96f / 72f * fontScale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var valueBrush = new SolidBrush(_palette.Text))
            using (var labelBrush = new SolidBrush(_active ? accent : _palette.Muted))
            using (var weeklyBrush = new SolidBrush(_active ? weeklyAccent : _palette.Muted))
            {
                graphics.DrawString(value, valueFont, valueBrush, new RectangleF(42, 2, 52, 25));
                graphics.DrawString(_active ? resetHours : "OFFLINE", labelFont, labelBrush, new RectangleF(43, 23, 48, 14));
                if (_hasWeeklyWindow)
                {
                    using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        graphics.DrawString(_weeklyRemainingPercent.ToString(CultureInfo.InvariantCulture), weeklyFont, weeklyBrush, new RectangleF(95, 9, 20, 20), format);
                }
            }
        }

        internal static string FormatResetHours(DateTimeOffset? resetsAt, DateTimeOffset now)
        {
            if (!resetsAt.HasValue) return "--H";
            var hours = Math.Max(0d, (resetsAt.Value - now).TotalHours);
            return hours.ToString("0.#", CultureInfo.InvariantCulture) + "H";
        }

        private void SetRoundedRegion()
        {
            using (var path = GraphicsExtensions.CreateRoundedPath(
                new RectangleF(0, 0, Width, Height),
                Math.Max(8f, 8f * GetDrawingScale())))
            {
                Region = new Region(path);
            }
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

        private float GetDrawingScale()
        {
            return _dpiScale * _settings.WidgetScalePercent / 100f;
        }

        private Size GetScaledSize()
        {
            return GraphicsExtensions.ScaleSize(new Size(GetBaseWidth(), BaseHeight), GetDrawingScale());
        }

        private int GetBaseWidth()
        {
            return _hasWeeklyWindow ? DualBaseWidth : SingleBaseWidth;
        }

        private void ApplyScaledSize()
        {
            if (!IsHandleCreated) return;
            Size = GetScaledSize();
            SetRoundedRegion();
        }
    }
}
