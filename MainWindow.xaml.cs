using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using ProxyMaster.ViewModels;

namespace ProxyMaster;

public partial class MainWindow : Window
{
    private MainViewModel _vm;

    private const double CompactW = 512;
    private const double CompactH = 512;
    private const double FullW    = 820;
    private const double FullH    = 700;

    public MainWindow()
    {
        InitializeComponent();
        _vm = (MainViewModel)DataContext;

        // Синхронизируем PasswordBox (WPF не поддерживает binding для Password)
        PasswordInput.PasswordChanged += (s, e) =>
            _vm.Password = PasswordInput.Password;

        // Позволяет ViewModel обновить PasswordBox при загрузке сохранённого профиля
        _vm.PasswordSetter = pwd => PasswordInput.Password = pwd;

        // Авто-скролл лога вниз при новых строках
        _vm.LogLines.CollectionChanged += (s, e) =>
        {
            if (LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[^1]);
        };

        // Ctrl+C — копируем выделенные строки
        LogList.KeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.C &&
                System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control &&
                LogList.SelectedItems.Count > 0)
            {
                Clipboard.SetText(string.Join("\n", LogList.SelectedItems.Cast<string>()));
                e.Handled = true;
            }
        };

        // Анимация изменения размера при переключении компактного режима
        _vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsCompactMode))
                AnimateWindowSize(_vm.IsCompactMode);
        };

        Closing += (s, e) =>
        {
            if (_vm.IsRunning)
                _vm.Stop();
            _vm.SaveSettings();
            _vm.Dispose();
        };
    }

    private void AnimateWindowSize(bool compact)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var dur  = new Duration(TimeSpan.FromMilliseconds(220));

        if (compact)
        {
            MinWidth   = CompactW; MinHeight = CompactH;
            MaxWidth   = CompactW; MaxHeight = CompactH;
            ResizeMode = ResizeMode.NoResize;
            // WindowStyle остаётся SingleBorderWindow — заголовок для перетаскивания
        }
        else
        {
            MinWidth   = 700; MinHeight = 560;
            MaxWidth   = double.PositiveInfinity;
            MaxHeight  = double.PositiveInfinity;
            ResizeMode = ResizeMode.CanResize;
        }

        BeginAnimation(WidthProperty,
            new DoubleAnimation(compact ? CompactW : FullW, dur) { EasingFunction = ease });
        BeginAnimation(HeightProperty,
            new DoubleAnimation(compact ? CompactH : FullH, dur) { EasingFunction = ease });
    }
}
