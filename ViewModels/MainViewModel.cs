using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using ProxyMaster.Core;
using ProxyMaster.Models;
using ProxyMaster.Views;
using SystemProxy = ProxyMaster.Core.SystemProxy;
using System.Linq;

namespace ProxyMaster.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    // ---- Services --------------------------------------------------------
    private readonly ConnectionTracker     _tracker = new();
    private PacketInterceptor?             _interceptor;
    private TransparentProxyServer?        _server;

    private static readonly string SettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "ProxyMaster", "settings.json");

    // ---- State -----------------------------------------------------------
    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusColor)); OnPropertyChanged(nameof(CompactStatusText)); }
    }

    public string StatusText        => Loc[_isRunning ? "status_active" : "status_stopped"];
    public string StatusColor       => _isRunning ? "#4ECDC4" : "#FF6B6B";
    public string CompactStatusText => _isRunning ? "ON" : "OFF";
    public string ProxyTypeDisplay  => _proxyType == ProxyType.Socks5 ? "SOCKS5" : "HTTP";

    private long _bytesTotal;
    public string BytesDisplay => FormatBytes(_bytesTotal);

    private int _activeConnections;
    public int ActiveConnections
    {
        get => _activeConnections;
        set { _activeConnections = value; OnPropertyChanged(); }
    }

    // ---- Proxy settings --------------------------------------------------
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
        set { _proxyType = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProxyTypeDisplay)); }
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

    // Called from code-behind to push a value into PasswordBox
    public Action<string>? PasswordSetter { get; set; }

    // ---- Saved server profiles -------------------------------------------
    public ObservableCollection<ProxyConfig> SavedServers { get; } = new();

    private int _activeServerIndex = -1;
    public int ActiveServerIndex
    {
        get => _activeServerIndex;
        set
        {
            _activeServerIndex = value;
            OnPropertyChanged();
            if (value >= 0 && value < SavedServers.Count)
                LoadServerProfile(SavedServers[value]);
        }
    }

    private void LoadServerProfile(ProxyConfig cfg)
    {
        ProxyHost = cfg.Host;
        _proxyPort = cfg.Port;
        OnPropertyChanged(nameof(ProxyPort));
        ProxyType = cfg.Type;
        Username = cfg.Username ?? "";
        var pwd = SecureStorage.Unprotect(cfg.PasswordProtected);
        Password = pwd;
        PasswordSetter?.Invoke(pwd);
    }

    // ---- Process filter --------------------------------------------------
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

    private ICollectionView? _filteredProcesses;
    public ICollectionView FilteredProcesses => _filteredProcesses ??= CreateFilteredView();

    private ICollectionView CreateFilteredView()
    {
        var view = CollectionViewSource.GetDefaultView(Processes);
        view.Filter = obj => obj is ProcessEntry p &&
            (string.IsNullOrEmpty(_processFilter) ||
             p.Name.Contains(_processFilter, StringComparison.OrdinalIgnoreCase));
        return view;
    }

    private string _processFilter = "";
    public string ProcessFilter
    {
        get => _processFilter;
        set
        {
            _processFilter = value;
            OnPropertyChanged();
            FilteredProcesses.Refresh();
        }
    }

    public string SelectedProcessCount =>
        string.Format(Loc["sel_count"], Processes.Count(p => p.IsSelected), Processes.Count);

    // ---- Localization (proxy for XAML binding) ---------------------------
    public LocalizationService Loc => LocalizationService.Instance;

    // ---- Log -------------------------------------------------------------
    public ObservableCollection<string> LogLines { get; } = new();

    // Полный список строк (до фильтра)
    private readonly List<string> _allLogLines = new();

    private string _logFilter = string.Empty;
    public string LogFilter
    {
        get => _logFilter;
        set
        {
            if (_logFilter == value) return;
            _logFilter = value;
            OnPropertyChanged();
            ApplyLogFilter();
        }
    }

    private void ApplyLogFilter()
    {
        LogLines.Clear();
        var f = _logFilter;
        var lines = string.IsNullOrEmpty(f)
            ? _allLogLines
            : _allLogLines.Where(l => l.Contains(f, StringComparison.OrdinalIgnoreCase));
        foreach (var l in lines)
            LogLines.Add(l);
    }

    // Очередь для логов: proxy-потоки пишут сюда без блокировки,
    // UI-таймер вычитывает каждые 50 мс — thread pool никогда не блокируется
    private volatile bool _bytesUpdatePending;

    // ---- Compact mode ----------------------------------------------------
    private bool _isCompactMode;
    public bool IsCompactMode
    {
        get => _isCompactMode;
        set
        {
            if (_isCompactMode == value) return;
            _isCompactMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFullMode));
        }
    }
    public bool IsFullMode => !_isCompactMode;

    // ---- Commands --------------------------------------------------------
    public RelayCommand StartCommand            { get; }
    public RelayCommand StopCommand             { get; }
    public RelayCommand ToggleProxyCommand      { get; }
    public RelayCommand SaveCommand             { get; }
    public RelayCommand ClearLogCommand         { get; }
    public RelayCommand RefreshProcessesCommand { get; }
    public RelayCommand SettingsCommand         { get; }
    public RelayCommand AddServerCommand        { get; }
    public RelayCommand DeleteServerCommand     { get; }
    public RelayCommand ToggleCompactCommand    { get; }

    public MainViewModel()
    {
        StartCommand            = new RelayCommand(_ => Start(),            _ => !IsRunning);
        StopCommand             = new RelayCommand(_ => Stop(),             _ => IsRunning);
        ToggleProxyCommand      = new RelayCommand(_ => { if (IsRunning) Stop(); else Start(); });
        SaveCommand             = new RelayCommand(_ => SaveSettings());
        ClearLogCommand         = new RelayCommand(_ => { LogLines.Clear(); _allLogLines.Clear(); });
        RefreshProcessesCommand = new RelayCommand(_ => RefreshProcesses());
        SettingsCommand         = new RelayCommand(_ => OpenSettings());
        AddServerCommand        = new RelayCommand(_ => AddServer());
        DeleteServerCommand     = new RelayCommand(_ => DeleteServer(),
                                      _ => _activeServerIndex >= 0 && SavedServers.Count > 0);
        ToggleCompactCommand    = new RelayCommand(_ => IsCompactMode = !IsCompactMode);

        LocalizationService.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "Item[]")
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(SelectedProcessCount));
            }
        };

        LoadSettings();
    }

    // ---- Start / Stop ---------------------------------------------------

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
            _server.LogMessage       += AddLog;
            _server.BytesTransferred += bytes =>
            {
                Interlocked.Add(ref _bytesTotal, bytes);
                if (!_bytesUpdatePending)
                {
                    _bytesUpdatePending = true;
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        _bytesUpdatePending = false;
                        OnPropertyChanged(nameof(BytesDisplay));
                    });
                }
            };
            _server.Start();

            _interceptor = new PacketInterceptor(_tracker, localPort, cfg.Host, cfg.Port);
            _interceptor.LogMessage += AddLog;

            if (_filterByProcess)
            {
                var selected = new HashSet<string>(
                    Processes.Where(p => p.IsSelected).Select(p => p.Name),
                    StringComparer.OrdinalIgnoreCase);
                _interceptor.AllowedProcessNames = selected;
                AddLog($"Mode: selected apps ({selected.Count})");
            }
            else
            {
                SystemProxy.Enable("127.0.0.1", localPort);
            }

            _interceptor.Start();

            IsRunning = true;
            AddLog("=== ProxyMaster started ===");
            AddLog($"Local proxy → 127.0.0.1:{localPort}");
            SaveSettings();
        }
        catch (System.Net.Sockets.SocketException sex) when (sex.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
        {
            string msg = $"Порт {Settings.LocalProxyPort} уже занят.\n\nЗакройте другой экземпляр ProxyMaster и попробуйте снова.";
            AddLog($"[ERROR] Порт {Settings.LocalProxyPort} занят");
            MessageBox.Show(msg, "ProxyMaster", MessageBoxButton.OK, MessageBoxImage.Warning);
            StopInternal();
        }
        catch (Exception ex)
        {
            AddLog($"[ERROR] {ex.Message}");
            MessageBox.Show(ex.Message, "Start error", MessageBoxButton.OK, MessageBoxImage.Error);
            StopInternal();
        }
    }

    public void Stop()
    {
        StopInternal();
        AddLog("=== ProxyMaster stopped ===");
    }

    private void StopInternal()
    {
        SystemProxy.Disable();
        _interceptor?.Stop();
        _interceptor = null;
        _server?.Stop();
        _server = null;
        IsRunning = false;
    }

    // ---- Settings -------------------------------------------------------

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

                    foreach (var srv in s.SavedServers)
                        SavedServers.Add(srv);

                    if (s.SelectedProcesses.Count > 0)
                        RefreshProcesses(s.SelectedProcesses);
                }
            }
        }
        catch { /* first run */ }
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
        Settings.Language          = LocalizationService.Instance.Language;
        Settings.Theme             = ThemeManager.CurrentTheme;
        Settings.SavedServers      = SavedServers.ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath,
            JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ---- Saved server profiles ------------------------------------------

    private void AddServer()
    {
        var cfg = new ProxyConfig
        {
            Type              = _proxyType,
            Host              = _proxyHost,
            Port              = _proxyPort,
            Username          = string.IsNullOrWhiteSpace(_username) ? null : _username,
            PasswordProtected = SecureStorage.Protect(_password),
        };
        SavedServers.Add(cfg);
        _activeServerIndex = SavedServers.Count - 1;
        OnPropertyChanged(nameof(ActiveServerIndex));
        SaveSettings();
    }

    private void DeleteServer()
    {
        if (_activeServerIndex < 0 || _activeServerIndex >= SavedServers.Count) return;
        SavedServers.RemoveAt(_activeServerIndex);
        _activeServerIndex = SavedServers.Count > 0 ? 0 : -1;
        OnPropertyChanged(nameof(ActiveServerIndex));
        SaveSettings();
    }

    // ---- Settings: language and theme ------------------------------------

    private void OpenSettings()
    {
        var win = new SettingsWindow(
            LocalizationService.Instance.Language,
            ThemeManager.CurrentTheme)
        {
            Owner = Application.Current.MainWindow
        };
        win.ShowDialog();
        SaveSettings();
    }

    // ---- Process list ---------------------------------------------------

    public void RefreshProcesses(IEnumerable<string>? preselected = null)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Processes.Where(p => p.IsSelected))
            selected.Add(p.Name);

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
                    Icon        = ProcessHelper.GetIcon(path),
                };
                entry.PropertyChanged += (_, _) =>
                    OnPropertyChanged(nameof(SelectedProcessCount));
                Processes.Add(entry);
            }
            OnPropertyChanged(nameof(SelectedProcessCount));
        });
    }

    // ---- Log ------------------------------------------------------------

    private void AddLog(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (string.IsNullOrEmpty(line)) return;
            _allLogLines.Add(line);
            if (_allLogLines.Count > 500) _allLogLines.RemoveAt(0);

            // Добавляем в видимый список только если проходит фильтр
            if (string.IsNullOrEmpty(_logFilter) ||
                line.Contains(_logFilter, StringComparison.OrdinalIgnoreCase))
            {
                LogLines.Add(line);
                if (LogLines.Count > 500) LogLines.RemoveAt(0);
            }
        });
    }

    // ---- Helpers --------------------------------------------------------

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

/// <summary>Simple ICommand implementation.</summary>
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
