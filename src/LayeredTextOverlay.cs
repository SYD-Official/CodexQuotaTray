using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Media.Imaging;
using Media = System.Windows.Media;

namespace CodexQuotaTray
{
    internal static class DirectWriteTextRenderer
    {
        internal sealed class TextItem
        {
            public string Text;
            public float Size;
            public RectangleF Bounds;
            public Color Color;
            public FontStyle Style;
            public StringAlignment Alignment;
        }

        internal static Bitmap Render(int width, int height, IList<TextItem> items, float scale)
        {
            var visual = new Media.DrawingVisual();
            Media.TextOptions.SetTextRenderingMode(visual, Media.TextRenderingMode.Grayscale);
            Media.TextOptions.SetTextFormattingMode(visual, Media.TextFormattingMode.Display);
            Media.TextOptions.SetTextHintingMode(visual, Media.TextHintingMode.Fixed);
            using (var context = visual.RenderOpen())
            {
                foreach (var item in items)
                {
                    var weight = (item.Style & FontStyle.Bold) != 0 ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;
                    var style = (item.Style & FontStyle.Italic) != 0 ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal;
                    var typeface = new Media.Typeface(new Media.FontFamily("Microsoft YaHei UI"), style, weight, System.Windows.FontStretches.Normal);
                    var brush = new Media.SolidColorBrush(Media.Color.FromArgb(item.Color.A, item.Color.R, item.Color.G, item.Color.B));
                    var formatted = new Media.FormattedText(item.Text ?? string.Empty, CultureInfo.CurrentUICulture,
                        System.Windows.FlowDirection.LeftToRight, typeface, item.Size * 96f / 72f * scale, brush, 1d)
                    {
                        Trimming = System.Windows.TextTrimming.CharacterEllipsis,
                        MaxTextWidth = Math.Max(1d, item.Bounds.Width * scale),
                        MaxTextHeight = Math.Max(1d, item.Bounds.Height * scale),
                        TextAlignment = item.Alignment == StringAlignment.Center
                            ? System.Windows.TextAlignment.Center
                            : (item.Alignment == StringAlignment.Far ? System.Windows.TextAlignment.Right : System.Windows.TextAlignment.Left)
                    };
                    var x = Math.Round(item.Bounds.X * scale);
                    var y = Math.Round(item.Bounds.Y * scale + Math.Max(0d, (item.Bounds.Height * scale - formatted.Height) / 2d));
                    context.DrawText(formatted, new System.Windows.Point(x, y));
                }
            }

            var target = new RenderTargetBitmap(width, height, 96, 96, Media.PixelFormats.Pbgra32);
            target.Render(visual);
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { target.CopyPixels(System.Windows.Int32Rect.Empty, data.Scan0, data.Stride * height, data.Stride); }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
    }
}
