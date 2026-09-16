using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class IconGenerator
{
    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };

    private static int Main(string[] args)
    {
        if (args.Length < 1 || args.Length > 2) return 1;

        var frames = new List<byte[]>();
        foreach (var size in Sizes)
        {
            using (var bitmap = RenderIcon(size))
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                frames.Add(stream.ToArray());
            }
        }

        using (var stream = File.Create(args[0]))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)frames.Count);

            var offset = 6 + frames.Count * 16;
            for (var index = 0; index < frames.Count; index++)
            {
                var size = Sizes[index];
                writer.Write((byte)(size == 256 ? 0 : size));
                writer.Write((byte)(size == 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(frames[index].Length);
                writer.Write(offset);
                offset += frames[index].Length;
            }

            foreach (var frame in frames) writer.Write(frame);
        }

        if (args.Length == 2)
        {
            using (var preview = RenderIcon(256)) preview.Save(args[1], ImageFormat.Png);
        }
        return 0;
    }

    private static Bitmap RenderIcon(int size)
    {
        var renderSize = Math.Max(64, size * 4);
        using (var source = new Bitmap(renderSize, renderSize, PixelFormat.Format32bppArgb))
        {
            var scale = renderSize / 256f;
            using (var graphics = Graphics.FromImage(source))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;

                var card = Scale(new RectangleF(10, 10, 236, 236), scale);
                using (var path = RoundedPath(card, 50 * scale))
                using (var background = new LinearGradientBrush(
                    card,
                    Color.FromArgb(24, 38, 52),
                    Color.FromArgb(10, 17, 25),
                    55f))
                using (var border = new Pen(Color.FromArgb(78, 102, 121), Math.Max(2f, 4f * scale)))
                {
                    graphics.FillPath(background, path);
                    graphics.DrawPath(border, path);
                }

                var ring = Scale(new RectangleF(45, 45, 166, 166), scale);
                using (var track = new Pen(Color.FromArgb(58, 76, 91), Math.Max(2f, 18f * scale)))
                using (var cyan = new Pen(Color.FromArgb(49, 205, 228), Math.Max(2f, 18f * scale)))
                using (var blue = new Pen(Color.FromArgb(63, 159, 230), Math.Max(1.5f, 7f * scale)))
                {
                    track.StartCap = track.EndCap = LineCap.Round;
                    cyan.StartCap = cyan.EndCap = LineCap.Round;
                    blue.StartCap = blue.EndCap = LineCap.Round;
                    graphics.DrawArc(track, ring, -90, 359.9f);
                    graphics.DrawArc(cyan, ring, -90, 292f);
                    if (size >= 32)
                        graphics.DrawArc(blue, Scale(new RectangleF(67, 67, 122, 122), scale), 105f, 135f);
                }

                using (var letter = new Pen(Color.FromArgb(247, 250, 252), Math.Max(2f, 20f * scale)))
                {
                    letter.StartCap = letter.EndCap = LineCap.Round;
                    graphics.DrawArc(letter, Scale(new RectangleF(88, 83, 86, 90), scale), 43f, 274f);
                }
            }

            var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(result))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, size, size), 0, 0, renderSize, renderSize, GraphicsUnit.Pixel);
            }
            return result;
        }
    }

    private static RectangleF Scale(RectangleF value, float scale)
    {
        return new RectangleF(value.X * scale, value.Y * scale, value.Width * scale, value.Height * scale);
    }

    private static GraphicsPath RoundedPath(RectangleF bounds, float radius)
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
