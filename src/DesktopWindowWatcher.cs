using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CodexQuotaTray
{
    // Out-of-context hooks are delivered on the installing UI thread. Keep callbacks
    // short: notify the context; UIA reads run separately after coalescing events.
    internal sealed class DesktopWindowWatcher : IDisposable
    {
        private delegate void WinEventCallback(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time);
        private readonly WinEventCallback _callback;
        private readonly List<IntPtr> _hooks = new List<IntPtr>();
        private readonly HashSet<IntPtr> _visibleWindows = new HashSet<IntPtr>();
        private bool _disposed;
        public event Action<bool> Changed;

        public DesktopWindowWatcher()
        {
            _callback = OnEvent;
            EnumWindows(delegate(IntPtr window, IntPtr state)
            {
                if (IsWindowVisible(window) && (GetWindowLong(window, -20) & 0x80) == 0) _visibleWindows.Add(window);
                return true;
            }, IntPtr.Zero);
            Add(0x0003, 0x0003); // EVENT_SYSTEM_FOREGROUND
            Add(0x8000, 0x8003); // create/destroy/show/hide
            Add(0x800A, 0x800B); // state/location change
        }

        private void Add(uint first, uint last)
        {
            var hook = SetWinEventHook(first, last, IntPtr.Zero, _callback, 0, 0, 2); // skip this process
            if (hook != IntPtr.Zero) _hooks.Add(hook);
        }

        private void OnEvent(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time)
        {
            if (_disposed || window == IntPtr.Zero) return;
            try
            {
                var foreground = GetForegroundWindow();
                uint shellProcess, eventProcess;
                GetWindowThreadProcessId(FindWindow("Shell_TrayWnd", null), out shellProcess);
                GetWindowThreadProcessId(window, out eventProcess);
                var shellEvent = shellProcess != 0 && shellProcess == eventProcess;
                var lifecycle = false;
                if (kind >= 0x8000 && kind <= 0x8003 && objectId == 0)
                {
                    if (kind == 0x8001 || kind == 0x8003) lifecycle = _visibleWindows.Remove(window);
                    else if (GetAncestor(window, 2) == window && IsWindowVisible(window) && (GetWindowLong(window, -20) & 0x80) == 0)
                        lifecycle = _visibleWindows.Add(window);
                }
                var foregroundEvent = kind == 3 || window == foreground;
                if (!foregroundEvent && !shellEvent && !lifecycle) return;
                var handler = Changed;
                if (handler != null) handler(kind == 3 || shellEvent || lifecycle);
            }
            catch { } // A disappearing desktop window must not terminate the app.
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var hook in _hooks) UnhookWinEvent(hook);
            _hooks.Clear();
            _visibleWindows.Clear();
            Changed = null;
            GC.KeepAlive(_callback);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventCallback callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        private delegate bool EnumWindowCallback(IntPtr window, IntPtr state);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr state);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", ExactSpelling = true)]
        private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    }
}
