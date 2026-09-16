using System;
using System.Drawing;
using System.Windows.Automation;

namespace CodexQuotaTray
{
    internal static class TrayIconAnchorResolver
    {
        public static Rectangle Resolve(Point pointer)
        {
            try
            {
                var element = AutomationElement.FromPoint(new System.Windows.Point(pointer.X, pointer.Y));
                Rectangle? best = null;

                for (var depth = 0; element != null && depth < 6; depth++)
                {
                    var current = element.Current;
                    var bounds = current.BoundingRectangle;
                    if (!bounds.IsEmpty && bounds.Width >= 8 && bounds.Height >= 8 &&
                        bounds.Width <= 160 && bounds.Height <= 160)
                    {
                        var candidate = Rectangle.Round(new RectangleF(
                            (float)bounds.X,
                            (float)bounds.Y,
                            (float)bounds.Width,
                            (float)bounds.Height));

                        if (candidate.Contains(pointer) &&
                            (best == null || candidate.Width * candidate.Height < best.Value.Width * best.Value.Height))
                        {
                            best = candidate;
                        }

                        if (candidate.Contains(pointer) && current.ControlType == ControlType.Button)
                        {
                            return candidate;
                        }
                    }

                    element = TreeWalker.ControlViewWalker.GetParent(element);
                }

                if (best.HasValue)
                {
                    return best.Value;
                }
            }
            catch (ElementNotAvailableException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            return new Rectangle(pointer.X - 1, pointer.Y - 1, 3, 3);
        }
    }
}
