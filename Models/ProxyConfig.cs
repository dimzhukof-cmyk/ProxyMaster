using System.Text.Json.Serialization;

namespace ProxyMaster.Models;

public enum ProxyType { Socks5, Http }

/// <summary>
/// Настройки прокси-сервера.
/// </summary>
public class ProxyConfig
{
    public ProxyType Type     { get; set; } = ProxyType.Socks5;
    public string    Host     { get; set; } = "127.0.0.1";
    public int       Port     { get; set; } = 1080;
    public string?   Username { get; set; }

    /// <summary>Пароль в открытом виде — НЕ сохраняется на диск.</summary>
    [JsonIgnore]
    public string?   Password { get; set; }

    /// <summary>Пароль, зашифрованный через DPAPI — именно это пишется в settings.json.</summary>
    public string?   PasswordProtected { get; set; }

    [JsonIgnore]
    public string DisplayName => $"{Type} {Host}:{Port}";
}

/// <summary>
/// Правило: направлять трафик через прокси или напрямую.
/// В текущей версии реализован режим «весь трафик через один прокси».
/// Правила — задел для v2 (per-app routing).
/// </summary>
public class ProxyRule
{
    public string  Name      { get; set; } = "Default";
    public bool    Enabled   { get; set; } = true;

    /// <summary>null = все приложения, иначе имя процесса (e.g. "chrome.exe")</summary>
    public string? ProcessName { get; set; }

    /// <summary>null = все адреса</summary>
    public string? TargetHost { get; set; }

    public RuleAction Action { get; set; } = RuleAction.Proxy;
}

public enum RuleAction { Proxy, Direct, Block }

/// <summary>
/// Полный файл настроек, сохраняемый на диск (JSON).
/// </summary>
public class AppSettings
{
    public ProxyConfig     Proxy            { get; set; } = new();
    public List<ProxyRule> Rules            { get; set; } = new();
    public ushort          LocalProxyPort   { get; set; } = 8877;
    public bool            FilterByProcess  { get; set; } = false;
    public List<string>    SelectedProcesses{ get; set; } = new();
}
