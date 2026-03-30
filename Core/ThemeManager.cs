using System.Windows;

namespace ProxyMaster.Core;

public static class ThemeManager
{
    public static string CurrentTheme { get; private set; } = "Dark";

    public static readonly (string Id, string PreviewBg, string PreviewAccent)[] Themes =
    {
        ("Dark",       "#1E1E2E", "#7C6AF7"),
        ("Light",      "#F2F2F7", "#5E5CE6"),
        ("DarkBlue",   "#0A0F1E", "#4A9EFF"),
        ("DarkGreen",  "#0A1A0E", "#2ECC71"),
        ("DarkPurple", "#1A0A2E", "#B44FE8"),
    };

    public static void ApplyTheme(string themeId)
    {
        var app = Application.Current;
        if (app == null) return;

        var dicts = app.Resources.MergedDictionaries;

        // Убираем старую тему
        for (int i = dicts.Count - 1; i >= 0; i--)
        {
            var src = dicts[i].Source?.OriginalString ?? "";
            if (src.Contains("/Themes/Theme"))
            {
                dicts.RemoveAt(i);
                break;
            }
        }

        // Добавляем новую
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/Theme{themeId}.xaml", UriKind.Absolute)
        };
        dicts.Insert(0, dict);

        CurrentTheme = themeId;
    }
}
