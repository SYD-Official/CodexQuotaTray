using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace CodexQuotaTray
{
    internal static class RingIconFactory
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Icon Create(int remainingPercent, bool active)
        {
            return Create(remainingPercent, active, ThemePalette.Presets()[0]);
        }

        public static Icon Create(int remainingPercent, bool active, ThemePalette palette)
        {
            return Create(remainingPercent, active, palette, false);
        }

        public static Icon Create(int remainingPercent, bool active, ThemePalette palette, bool completionAlert)
        {
            remainingPercent = Math.Max(0, Math.Min(100, remainingPercent));
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.Clear(Color.Transparent);

                var taskbarLight = ThemePalette.SystemUsesLightTaskbar();
                var track = active
                    ? (taskbarLight ? Color.FromArgb(142, 153, 165) : Color.FromArgb(76, 91, 108))
                    : Color.FromArgb(112, 118, 124);
                var accent = active ? palette.QuotaColor(remainingPercent) : Color.FromArgb(132, 136, 142);

                // The outer quota ring always uses the largest safe diameter in a 32px icon.
                const float outerDiameter = 31.5f;
                const float strokeWidth = 3.9f;
                using (var trackPen = new Pen(track, strokeWidth))
                using (var accentPen = new Pen(accent, strokeWidth))
                {
                    trackPen.StartCap = trackPen.EndCap = LineCap.Round;
                    accentPen.StartCap = accentPen.EndCap = LineCap.Round;
                    var diameter = outerDiameter - strokeWidth;
                    var ring = new RectangleF(16f - diameter / 2f, 16f - diameter / 2f, diameter, diameter);
                    graphics.DrawArc(trackPen, ring, -90, 359.9f);
                    if (active && remainingPercent > 0)
                    {
                        graphics.DrawArc(accentPen, ring, -90, 360f * remainingPercent / 100f);
                    }
                }

                var text = active ? remainingPercent.ToString() : "–";
                var fontSize = remainingPercent == 100 ? 9.2f : (remainingPercent >= 10 ? 12f : 14f);
                using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(active
                    ? (taskbarLight ? Color.FromArgb(25, 34, 43) : Color.White)
                    : Color.FromArgb(210, 214, 218)))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    // Pixel units keep the number independent of Windows display scaling.
                    graphics.DrawString(text, font, brush, new RectangleF(3f, 3f, 26f, 26f), format);
                }

                if (completionAlert)
                {
                    using (var glow = new SolidBrush(Color.FromArgb(80, palette.Secondary)))
                    using (var dot = new SolidBrush(palette.Secondary))
                    using (var border = new Pen(taskbarLight ? Color.White : Color.FromArgb(232, 244, 255), 1.2f))
                    {
                        graphics.FillEllipse(glow, 20f, 0f, 12f, 12f);
                        graphics.FillEllipse(dot, 22f, 2f, 8f, 8f);
                        graphics.DrawEllipse(border, 22f, 2f, 8f, 8f);
                    }
                }

                var handle = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        internal static Color PickColor(int remainingPercent)
        {
            return ThemePalette.Presets()[0].QuotaColor(remainingPercent);
        }
    }
}
