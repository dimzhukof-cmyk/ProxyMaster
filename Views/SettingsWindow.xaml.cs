using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ProxyMaster.Core;

namespace ProxyMaster.Views;

public partial class SettingsWindow : Window
{
    private string _selectedLanguage;
    private string _selectedTheme;

    public SettingsWindow(string currentLanguage, string currentTheme)
    {
        InitializeComponent();
        _selectedLanguage = currentLanguage;
        _selectedTheme    = currentTheme;

        BuildLanguageButtons();
        BuildThemeSwatches();
    }

    // ---- Кнопки языка ----

    private void BuildLanguageButtons()
    {
        LangPanel.Children.Clear();
        foreach (var (code, name) in LocalizationService.Languages)
        {
            var btn = new Button
            {
                Content = name,
                Tag     = code,
                Style   = code == _selectedLanguage
                          ? (Style)FindResource("LangButtonActive")
                          : (Style)FindResource("LangButton"),
            };
            btn.Click += LangButton_Click;
            LangPanel.Children.Add(btn);
        }
    }

    private void LangButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _selectedLanguage = (string)btn.Tag;
        LocalizationService.Instance.Language = _selectedLanguage;

        // Обновляем стили всех кнопок
        foreach (Button b in LangPanel.Children)
            b.Style = (string)b.Tag == _selectedLanguage
                      ? (Style)FindResource("LangButtonActive")
                      : (Style)FindResource("LangButton");
    }

    // ---- Свотчи тем ----

    private void BuildThemeSwatches()
    {
        var loc = LocalizationService.Instance;
        var themeKeys = new[] { "theme_dark", "theme_light", "theme_blue", "theme_green", "theme_purple" };

        ThemePanel.Children.Clear();
        for (int i = 0; i < ThemeManager.Themes.Length; i++)
        {
            var (id, bg, accent) = ThemeManager.Themes[i];
            string labelKey = themeKeys[i];

            var swatch = BuildSwatch(id, bg, accent, loc[labelKey], id == _selectedTheme);
            ThemePanel.Children.Add(swatch);
        }
    }

    private Border BuildSwatch(string themeId, string bgHex, string accentHex,
                                string label, bool isActive)
    {
        var bgColor     = (Color)ColorConverter.ConvertFromString(bgHex);
        var accentColor = (Color)ColorConverter.ConvertFromString(accentHex);

        var border = new Border
        {
            Width         = 82,
            Height        = 60,
            Margin        = new Thickness(4),
            CornerRadius  = new CornerRadius(10),
            Background    = new SolidColorBrush(bgColor),
            BorderBrush   = isActive
                            ? new SolidColorBrush(accentColor)
                            : new SolidColorBrush(Color.FromArgb(80, 128, 128, 128)),
            BorderThickness = new Thickness(isActive ? 2.5 : 1.5),
            Cursor        = System.Windows.Input.Cursors.Hand,
            Tag           = themeId,
        };

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        // Полоска акцентного цвета
        stack.Children.Add(new Border
        {
            Width           = 40,
            Height          = 6,
            CornerRadius    = new CornerRadius(3),
            Background      = new SolidColorBrush(accentColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin          = new Thickness(0, 0, 0, 6),
        });

        // Подпись
        stack.Children.Add(new TextBlock
        {
            Text              = label,
            FontSize          = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground        = isActive
                                ? new SolidColorBrush(Colors.White)
                                : new SolidColorBrush(Color.FromArgb(180, 200, 200, 200)),
        });

        border.Child = stack;
        border.MouseLeftButtonUp += ThemeSwatch_Click;
        return border;
    }

    private void ThemeSwatch_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Border b) return;
        _selectedTheme = (string)b.Tag;
        ThemeManager.ApplyTheme(_selectedTheme);
        BuildThemeSwatches(); // перерисовываем с новым выделением
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
