using System;
using System.Drawing;

namespace CodexQuotaTray
{
    internal static class WidgetPlacement
    {
        public static double ClampRatio(double ratio)
        {
            return double.IsNaN(ratio) || double.IsInfinity(ratio) ? 0.5 : Math.Max(0, Math.Min(1, ratio));
        }

        public static int Coordinate(int start, int end, int extent, double ratio)
        {
            var minimum = start + 4;
            var maximum = Math.Max(minimum, end - extent - 4);
            return minimum + (int)Math.Round(ClampRatio(ratio) * (maximum - minimum));
        }

        public static double Ratio(int coordinate, int start, int end, int extent)
        {
            var minimum = start + 4;
            var span = end - extent - 4 - minimum;
            return span <= 0 ? 0 : ClampRatio((coordinate - minimum) / (double)span);
        }

        public static bool StartsDrag(Point origin, Point current, Size threshold)
        {
            return Math.Abs((long)current.X - origin.X) >= Math.Max(1, threshold.Width)
                || Math.Abs((long)current.Y - origin.Y) >= Math.Max(1, threshold.Height);
        }
    }
}
