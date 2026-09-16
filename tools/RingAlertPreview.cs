using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

internal static class RingAlertPreview
{
    private static readonly Color Background = Color.FromArgb(24, 27, 31);
    private static readonly Color Surface = Color.FromArgb(31, 36, 42);
    private static readonly Color Border = Color.FromArgb(57, 66, 75);
    private static readonly Color Track = Color.FromArgb(65, 74, 84);
    private static readonly Color Text = Color.FromArgb(242, 245, 248);
    private static readonly Color Muted = Color.FromArgb(165, 175, 186);
    private static readonly Color Cyan = Color.FromArgb(67, 194, 221);
    private static readonly Color Orange = Color.FromArgb(244, 164, 73);
    private static readonly Color Red = Color.FromArgb(255, 98, 92);
    private static readonly Color Secondary = Color.FromArgb(65, 174, 235);

    [STAThread]
    private static int Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : "RingAlertPreview.png";
        using (var bitmap = new Bitmap(1200, 560))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            graphics.Clear(Background);
            DrawText(graphics, "额度不足时的数字环颜色预览", 31, FontStyle.Bold, Text, new RectangleF(54, 30, 900, 55));
            DrawText(graphics, "平时保持当前主题色，仅在额度接近用完时切换颜色", 17, FontStyle.Regular, Muted, new RectangleF(56, 86, 900, 38));

            DrawState(graphics, new Rectangle(54, 145, 330, 300), "正常", 52, Cyan, "保持主题色");
            DrawState(graphics, new Rectangle(435, 145, 330, 300), "低额度提醒", 20, Orange, "建议：剩余 ≤ 20%");
            DrawState(graphics, new Rectangle(816, 145, 330, 300), "即将用尽", 10, Red, "建议：剩余 ≤ 10%");

            DrawText(graphics, "5 小时与每周额度环分别使用自己的告警色；大数字始终表示 5 小时额度。", 16,
                FontStyle.Regular, Muted, new RectangleF(54, 477, 1092, 42));
            bitmap.Save(output, ImageFormat.Png);
        }

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(output));
        SaveTaskbarState(Path.Combine(outputDirectory, "TaskbarQuotaLowPreview.png"), "低额度提醒", 20, Orange);
        SaveTaskbarState(Path.Combine(outputDirectory, "TaskbarQuotaCriticalPreview.png"), "即将用尽", 10, Red);
        return 0;
    }

    private static void SaveTaskbarState(string output, string title, int percent, Color accent)
    {
        using (var bitmap = new Bitmap(390, 190))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            graphics.Clear(Background);
            DrawText(graphics, title, 21, FontStyle.Bold, accent, new RectangleF(30, 18, 330, 38));
            DrawTaskbarWidget(graphics, new Rectangle(30, 72, 330, 92), percent, accent);
            bitmap.Save(output, ImageFormat.Png);
        }
    }

    private static void DrawState(Graphics graphics, Rectangle card, string title, int percent, Color accent, string caption)
    {
        using (var fill = new SolidBrush(Surface)) graphics.FillRoundedRectangle(fill, card, 22);
        using (var pen = new Pen(Color.FromArgb(105, accent), 1.5f)) graphics.DrawRoundedRectangle(pen, card, 22);
        DrawText(graphics, title, 21, FontStyle.Bold, accent, new RectangleF(card.X + 26, card.Y + 20, card.Width - 52, 38));
        DrawTaskbarWidget(graphics, new Rectangle(card.X + 28, card.Y + 79, 274, 92), percent, accent);
        DrawTrayRing(graphics, new Rectangle(card.X + 52, card.Y + 190, 68, 68), percent, accent);
        DrawText(graphics, "托盘单环", 14, FontStyle.Regular, Muted, new RectangleF(card.X + 135, card.Y + 196, 145, 28));
        DrawText(graphics, caption, 13, FontStyle.Regular, Muted, new RectangleF(card.X + 135, card.Y + 226, 165, 28));
    }

    private static void DrawTaskbarWidget(Graphics graphics, Rectangle bounds, int percent, Color accent)
    {
        using (var fill = new SolidBrush(Color.FromArgb(26, 31, 37))) graphics.FillRoundedRectangle(fill, bounds, 14);
        using (var pen = new Pen(Border, 1.5f)) graphics.DrawRoundedRectangle(pen, bounds, 14);
        var fiveHour = new RectangleF(bounds.X + 17, bounds.Y + 17, 57, 57);
        var weekly = new RectangleF(bounds.Right - 61, bounds.Y + 25, 41, 41);
        DrawArc(graphics, fiveHour, 7f, Track, accent, percent);
        DrawText(graphics, percent + "%", 25, FontStyle.Bold, Text, new RectangleF(bounds.X + 91, bounds.Y + 12, 150, 40));
        DrawText(graphics, "3.6H", 13, FontStyle.Bold, accent, new RectangleF(bounds.X + 94, bounds.Y + 50, 125, 25));
        DrawArc(graphics, weekly, 5f, Track, Secondary, 58);
        DrawText(graphics, "58", 12, FontStyle.Bold, Text, weekly, StringAlignment.Center);
    }

    private static void DrawTrayRing(Graphics graphics, Rectangle bounds, int percent, Color accent)
    {
        DrawArc(graphics, bounds, 8f, Color.FromArgb(76, 91, 108), accent, percent);
        DrawText(graphics, percent.ToString(), percent >= 10 ? 17 : 20, FontStyle.Bold, Text,
            new RectangleF(bounds.X + 3, bounds.Y + 4, bounds.Width - 6, bounds.Height - 5), StringAlignment.Center);
    }

    private static void DrawArc(Graphics graphics, RectangleF bounds, float width, Color track, Color accent, int percent)
    {
        using (var trackPen = new Pen(track, width))
        using (var accentPen = new Pen(accent, width))
        {
            trackPen.StartCap = trackPen.EndCap = LineCap.Round;
            accentPen.StartCap = accentPen.EndCap = LineCap.Round;
            graphics.DrawArc(trackPen, bounds, -90, 359.9f);
            graphics.DrawArc(accentPen, bounds, -90, 360f * percent / 100f);
        }
    }

    private static void DrawText(Graphics graphics, string value, float size, FontStyle style, Color color, RectangleF bounds)
    {
        DrawText(graphics, value, size, style, color, bounds, StringAlignment.Near);
    }

    private static void DrawText(Graphics graphics, string value, float size, FontStyle style, Color color, RectangleF bounds, StringAlignment alignment)
    {
        using (var font = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(color))
        using (var format = new StringFormat { Alignment = alignment, LineAlignment = StringAlignment.Center })
            graphics.DrawString(value, font, brush, bounds, format);
    }

    private static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
    {
        using (var path = RoundedPath(bounds, radius)) graphics.FillPath(brush, path);
    }

    private static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle bounds, int radius)
    {
        using (var path = RoundedPath(bounds, radius)) graphics.DrawPath(pen, path);
    }

    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
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
