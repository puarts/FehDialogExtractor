using Microsoft.Win32;
using System;
using System.Linq;
using System.Windows;

namespace FehDialogExtractor
{
    public static class ThemeManager
    {
        private const string LightPath = "/FehDialogExtractor;component/Themes/LightTheme.xaml";
        private const string DarkPath = "/FehDialogExtractor;component/Themes/DarkTheme.xaml";
        public static bool IsDark { get; private set; }

        public static void Initialize(bool followOs = true)
        {
            if (followOs)
                ApplyTheme(IsOsInDarkMode());
            else
                ApplyTheme(false);
        }

        public static void ApplyTheme(bool dark)
        {
            var app = Application.Current;
            if (app == null) return;

            var md = app.Resources.MergedDictionaries;

            // 既存テーマ辞書を削除
            for (int i = md.Count - 1; i >= 0; i--)
            {
                var src = md[i].Source;
                if (src != null && (src.OriginalString.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
                                    src.OriginalString.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase)))
                {
                    md.RemoveAt(i);
                }
            }

            var rd = new ResourceDictionary { Source = new Uri(dark ? DarkPath : LightPath, UriKind.Relative) };
            md.Add(rd);
            IsDark = dark;
        }

        public static void ToggleTheme() => ApplyTheme(!IsDark);

        public static bool IsOsInDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null)
                {
                    var v = key.GetValue("AppsUseLightTheme");
                    if (v is int iv) return iv == 0;
                    if (v is string s) return s != "1";
                }
            }
            catch
            {
                // ignore
            }
            return false;
        }
    }
}