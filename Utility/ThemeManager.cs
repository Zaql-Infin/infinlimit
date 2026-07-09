using System.Windows;
using System.Windows.Media;

namespace InfinLimit.Utility
{
    public static class ThemeManager
    {
        public const string DefaultAccentHex = "#6a6aff";

        public static Color CurrentAccent { get; private set; } = (Color)ColorConverter.ConvertFromString(DefaultAccentHex);

        public static void Initialize()
        {
            string accentHex = Config.Instance.Settings.AccentHex;
            if (!string.IsNullOrWhiteSpace(accentHex))
            {
                try
                {
                    ApplyAccent((Color)ColorConverter.ConvertFromString(accentHex), persist: false);
                    return;
                }
                catch { }
            }
            ApplyAccent(CurrentAccent, persist: false);
        }

        public static void ApplyAccent(Color color, bool persist = true)
        {
            CurrentAccent = color;
            var app = Application.Current;
            if (app != null)
            {
                app.Resources["UiAccentColor"] = color;
                app.Resources["UiAccent"] = new SolidColorBrush(color);
                app.Resources["Accent"] = color;
                app.Resources["AccentColor"] = new SolidColorBrush(color);
                app.Resources["TitleColor"] = new SolidColorBrush(color);
                app.Resources["Download"] = new SolidColorBrush(color);
            }
            if (persist)
            {
                Config.Instance.Settings.AccentHex = ColorToHex(color);
                Config.Save();
            }
        }

        public static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
