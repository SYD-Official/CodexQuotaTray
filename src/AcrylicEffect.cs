using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CodexQuotaTray
{
    internal static class AcrylicEffect
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int State;
            public int Flags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int Size;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr window, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        internal static bool Apply(IntPtr window, bool enabled, int opacityPercent, Color tint)
        {
            if (window == IntPtr.Zero) return false;
            ConfigureRoundedCorners(window);

            var policy = new AccentPolicy
            {
                State = enabled ? 4 : 0,
                Flags = enabled ? 2 : 0,
                GradientColor = enabled ? ToAbgr(GetEffectiveOpacity(opacityPercent, tint), GetGlassTint(tint)) : 0,
                AnimationId = 0
            };
            var size = Marshal.SizeOf(typeof(AccentPolicy));
            var pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, pointer, false);
                var data = new WindowCompositionAttributeData { Attribute = 19, Data = pointer, Size = size };
                if (SetWindowCompositionAttribute(window, ref data) != 0) return true;
            }
            catch
            {
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }

            try
            {
                const int systemBackdropType = 38;
                var backdrop = enabled ? 3 : 1;
                return DwmSetWindowAttribute(window, systemBackdropType, ref backdrop, sizeof(int)) == 0;
            }
            catch
            {
                return false;
            }
        }

        internal static bool ConfigureRoundedCorners(IntPtr window)
        {
            if (window == IntPtr.Zero) return false;
            try
            {
                const int windowCornerPreference = 33;
                const int round = 2;
                var preference = round;
                return DwmSetWindowAttribute(window, windowCornerPreference, ref preference, sizeof(int)) == 0;
            }
            catch
            {
                return false;
            }
        }

        private static int GetEffectiveOpacity(int opacityPercent, Color tint)
        {
            return Math.Max(5, Math.Min(95, opacityPercent));
        }

        private static Color GetGlassTint(Color tint)
        {
            return tint.GetBrightness() >= 0.68f
                ? Color.FromArgb(226, 235, 241)
                : Color.FromArgb(7, 24, 35);
        }

        private static int ToAbgr(int opacityPercent, Color color)
        {
            var alpha = Math.Max(0, Math.Min(255, (int)Math.Round(opacityPercent * 255d / 100d)));
            return (alpha << 24) | (color.B << 16) | (color.G << 8) | color.R;
        }
    }
}
