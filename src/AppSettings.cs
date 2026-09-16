using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace CodexQuotaTray
{
    internal enum ActivityRange
    {
        Hours5,
        Hours24,
        Days7,
        Days30
    }

    internal sealed class AppSettings
    {
        public AppSettings()
        {
            Reset();
        }

        public bool WidgetVisible { get; set; }
        public bool QuotaPrimaryEmphasis { get; set; }
        public bool CompletionReminderEnabled { get; set; }
        public bool GlassEnabled { get; set; }
        public int GlassOpacityPercent { get; set; }
        public string ThemeId { get; set; }
        public string AppearanceMode { get; set; }
        public Color CustomPrimary { get; set; }
        public Color CustomSecondary { get; set; }
        public string Language { get; set; }
        public ActivityRange ActivityRange { get; set; }
        public int PopupScalePercent { get; set; }
        public int FontScalePercent { get; set; }
        public int WidgetScalePercent { get; set; }

        public bool IsEnglish
        {
            get
            {
                if (string.Equals(Language, "en", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(Language, "zh", StringComparison.OrdinalIgnoreCase)) return false;
                return !SystemLanguageName().StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            }
        }

        public void Reset()
        {
            WidgetVisible = true;
            QuotaPrimaryEmphasis = true;
            CompletionReminderEnabled = false;
            GlassEnabled = true;
            GlassOpacityPercent = 40;
            ThemeId = "cyan";
            AppearanceMode = "light";
            CustomPrimary = Color.FromArgb(67, 194, 221);
            CustomSecondary = Color.FromArgb(65, 174, 235);
            Language = "system";
            ActivityRange = ActivityRange.Days30;
            PopupScalePercent = 100;
            FontScalePercent = 100;
            WidgetScalePercent = 100;
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            var path = GetPath();
            if (!File.Exists(path))
            {
                return settings;
            }

            try
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(path))
                {
                    var separator = line.IndexOf('=');
                    if (separator <= 0) continue;
                    values[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
                }

                bool widgetVisible;
                if (TryGet(values, "WidgetVisible", out widgetVisible)) settings.WidgetVisible = widgetVisible;
                if (TryGet(values, "QuotaPrimaryEmphasis", out widgetVisible)) settings.QuotaPrimaryEmphasis = widgetVisible;
                if (TryGet(values, "CompletionReminderEnabled", out widgetVisible)) settings.CompletionReminderEnabled = widgetVisible;
                if (TryGet(values, "GlassEnabled", out widgetVisible)) settings.GlassEnabled = widgetVisible;

                string text;
                if (values.TryGetValue("Theme", out text) && ThemePalette.IsKnownTheme(text)) settings.ThemeId = text;
                if (values.TryGetValue("Appearance", out text) && (text == "system" || text == "dark" || text == "light"))
                    settings.AppearanceMode = text;
                if (values.TryGetValue("Language", out text) && (text == "system" || text == "zh" || text == "en")) settings.Language = text;

                int seconds;
                if (TryGet(values, "GlassOpacityPercent", out seconds) && IsInRange(seconds, 5, 95))
                    settings.GlassOpacityPercent = seconds;

                ActivityRange range;
                if (values.TryGetValue("ActivityRange", out text) && Enum.TryParse(text, true, out range))
                    settings.ActivityRange = range;

                if (TryGet(values, "PopupScalePercent", out seconds) && IsInRange(seconds, 80, 120)) settings.PopupScalePercent = seconds;
                if (TryGet(values, "FontScalePercent", out seconds) && IsInRange(seconds, 80, 120)) settings.FontScalePercent = seconds;
                if (TryGet(values, "WidgetScalePercent", out seconds) && IsInRange(seconds, 70, 130)) settings.WidgetScalePercent = seconds;
                int argb;
                if (TryGet(values, "CustomPrimary", out argb)) settings.CustomPrimary = Color.FromArgb(argb);
                if (TryGet(values, "CustomSecondary", out argb)) settings.CustomSecondary = Color.FromArgb(argb);
            }
            catch
            {
                settings.Reset();
            }

            return settings;
        }

        public void Save()
        {
            var path = GetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, new[]
            {
                "WidgetVisible=" + WidgetVisible,
                "QuotaPrimaryEmphasis=" + QuotaPrimaryEmphasis,
                "CompletionReminderEnabled=" + CompletionReminderEnabled,
                "GlassEnabled=" + GlassEnabled,
                "GlassOpacityPercent=" + GlassOpacityPercent,
                "Theme=" + ThemeId,
                "Appearance=" + AppearanceMode,
                "CustomPrimary=" + CustomPrimary.ToArgb().ToString(CultureInfo.InvariantCulture),
                "CustomSecondary=" + CustomSecondary.ToArgb().ToString(CultureInfo.InvariantCulture),
                "Language=" + Language,
                "ActivityRange=" + ActivityRange,
                "PopupScalePercent=" + PopupScalePercent,
                "FontScalePercent=" + FontScalePercent,
                "WidgetScalePercent=" + WidgetScalePercent
            });
        }

        private static string GetPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexMeter",
                "settings.ini");
        }

        private static bool TryGet(Dictionary<string, string> values, string key, out bool parsed)
        {
            string value;
            parsed = false;
            return values.TryGetValue(key, out value) && bool.TryParse(value, out parsed);
        }

        private static bool TryGet(Dictionary<string, string> values, string key, out int parsed)
        {
            string value;
            parsed = 0;
            return values.TryGetValue(key, out value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
        }

        private static bool IsInRange(int value, int minimum, int maximum)
        {
            return value >= minimum && value <= maximum;
        }

        internal static string SystemLanguageName()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\International\User Profile"))
                {
                    var languages = key == null ? null : key.GetValue("Languages") as string[];
                    if (languages != null && languages.Length > 0 && !string.IsNullOrWhiteSpace(languages[0]))
                        return languages[0];
                }
            }
            catch { }

            return CultureInfo.InstalledUICulture.Name;
        }

    }
}
