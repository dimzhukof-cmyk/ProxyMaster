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

        // Загружаем новую тему
        var newTheme = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/Theme{themeId}.xaml",
                             UriKind.Absolute)
        };

        // Копируем каждый ресурс напрямую в Application.Resources —
        // это гарантирует что все DynamicResource-биндинги обновятся немедленно
        foreach (var key in newTheme.Keys)
            app.Resources[key] = newTheme[key];

        CurrentTheme = themeId;
    }
}
