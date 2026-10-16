using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexQuotaTray
{
    /// <summary>
    /// 前台窗口全屏检测 —— 仅用于决定任务栏额度小窗(Overlay)是否需要临时隐藏。
    /// 判定:前台窗口覆盖「悬浮窗所在显示器」的整屏矩形。
    /// 最大化状态的窗口还需移除普通标题栏和可调整边框:
    /// Chrome 视频全屏可能保留 IsZoomed/showCmd/WS_MAXIMIZE,不能单凭它们排除全屏。
    /// 普通带边框最大化窗口(包括任务栏自动隐藏时)保持数字条显示。
    /// 全程容错:任何异常/拿不准的情况都返回 false(宁可保持显示,绝不误隐藏)。
    /// </summary>
    internal static class FullscreenDetector
    {
        private const int MonitorDefaultToNearest = 2;
        private const int GwlStyle = -16;
        private const int SwShowMaximized = 3;
        private const long WsMaximize = 0x01000000L;
        private const long WsCaption = 0x00C00000L;
        private const long WsThickFrame = 0x00040000L;
        private const int CoversTolerance = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public bool IsEmpty { get { return Right <= Left || Bottom <= Top; } }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int cbSize;
            public NativeRect rcMonitor;
            public NativeRect rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPlacement
        {
            public int length;
            public int flags;
            public int showCmd;
            public PointStruct minPosition;
            public PointStruct maxPosition;
            public NativeRect normalPosition;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PointStruct
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll")]
        private static extern bool GetWindowPlacement(IntPtr window, ref WindowPlacement placement);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        /// <summary>
        /// 判断当前前台窗口是否为「覆盖悬浮窗所在显示器」的全屏窗口。
        /// 任何异常都返回 false(维持正常显示,不崩溃)。
        /// </summary>
        public static bool IsFullscreenOnWidgetMonitor(IntPtr widgetHandle)
        {
            try
            {
                return Detect(widgetHandle);
            }
            catch
            {
                return false;
            }
        }

        private static bool Detect(IntPtr widgetHandle)
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            if (IsOwnProcessWindow(foreground)) return false;
            if (IsShellWindow(foreground)) return false;
            if (!IsWindowVisible(foreground)) return false;

            NativeRect bounds;
            if (!GetWindowRect(foreground, out bounds) || bounds.IsEmpty) return false;

            // 悬浮窗所在显示器:优先用悬浮窗自身句柄,其次回退到主任务栏
            var anchor = widgetHandle != IntPtr.Zero ? widgetHandle : FindWindow("Shell_TrayWnd", null);
            if (anchor == IntPtr.Zero) return false;
            var monitor = MonitorFromWindow(anchor, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return false;
            var info = new MonitorInfo { cbSize = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetMonitorInfo(monitor, ref info)) return false;

            return ShouldHideOverlay(
                Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
                Rectangle.FromLTRB(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom),
                IsMaximizedState(foreground), GetWindowLongStyle(foreground));
        }

        internal static bool ShouldHideOverlay(Rectangle candidate, Rectangle target, bool isMaximized, long style)
        {
            return candidate.Width > 0 && candidate.Height > 0
                && target.Width > 0 && target.Height > 0
                && candidate.Left <= target.Left + CoversTolerance
                && candidate.Top <= target.Top + CoversTolerance
                && candidate.Right >= target.Right - CoversTolerance
                && candidate.Bottom >= target.Bottom - CoversTolerance
                && (!isMaximized || (style & (WsCaption | WsThickFrame)) == 0);
        }

        private static bool IsMaximizedState(IntPtr window)
        {
            if (IsZoomed(window)) return true;
            var placement = new WindowPlacement { length = Marshal.SizeOf(typeof(WindowPlacement)) };
            if (GetWindowPlacement(window, ref placement) && placement.showCmd == SwShowMaximized) return true;
            return (GetWindowLongStyle(window) & WsMaximize) != 0;
        }

        private static long GetWindowLongStyle(IntPtr window)
        {
            if (IntPtr.Size == 8) return GetWindowLongPtr64(window, GwlStyle).ToInt64();
            return GetWindowLong32(window, GwlStyle);
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

        private static bool IsOwnProcessWindow(IntPtr window)
        {
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            return processId == (uint)Process.GetCurrentProcess().Id;
        }

        private static bool IsShellWindow(IntPtr window)
        {
            var className = new StringBuilder(128);
            GetClassName(window, className, className.Capacity);
            var name = className.ToString();
            return string.Equals(name, "Progman", StringComparison.Ordinal)
                || string.Equals(name, "WorkerW", StringComparison.Ordinal)
                || string.Equals(name, "Shell_TrayWnd", StringComparison.Ordinal)
                || string.Equals(name, "Shell_SecondaryTrayWnd", StringComparison.Ordinal)
                || string.Equals(name, "MultitaskingViewFrame", StringComparison.Ordinal)
                || string.Equals(name, "XamlExplorerHostIslandWindow", StringComparison.Ordinal)
                || string.Equals(name, "ForegroundStaging", StringComparison.Ordinal);
        }
    }
}
