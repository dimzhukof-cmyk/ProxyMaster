using System.Runtime.InteropServices;

namespace ProxyMaster.Core;

/// <summary>
/// Устанавливает / снимает системный HTTP-прокси Windows (WinInet).
/// Покрывает браузеры (Chrome, Opera, Edge, IE) и большинство приложений,
/// которые уважают настройки IE/WinInet.
/// </summary>
internal static class SystemProxy
{
    private const int INTERNET_OPTION_SETTINGS_CHANGED   = 39;
    private const int INTERNET_OPTION_REFRESH             = 37;
    private const string RegPath =
        @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(
        IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    /// <summary>Включить системный прокси на адрес host:port.</summary>
    public static void Enable(string host, int port)
    {
        string proxyAddr = $"{host}:{port}";

        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegPath, writable: true);
        if (key == null) return;

        key.SetValue("ProxyEnable",  1,          Microsoft.Win32.RegistryValueKind.DWord);
        key.SetValue("ProxyServer",  proxyAddr,  Microsoft.Win32.RegistryValueKind.String);
        key.SetValue("ProxyOverride", "localhost;127.*;10.*;172.16.*;192.168.*;<local>",
                     Microsoft.Win32.RegistryValueKind.String);

        NotifyWindows();
    }

    /// <summary>Отключить системный прокси.</summary>
    public static void Disable()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegPath, writable: true);
        if (key == null) return;

        key.SetValue("ProxyEnable", 0, Microsoft.Win32.RegistryValueKind.DWord);
        NotifyWindows();
    }

    private static void NotifyWindows()
    {
        InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH,          IntPtr.Zero, 0);
    }
}
