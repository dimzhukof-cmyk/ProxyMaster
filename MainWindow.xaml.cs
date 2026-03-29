using System.Windows;
using System.Windows.Controls;
using ProxyMaster.ViewModels;

namespace ProxyMaster;

public partial class MainWindow : Window
{
    private MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = (MainViewModel)DataContext;

        // Синхронизируем PasswordBox (WPF не поддерживает binding для Password)
        PasswordInput.PasswordChanged += (s, e) =>
            _vm.Password = PasswordInput.Password;

        // Авто-скролл лога вниз
        _vm.LogLines.CollectionChanged += (s, e) =>
        {
            if (LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[^1]);
        };

        Closing += (s, e) =>
        {
            if (_vm.IsRunning)
                _vm.Stop();
            _vm.SaveSettings();
            _vm.Dispose();
        };
    }
}
