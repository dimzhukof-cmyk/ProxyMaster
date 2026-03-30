# ProxyMaster

> A Proxifier-like transparent proxy client for Windows 11/10.
> Routes all (or selected) network traffic through a SOCKS5 or HTTP proxy — no per-app configuration required.

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/license-MIT-green)
![Requires Admin](https://img.shields.io/badge/requires-Administrator-red)

---

## Features

- **System-wide proxy** — intercepts all outbound TCP traffic at the kernel level via [WinDivert](https://reqrypt.org/windivert.html)
- **Per-app routing** — select specific processes from a running list; only their traffic goes through the proxy
- **SOCKS5 & HTTP CONNECT** — full support for both proxy protocols, with optional username/password auth
- **Browser compatibility** — sets Windows system proxy (WinInet) so browsers work automatically in "all apps" mode
- **Secure password storage** — proxy password is encrypted with Windows DPAPI (per-user key), never stored in plain text
- **Dark UI** — clean WPF interface with live traffic log and byte counter
- **SSRF protection** — HTTP CONNECT tunneling to loopback addresses (127.x.x.x) is blocked

---

## Screenshots

```
┌─────────────────────────────────────────────────────────────┐
│ ● ProxyMaster — системный прокси для Windows    АКТИВЕН    │
├─────────────────────────────────────────────────────────────┤
│ Тип     Адрес сервера        Порт  Логин    Пароль          │
│ SOCKS5  191.102.172.122      9843  user     ••••••  [Save]  │
├─────────────────────────────────────────────────────────────┤
│ Traffic: ● All apps   ○ Selected only                       │
├─────────────────────────────────────────────────────────────┤
│ [11:42:01] ProxyMaster started                             │
│ [11:42:01] System proxy → 127.0.0.1:8877                   │
│ [11:42:03] [→] google.com:443 via Socks5                   │
└─────────────────────────────────────────────────────────────┘
```

---

## Requirements

| Requirement | Details |
|---|---|
| OS | Windows 10 or Windows 11 (x64) |
| Runtime | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Privileges | **Administrator** (required by WinDivert) |
| Driver | WinDivert 2.2 (included in the release archive) |

---

## Installation

1. Download the latest release archive: `ProxyMaster-x.x.x-win-x64.zip`
2. Extract to any folder
3. Right-click `ProxyMaster.exe` → **Run as administrator**

> **No installer needed.** Settings are saved to `%AppData%\ProxyMaster\settings.json`.

---

## Usage

### All traffic mode (default)
1. Enter your proxy server address, port, and credentials
2. Click **Save**
3. Click **▶ START**

ProxyMaster sets the Windows system proxy and activates WinDivert — all TCP traffic is routed through your proxy.

### Per-app mode
1. Switch to **Selected only**
2. Click **↻ Refresh list** — all running processes appear
3. Check the apps you want to proxy (e.g. `chrome.exe`, `telegram.exe`)
4. Click **▶ START**

Only checked apps are intercepted. System proxy is not set — other apps connect directly.

### Stopping
Click **■ STOP**. WinDivert is unloaded and the system proxy is restored to its previous state.

---

## How It Works

```
App (e.g. Chrome) ──SYN──► WinDivert (kernel)
                                │
                    rewrites dst → 127.0.0.1:8877
                                │
                    TransparentProxyServer (local TCP)
                                │
                    SOCKS5 / HTTP CONNECT handshake
                                │
                    Upstream proxy server (e.g. 1.2.3.4:1080)
                                │
                          Internet 🌐
```

- **WinDivert** captures outbound SYN packets and rewrites the destination to the local listener
- **ConnectionTracker** maps source ports to original destinations
- **TransparentProxyServer** looks up the original destination and tunnels the connection via SOCKS5/HTTP
- **SystemProxy** sets WinInet system proxy for browsers in "all apps" mode

---

## Security

| Concern | Mitigation |
|---|---|
| Password in settings file | Encrypted with Windows DPAPI (`ProtectedData`, per-user scope) |
| SSRF via HTTP CONNECT | `CONNECT` requests to `127.*`, `localhost`, `::1` return `403 Forbidden` |
| Unauthorized local access | Proxy listener bound to `127.0.0.1` only; non-loopback connections rejected |
| DoS / connection flood | Hard limit of 200 concurrent connections |

---

## Building from Source

```powershell
# Requirements: .NET 8 SDK, WinDivert 2.2 DLLs in project root
git clone https://github.com/dimzhukof-cmyk/ProxyMaster.git
cd ProxyMaster

# Copy WinDivert files to project root
# WinDivert.dll, WinDivert64.sys → C:\...\ProxyMaster\

dotnet build -c Release
# Output: bin\Release\net8.0-windows\ProxyMaster.exe
```

---

## Known Limitations

- **TCP only** — UDP traffic is not intercepted (most apps use TCP)
- **IPv4 only** — IPv6 interception is not implemented
- **Administrator required** — WinDivert cannot function without elevated privileges
- **Antivirus alerts** — some AV products flag WinDivert as suspicious (it's a legitimate kernel driver used by many tools including Wireshark alternatives)

---

## License

MIT © 2025 — see [LICENSE](LICENSE)

---

*[Читать на русском →](README.ru.md)*
