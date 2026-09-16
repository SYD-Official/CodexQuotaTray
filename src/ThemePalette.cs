using System;
using System.Collections.Generic;
using System.Drawing;
using Microsoft.Win32;

namespace CodexQuotaTray
{
    internal sealed class ThemePalette
    {
        public string Id { get; private set; }
        public string ChineseName { get; private set; }
        public string EnglishName { get; private set; }
        public bool IsLight { get; private set; }
        public Color Background { get; private set; }
        public Color Surface { get; private set; }
        public Color Text { get; private set; }
        public Color Muted { get; private set; }
        public Color Faint { get; private set; }
        public Color Divider { get; private set; }
        public Color Track { get; private set; }
        public Color Primary { get; private set; }
        public Color Secondary { get; private set; }
        public Color Success { get; private set; }
        public Color Warning { get; private set; }
        public Color Danger { get; private set; }

        public string Name(bool english)
        {
            return english ? EnglishName : ChineseName;
        }

        public Color QuotaColor(int remainingPercent)
        {
            if (remainingPercent <= 10) return Danger;
            if (remainingPercent <= 20) return Warning;
            return Primary;
        }

        public static IList<ThemePalette> Presets()
        {
            return new[]
            {
                Create("cyan", "深海青", "Deep Cyan", "#1A1F25", "#232A32", "#43C2DD", "#41AEEB", "#56CF97", "#F4A449", "#FF625C"),
                Create("aurora", "极光紫", "Aurora", "#1A1F25", "#232A32", "#8F82CF", "#B97EC1", "#59C79B", "#D9A354", "#E86A72"),
                Create("emerald", "森林绿", "Emerald", "#1A1F25", "#232A32", "#55B492", "#6C98BD", "#55BD91", "#D3A44F", "#E06B68"),
                Create("sunset", "暮光橙", "Sunset", "#1A1F25", "#232A32", "#D39A58", "#BE788B", "#5DB897", "#D5A74F", "#E36C68")
            };
        }

        public static bool IsKnownTheme(string id)
        {
            if (string.Equals(id, "custom", StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var theme in Presets())
            {
                if (string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static ThemePalette FromSettings(AppSettings settings)
        {
            ThemePalette selected = null;
            if (string.Equals(settings.ThemeId, "custom", StringComparison.OrdinalIgnoreCase))
            {
                selected = Create("custom", "自定义", "Custom", "#1A1F25", "#232A32",
                    ColorToHex(settings.CustomPrimary), ColorToHex(settings.CustomSecondary),
                    "#56CF97", "#F4A449", "#FF625C");
            }
            else
            {
                foreach (var theme in Presets())
                {
                    if (!string.Equals(theme.Id, settings.ThemeId, StringComparison.OrdinalIgnoreCase)) continue;
                    selected = theme;
                    break;
                }
            }

            if (selected == null) selected = Presets()[0];
            var useLight = string.Equals(settings.AppearanceMode, "light", StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(settings.AppearanceMode, "system", StringComparison.OrdinalIgnoreCase) && SystemUsesLightAppearance());
            return WithAppearance(selected, useLight);
        }

        public static bool SystemUsesLightAppearance()
        {
            return ReadWindowsThemeValue("AppsUseLightTheme");
        }

        public static bool SystemUsesLightTaskbar()
        {
            return ReadWindowsThemeValue("SystemUsesLightTheme");
        }

        private static bool ReadWindowsThemeValue(string valueName)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var value = key == null ? null : key.GetValue(valueName);
                    return value != null && Convert.ToInt32(value) != 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static ThemePalette WithAppearance(ThemePalette source, bool light)
        {
            return new ThemePalette
            {
                Id = source.Id,
                ChineseName = source.ChineseName,
                EnglishName = source.EnglishName,
                IsLight = light,
                Background = light ? Parse("#F4F7FA") : source.Background,
                Surface = light ? Parse("#FFFFFF") : source.Surface,
                Text = light ? Parse("#18212B") : source.Text,
                Muted = light ? Parse("#5D6A78") : source.Muted,
                Faint = light ? Parse("#8A96A3") : source.Faint,
                Divider = light ? Parse("#D6DEE6") : source.Divider,
                Track = light ? Parse("#DCE3EA") : source.Track,
                Primary = source.Primary,
                Secondary = source.Secondary,
                Success = source.Success,
                Warning = source.Warning,
                Danger = source.Danger
            };
        }

        private static ThemePalette Create(
            string id,
            string chineseName,
            string englishName,
            string background,
            string surface,
            string primary,
            string secondary,
            string success,
            string warning,
            string danger)
        {
            return new ThemePalette
            {
                Id = id,
                ChineseName = chineseName,
                EnglishName = englishName,
                IsLight = false,
                Background = Parse(background),
                Surface = Parse(surface),
                Text = Parse("#F2F5F8"),
                Muted = Parse("#A5AFBA"),
                Faint = Parse("#717B86"),
                Divider = Parse("#39424B"),
                Track = Parse("#414A54"),
                Primary = Parse(primary),
                Secondary = Parse(secondary),
                Success = Parse(success),
                Warning = Parse(warning),
                Danger = Parse(danger)
            };
        }

        private static Color Parse(string value)
        {
            return ColorTranslator.FromHtml(value);
        }

        private static string ColorToHex(Color color)
        {
            return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }
    }
}
