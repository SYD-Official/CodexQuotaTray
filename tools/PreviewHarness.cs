using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using System.Windows.Automation;
using CodexQuotaTray;

internal static class PreviewHarness
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 19) return 1;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (args.Length == 1 && args[0] == "--taskbar-probe")
            return RunTaskbarProbe();

        if (args.Length == 0 || (args.Length == 1 && args[0].StartsWith("--live-", StringComparison.Ordinal)))
        {
            using (var interactive = new QuotaPopupForm())
            {
                var mode = args.Length == 1 ? args[0] : "--live-dark";
                var light = mode.IndexOf("light", StringComparison.Ordinal) >= 0;
                var settings = mode.IndexOf("settings", StringComparison.Ordinal) >= 0;
                var moreSettings = mode.IndexOf("more", StringComparison.Ordinal) >= 0;
                var equalQuotaLayout = mode.IndexOf("equal", StringComparison.Ordinal) >= 0;
                var glassEnabled = mode.IndexOf("off", StringComparison.Ordinal) < 0;
                var useTestBackdrop = mode.IndexOf("backdrop", StringComparison.Ordinal) >= 0;
                var stressRepaint = mode.IndexOf("repaint", StringComparison.Ordinal) >= 0;
                var glassOpacity = mode.IndexOf("low", StringComparison.Ordinal) >= 0 ? 5
                    : (mode.IndexOf("high", StringComparison.Ordinal) >= 0 ? 95 : 40);
                interactive.ApplySettings(new AppSettings
                {
                    AppearanceMode = light ? "light" : "dark",
                    Language = "zh",
                    GlassEnabled = glassEnabled,
                    GlassOpacityPercent = glassOpacity,
                    QuotaPrimaryEmphasis = !equalQuotaLayout,
                    ActivityRange = mode.IndexOf("5h", StringComparison.OrdinalIgnoreCase) >= 0 ? ActivityRange.Hours5 : ActivityRange.Days30
                });
                interactive.ApplyResult(new SessionQuotaReader().ReadLatest());
                if (moreSettings) interactive.ShowPreviewPage(2);
                else if (settings) interactive.ShowPreviewPage(1);
                if (mode.IndexOf("entryhover", StringComparison.Ordinal) >= 0)
                    interactive.SetPreviewHint(36);
                interactive.ExitRequested += delegate { interactive.Close(); };
                var work = Screen.PrimaryScreen.WorkingArea;
                interactive.Location = new Point(work.Left + (work.Width - interactive.Width) / 2, work.Top + (work.Height - interactive.Height) / 2);
                Form backdrop = null;
                Bitmap backdropImage = null;
                if (useTestBackdrop)
                {
                    backdrop = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = work, ShowInTaskbar = false };
                    backdropImage = CreateTestBackdrop(work.Size);
                    backdrop.BackgroundImage = backdropImage;
                    backdrop.Show();
                    Application.DoEvents();
                }
                var captureTimer = new Timer { Interval = 1200 };
                Timer repaintTimer = null;
                if (stressRepaint)
                {
                    repaintTimer = new Timer { Interval = 20 };
                    repaintTimer.Tick += delegate { interactive.Invalidate(); };
                    repaintTimer.Start();
                }
                captureTimer.Tick += delegate
                {
                    captureTimer.Stop();
                    using (var bitmap = new Bitmap(interactive.Width, interactive.Height))
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(interactive.Left, interactive.Top, 0, 0, interactive.Size);
                        var fileName = moreSettings
                            ? (light ? "AcrylicMoreLightPreview.png" : "AcrylicMoreDarkPreview.png")
                            : (settings
                            ? (glassEnabled
                                ? (light ? "AcrylicSettingsLightPreview.png" : "AcrylicSettingsDarkPreview.png")
                                : (light ? "AcrylicSettingsLightOffPreview.png" : "AcrylicSettingsDarkOffPreview.png"))
                            : (useTestBackdrop
                                ? (light ? "AcrylicBackdropLightPreview.png" : "AcrylicBackdropDarkPreview.png")
                                : (light ? "AcrylicLightPreview.png" : "AcrylicLivePreview.png")));
                        bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName), ImageFormat.Png);
                    }
                    interactive.Close();
                };
                captureTimer.Start();
                Application.Run(interactive);
                captureTimer.Dispose();
                if (repaintTimer != null) repaintTimer.Dispose();
                if (backdrop != null) backdrop.Dispose();
                if (backdropImage != null) backdropImage.Dispose();
            }
            return 0;
        }

        using (var form = new QuotaPopupForm())
        {
            var result = new SessionQuotaReader().ReadLatest();
            form.ApplyResult(result);
            form.Show();
            Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(args[0], ImageFormat.Png);
            }

            if (args.Length >= 3)
            {
                form.ShowPreviewPage(1);
                Application.DoEvents();
                using (var settingsBitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(settingsBitmap, new Rectangle(Point.Empty, form.Size));
                    settingsBitmap.Save(args[2], ImageFormat.Png);
                }
            }

            if (args.Length >= 4)
            {
                form.ShowPreviewPage(2);
                Application.DoEvents();
                using (var displayBitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(displayBitmap, new Rectangle(Point.Empty, form.Size));
                    displayBitmap.Save(args[3], ImageFormat.Png);
                }
            }

            if (args.Length >= 5)
            {
                var englishSettings = new AppSettings
                {
                    Language = "en",
                    ThemeId = "aurora",
                    ActivityRange = ActivityRange.Days7
                };
                form.ApplySettings(englishSettings);
                form.ShowPreviewPage(0);
                Application.DoEvents();
                using (var englishBitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(englishBitmap, new Rectangle(Point.Empty, form.Size));
                    englishBitmap.Save(args[4], ImageFormat.Png);
                }
            }

            if (args.Length >= 6)
            {
                form.ApplySettings(new AppSettings());
                form.SetPreviewHover(27);
                Application.DoEvents();
                using (var hoverBitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(hoverBitmap, new Rectangle(Point.Empty, form.Size));
                    hoverBitmap.Save(args[5], ImageFormat.Png);
                }
            }

            if (args.Length >= 7)
            {
                form.ApplySettings(new AppSettings { AppearanceMode = "light" });
                form.ShowPreviewPage(0);
                Application.DoEvents();
                using (var lightBitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(lightBitmap, new Rectangle(Point.Empty, form.Size));
                    lightBitmap.Save(args[6], ImageFormat.Png);
                }
            }

            if (args.Length >= 8)
            {
                using (var previewBitmap = new Bitmap(768, 288))
                using (var graphics = Graphics.FromImage(previewBitmap))
                {
                    graphics.Clear(Color.FromArgb(31, 34, 38));
                    graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    var values = new[] { 3, 36, 100 };
                    for (var index = 0; index < values.Length; index++)
                    {
                        using (var icon = RingIconFactory.Create(values[index], true, ThemePalette.Presets()[0]))
                        using (var iconBitmap = icon.ToBitmap())
                            graphics.DrawImage(iconBitmap, new Rectangle(index * 256, 0, 256, 256));
                        using (var font = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var brush = new SolidBrush(Color.White))
                            graphics.DrawString(values[index] + "%", font, brush, index * 256 + 104, 260);
                    }
                    previewBitmap.Save(args[7], ImageFormat.Png);
                }
            }

            if (args.Length >= 9)
            {
                form.ApplySettings(new AppSettings { AppearanceMode = "dark" });
                form.ShowPreviewPage(1);
                form.SetPreviewHint(3);
                SaveForm(form, args[8]);
            }

            if (args.Length >= 10)
            {
                form.ShowPreviewPage(2);
                form.SetPreviewHint(4);
                SaveForm(form, args[9]);
            }

            var themeIds = new[] { "aurora", "emerald", "sunset" };
            for (var index = 0; index < themeIds.Length && args.Length >= 11 + index; index++)
            {
                form.ApplySettings(new AppSettings { AppearanceMode = "dark", ThemeId = themeIds[index] });
                form.ShowPreviewPage(0);
                form.SetPreviewHint(0);
                SaveForm(form, args[10 + index]);
            }

            if (args.Length >= 14)
            {
                form.ApplySettings(new AppSettings { AppearanceMode = "dark", Language = "zh" });
                form.ShowPreviewPage(1);
                form.SetPreviewHint(31);
                SaveForm(form, args[13]);
            }

            if (args.Length >= 15)
            {
                form.SetPreviewHint(20);
                SaveForm(form, args[14]);
            }

            if (args.Length >= 16)
            {
                form.SetPreviewHint(37);
                SaveForm(form, args[15]);
            }
            if (args.Length >= 17)
            {
                form.ApplySettings(new AppSettings { AppearanceMode = "dark", Language = "zh" });
                form.ShowPreviewPage(2);
                form.SetUpdateStatus("当前已是最新版本 v2.6.0", "You are up to date · v2.6.0", false);
                form.SetPreviewHint(0);
                SaveForm(form, args[16]);
            }
            if (args.Length >= 18)
            {
                form.ApplyResult(new QuotaReadResult { IsCodexRunning = false });
                form.ShowPreviewPage(0);
                form.SetPreviewHint(0);
                SaveForm(form, args[17]);
            }
            if (args.Length >= 19)
            {
                form.ApplySettings(new AppSettings { AppearanceMode = "dark", Language = "zh", GlassEnabled = false });
                form.ShowPreviewPage(2);
                form.SetPreviewHint(0);
                SaveForm(form, args[18]);
            }
            form.Close();

            if (args.Length >= 2)
            {
                using (var widget = new TaskbarWidgetForm())
                {
                    widget.ApplyResult(result);
                    widget.Show();
                    Application.DoEvents();
                    using (var widgetBitmap = new Bitmap(widget.Width, widget.Height))
                    {
                        widget.DrawToBitmap(widgetBitmap, new Rectangle(Point.Empty, widget.Size));
                        widgetBitmap.Save(args[1], ImageFormat.Png);
                    }
                    widget.Close();
                }
            }
        }

        return 0;
    }

    private static int RunTaskbarProbe()
    {
        using (var widget = new TaskbarWidgetForm())
        {
            widget.ApplySettings(new AppSettings());
            widget.ApplyResult(new SessionQuotaReader().ReadLatest());
            widget.Show();
            widget.EnsureOnTop();
            widget.RefreshDynamicAnchor();
            var refreshDeadline = DateTime.UtcNow.AddSeconds(1);
            while (DateTime.UtcNow < refreshDeadline)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(10);
            }
            Application.DoEvents();
            Console.WriteLine("WIDGET=" + widget.GetAnchorBounds());
            var initial = TaskbarWidgetForm.CalculateLeadingPlacement(0, 1920, 146, 600, 8);
            var afterOpen = TaskbarWidgetForm.CalculateLeadingPlacement(0, 1920, 146, 500, 8);
            var afterClose = TaskbarWidgetForm.CalculateLeadingPlacement(0, 1920, 146, 650, 8);
            Console.WriteLine("SIMULATED_INITIAL=" + initial + " AFTER_OPEN_TWO=" + afterOpen + " AFTER_CLOSE=" + afterClose);
            if (afterOpen >= initial || afterClose <= initial) return 3;

            var taskbar = AutomationElement.RootElement.FindFirst(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd"));
            if (taskbar == null) return 2;
            var buttons = taskbar.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            for (var index = 0; index < buttons.Count; index++)
            {
                var current = buttons[index].Current;
                if (current.AutomationId != "SystemTrayIcon" && current.AutomationId != "NotifyItemIcon" &&
                    current.AutomationId != "StartButton" && current.AutomationId != "SearchButton" &&
                    !current.AutomationId.StartsWith("Appid:", StringComparison.Ordinal)) continue;
                Console.WriteLine(current.AutomationId + "|" + current.BoundingRectangle + "|" + current.Name.Replace(Environment.NewLine, " "));
            }
        }
        return 0;
    }

    private static Bitmap CreateTestBackdrop(Size size)
    {
        var bitmap = new Bitmap(size.Width, size.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var gradient = new LinearGradientBrush(new Rectangle(Point.Empty, size), Color.FromArgb(4, 15, 27), Color.FromArgb(22, 75, 96), 25f))
        {
            graphics.FillRectangle(gradient, new Rectangle(Point.Empty, size));
            using (var glow = new SolidBrush(Color.FromArgb(65, 58, 202, 232))) graphics.FillEllipse(glow, size.Width / 3, 40, 520, 520);
            using (var glow = new SolidBrush(Color.FromArgb(75, 20, 102, 180))) graphics.FillEllipse(glow, size.Width / 2, size.Height / 2, 620, 420);
            using (var font = new Font("Segoe UI", 46, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(Color.FromArgb(150, Color.White))) graphics.DrawString("GLASS  BACKDROP", font, brush, 90, size.Height / 2f);
        }
        return bitmap;
    }

    private static void SaveForm(Form form, string path)
    {
        Application.DoEvents();
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
