using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using ProxyMaster.Core;
using ProxyMaster.Models;
using SystemProxy = ProxyMaster.Core.SystemProxy;
using System.Linq;

namespace ProxyMaster.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    // ---- Сервисы --------------------------------------------------------
    private readonly ConnectionTracker     _tracker = new();
    private PacketInterceptor?             _interceptor;
    private TransparentProxyServer?        _server;

    private static readonly string SettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "ProxyMaster", "settings.json");

    // ---- Состояние ------------------------------------------------------
    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusColor)); }
    }

    public string StatusText  => _isRunning ? "АКТИВЕН"    : "ОСТАНОВЛЕН";
    public string StatusColor => _isRunning ? "#4ECDC4" : "#FF6B6B";

    private long _bytesTotal;
    public string BytesDisplay => FormatBytes(_bytesTotal);

    private int _activeConnections;
    public int ActiveConnections
    {
        get => _activeConnections;
        set { _activeConnections = value; OnPropertyChanged(); }
    }

    // ---- Настройки прокси -----------------------------------------------
    public AppSettings Settings { get; private set; } = new();

    private string _proxyHost = "127.0.0.1";
    public string ProxyHost
    {
        get => _proxyHost;
        set { _proxyHost = value; OnPropertyChanged(); }
    }

    private int _proxyPort = 1080;
    public string ProxyPort
    {
        get => _proxyPort.ToString();
        set { if (int.TryParse(value, out int p)) { _proxyPort = p; OnPropertyChanged(); } }
    }

    private ProxyType _proxyType = ProxyType.Socks5;
    public ProxyType ProxyType
    {
        get => _proxyType;
        set { _proxyType = value; OnPropertyChanged(); }
    }

    private string _username = "";
    public string Username
    {
        get => _username;
        set { _username = value; OnPropertyChanged(); }
    }

    private string _password = "";
    public string Password
    {
        get => _password;
        set { _password = value; OnPropertyChanged(); }
    }

    // ---- Фильтр по приложениям ------------------------------------------
    private bool _filterByProcess;
    public bool FilterByProcess
    {
        get => _filterByProcess;
        set
        {
            _filterByProcess = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowProcessList));
            OnPropertyChanged(nameof(FilterByProcessAll));
        }
    }
    public bool FilterByProcessAll
    {
        get => !_filterByProcess;
        set { if (value) FilterByProcess = false; }
    }
    public bool ShowProcessList => _filterByProcess;

    public ObservableCollection<ProcessEntry> Processes { get; } = new();

    public string SelectedProcessCount =>
        $"Выбрано: {Processes.Count(p => p.IsSelected)} из {Processes.Count}";

    // ---- Лог ------------------------------------------------------------
    public ObservableCollection<string> LogLines { get; } = new();

    // ---- Команды --------------------------------------------------------
    public RelayCommand StartCommand          { get; }
    public RelayCommand StopCommand           { get; }
    public RelayCommand SaveCommand           { get; }
    public RelayCommand ClearLogCommand       { get; }
    public RelayCommand RefreshProcessesCommand { get; }

    public MainViewModel()
    {
        StartCommand             = new RelayCommand(_ => Start(),             _ => !IsRunning);
        StopCommand              = new RelayCommand(_ => Stop(),              _ => IsRunning);
        SaveCommand              = new RelayCommand(_ => SaveSettings());
        ClearLogCommand          = new RelayCommand(_ => LogLines.Clear());
        RefreshProcessesCommand  = new RelayCommand(_ => RefreshProcesses());

        LoadSettings();
    }

    // ---- Запуск / Остановка ---------------------------------------------

    public void Start()
    {
        if (IsRunning) return;

        try
        {
            var cfg = new ProxyConfig
            {
                Type     = _proxyType,
                Host     = _proxyHost,
                Port     = _proxyPort,
                Username = string.IsNullOrWhiteSpace(_username) ? null : _username,
                Password = string.IsNullOrWhiteSpace(_password) ? null : _password,
            };

            ushort localPort = Settings.LocalProxyPort;

            _server = new TransparentProxyServer(_tracker, localPort) { Config = cfg };
            _server.LogMessage      += AddLog;
            _server.BytesTransferred += bytes =>
            {
                _bytesTotal += bytes;
                Application.Current?.Dispatcher.Invoke(() => OnPropertyChanged(nameof(BytesDisplay)));
            };
            _server.Start();

            _interceptor = new PacketInterceptor(_tracker, localPort, cfg.Host, cfg.Port);
            _interceptor.LogMessage += AddLog;

            if (_filterByProcess)
            {
                // Режим фильтрации: только выбранные приложения, без системного прокси
                var selected = new HashSet<string>(
                    Processes.Where(p => p.IsSelected).Select(p => p.Name),
                    StringComparer.OrdinalIgnoreCase);
                _interceptor.AllowedProcessNames = selected;
                AddLog($"Режим: выбранные приложения ({selected.Count} шт.)");
            }
            else
            {
                // Режим «все»: системный прокси покрывает браузеры
                SystemProxy.Enable("127.0.0.1", localPort);
            }

            _interceptor.Start();

            IsRunning = true;
            AddLog("=== ProxyMaster запущен ===");
            AddLog($"Системный прокси установлен → 127.0.0.1:{localPort}");
            SaveSettings();
        }
        catch (Exception ex)
        {
            AddLog($"[ОШИБКА] {ex.Message}");
            MessageBox.Show(ex.Message, "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            StopInternal();
        }
    }

    public void Stop()
    {
        StopInternal();
        AddLog("=== ProxyMaster остановлен ===");
    }

    private void StopInternal()
    {
        // Снимаем системный прокси в любом случае (на случай если он был установлен)
        SystemProxy.Disable();
        _interceptor?.Stop();
        _interceptor = null;
        _server?.Stop();
        _server = null;
        IsRunning = false;
    }

    // ---- Настройки ------------------------------------------------------

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null)
                {
                    Settings        = s;
                    ProxyHost       = s.Proxy.Host;
                    _proxyPort      = s.Proxy.Port;
                    ProxyType       = s.Proxy.Type;
                    Username        = s.Proxy.Username ?? "";
                    Password        = SecureStorage.Unprotect(s.Proxy.PasswordProtected);
                    FilterByProcess = s.FilterByProcess;
                    OnPropertyChanged(nameof(ProxyPort));

                    // Загружаем список процессов с сохранёнными выборами
                    if (s.SelectedProcesses.Count > 0)
                        RefreshProcesses(s.SelectedProcesses);
                }
            }
        }
        catch { /* первый запуск, игнорируем */ }
    }

    public void SaveSettings()
    {
        Settings.Proxy = new ProxyConfig
        {
            Type              = _proxyType,
            Host              = _proxyHost,
            Port              = _proxyPort,
            Username          = string.IsNullOrWhiteSpace(_username) ? null : _username,
            PasswordProtected = SecureStorage.Protect(_password),
        };
        Settings.FilterByProcess   = _filterByProcess;
        Settings.SelectedProcesses = Processes.Where(p => p.IsSelected).Select(p => p.Name).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath,
            JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ---- Список процессов -----------------------------------------------

    public void RefreshProcesses(IEnumerable<string>? preselected = null)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Сохраняем текущий выбор
        foreach (var p in Processes.Where(p => p.IsSelected))
            selected.Add(p.Name);

        // Добавляем сохранённые (при загрузке настроек)
        if (preselected != null)
            foreach (var name in preselected)
                selected.Add(name);

        Application.Current?.Dispatcher.Invoke(() =>
        {
            Processes.Clear();
            foreach (var (name, path) in ProcessHelper.GetRunningProcesses())
            {
                var entry = new ProcessEntry
                {
                    Name        = name,
                    DisplayPath = path,
                    IsSelected  = selected.Contains(name),
                };
                entry.PropertyChanged += (_, _) =>
                    OnPropertyChanged(nameof(SelectedProcessCount));
                Processes.Add(entry);
            }
            OnPropertyChanged(nameof(SelectedProcessCount));
        });
    }

    // ---- Лог ------------------------------------------------------------

    private void AddLog(string msg)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        Application.Current?.Dispatcher.Invoke(() =>
        {
            LogLines.Add(line);
            if (LogLines.Count > 500) LogLines.RemoveAt(0);
        });
    }

    // ---- Вспомогательные ------------------------------------------------

    private static string FormatBytes(long b)
    {
        if (b < 1024)        return $"{b} B";
        if (b < 1024 * 1024) return $"{b / 1024.0:F1} KB";
        return $"{b / 1024.0 / 1024.0:F2} MB";
    }

    public void Dispose() => StopInternal();

    // ---- INotifyPropertyChanged -----------------------------------------

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Простая реализация ICommand.</summary>
public class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute    = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? p) => _canExecute?.Invoke(p) ?? true;
    public void Execute(object? p)    => _execute(p);

    public event EventHandler? CanExecuteChanged
    {
        add    => System.Windows.Input.CommandManager.RequerySuggested += value;
        remove => System.Windows.Input.CommandManager.RequerySuggested -= value;
    }
}
