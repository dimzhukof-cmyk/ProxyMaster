using System.IO;
using System.Text.Json;
using System.Windows;
using ProxyMaster.Core;
using ProxyMaster.Models;

namespace ProxyMaster;

public partial class App : Application
{
    private static readonly string SettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "ProxyMaster", "settings.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Загружаем сохранённую тему и язык до открытия главного окна
        LoadThemeAndLanguage();

        DispatcherUnhandledException += (s, ex) =>
        {
            MessageBox.Show($"Критическая ошибка:\n{ex.Exception.Message}",
                "ProxyMaster", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
    }

    private static void LoadThemeAndLanguage()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
            if (s == null) return;

            if (!string.IsNullOrEmpty(s.Theme))
                ThemeManager.ApplyTheme(s.Theme);

            if (!string.IsNullOrEmpty(s.Language))
                LocalizationService.Instance.Language = s.Language;
        }
        catch { /* первый запуск */ }
    }
}
