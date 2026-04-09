using System.Net;
using System.Net.Sockets;
using ProxyMaster.Models;

namespace ProxyMaster.Core;

/// <summary>
/// Локальный TCP-сервер, принимающий перенаправленные WinDivert-ом соединения.
/// Для каждого входящего соединения:
///   1. Смотрит исходный порт → ищет оригинальный dst в ConnectionTracker
///   2. Подключается к прокси (SOCKS5 или HTTP CONNECT)
///   3. Пайпит данные в обе стороны
/// </summary>
internal sealed class TransparentProxyServer : IDisposable
{
    private readonly ConnectionTracker _tracker;
    private readonly ushort _listenPort;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    // Максимум одновременных соединений (защита от DoS)
    private int _activeCount;
    private const int MaxConcurrentConnections = 200;

    public ProxyConfig? Config { get; set; }
    public event Action<string>? LogMessage;
    public event Action<long>? BytesTransferred;

    private long _totalBytes;
    public long TotalBytes => _totalBytes;

    public TransparentProxyServer(ConnectionTracker tracker, ushort listenPort)
    {
        _tracker = tracker;
        _listenPort = listenPort;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, _listenPort);
        _listener.Start(backlog: 512);
        _acceptTask = AcceptLoopAsync(_cts.Token);
        LogMessage?.Invoke($"Прозрачный прокси слушает на 127.0.0.1:{_listenPort}");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _acceptTask?.Wait(3000);
        LogMessage?.Invoke("Прозрачный прокси остановлен");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch { continue; }

            // Обрабатываем каждое соединение независимо
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        client.NoDelay = true;
        using var _ = client;

        // --- Защита: лимит одновременных соединений ---
        if (Interlocked.Increment(ref _activeCount) > MaxConcurrentConnections)
        {
            Interlocked.Decrement(ref _activeCount);
            LogMessage?.Invoke("[SEC] Отказ: превышен лимит соединений");
            return;
        }

        try
        {
            await HandleClientInternalAsync(client, ct);
        }
        finally
        {
            Interlocked.Decrement(ref _activeCount);
        }
    }

    private async Task HandleClientInternalAsync(TcpClient client, CancellationToken ct)
    {
        var cfg = Config;
        if (cfg == null) return;

        // Принимаем соединения с loopback (системный прокси) и с реального IP машины (WinDivert-путь)
        var remoteEp = client.Client.RemoteEndPoint as IPEndPoint;
        if (remoteEp == null) return;

        ushort srcPort    = (ushort)remoteEp.Port;
        var localStream   = client.GetStream();
        string targetHost = "?";
        int    targetPort = 0;

        // Определяем режим: HTTP CONNECT, plain HTTP proxy или WinDivert (прозрачный)
        var (isHttpConnect, isPlainHttp, peekedBytes) = await PeekIsHttpConnect(localStream, ct);

        byte[]? plainHttpRequestBytes = null;

        if (isHttpConnect)
        {
            // --- Режим системного прокси: браузер шлёт HTTP CONNECT ---
            var (host, port, ok) = await ReadHttpConnectHeader(localStream, peekedBytes, ct);
            if (!ok)
            {
                LogMessage?.Invoke($"[WARN] CONNECT parse failed, port {srcPort}");
                return;
            }

            // --- Защита от SSRF: запрещаем туннелировать к localhost ---
            if (!IsTargetAllowed(host, port))
            {
                LogMessage?.Invoke($"[SEC] CONNECT к {host}:{port} заблокирован (loopback/invalid)");
                byte[] deny = System.Text.Encoding.ASCII.GetBytes(
                    "HTTP/1.1 403 Forbidden\r\n\r\n");
                await localStream.WriteAsync(deny, ct);
                return;
            }

            targetHost = host;
            targetPort = port;
        }
        else if (isPlainHttp)
        {
            // --- Режим plain HTTP proxy: GET http://host/path HTTP/1.1 ---
            var (host, port, reqBytes, ok) = await ReadPlainHttpRequest(localStream, peekedBytes, ct);
            if (!ok)
            {
                LogMessage?.Invoke($"[WARN] Plain HTTP parse failed, port {srcPort}");
                return;
            }
            if (!IsTargetAllowed(host, port))
            {
                LogMessage?.Invoke($"[SEC] Plain HTTP к {host}:{port} заблокирован");
                return;
            }
            targetHost = host;
            targetPort = port;
            plainHttpRequestBytes = reqBytes;
            LogMessage?.Invoke($"[HTTP] {targetHost}:{targetPort}");
        }
        else
        {
            // --- Режим WinDivert: ищем dst по srcPort ---
            if (!_tracker.TryGet(srcPort, out var originalDst) || originalDst == null)
            {
                string ascii = peekedBytes.Length > 0
                    ? new string(peekedBytes.Select(b => b >= 32 && b < 127 ? (char)b : '.').ToArray())
                    : "(empty)";
                LogMessage?.Invoke($"[WARN] port {srcPort}: \"{ascii}\"");
                return;
            }
            targetHost = originalDst.Address.ToString();
            targetPort = originalDst.Port;
            LogMessage?.Invoke($"[WD] {targetHost}:{targetPort}");
        }

        NetworkStream? upstreamStream = null;
        // Таймаут на установку соединения с прокси (SOCKS5/HTTP CONNECT)
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            upstreamStream = cfg.Type switch
            {
                ProxyType.Socks5 => await Socks5Client.ConnectAsync(
                    cfg.Host, cfg.Port, targetHost, targetPort,
                    cfg.Username, cfg.Password, connectCts.Token),

                ProxyType.Http => await HttpProxyClient.ConnectAsync(
                    cfg.Host, cfg.Port, targetHost, targetPort,
                    cfg.Username, cfg.Password, connectCts.Token),

                _ => throw new NotSupportedException()
            };

            // Для HTTP CONNECT браузер ждёт подтверждения "200 Connection established"
            if (isHttpConnect)
            {
                byte[] ok200 = System.Text.Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 Connection established\r\n\r\n");
                await localStream.WriteAsync(ok200, ct);
            }
            else if (isPlainHttp)
            {
                // Plain HTTP proxy: шлём весь запрос как есть upstream
                await upstreamStream.WriteAsync(plainHttpRequestBytes, ct);
            }
            else if (peekedBytes.Length > 0)
            {
                // WinDivert-путь: первые N байт (начало TLS ClientHello и т.п.)
                // были прочитаны при peek — отправляем их upstream прежде чем начать pipe
                await upstreamStream.WriteAsync(peekedBytes, ct);
            }

            LogMessage?.Invoke($"[→] {targetHost}:{targetPort} via {cfg.Type}");

            // Пайпим трафик в обе стороны параллельно
            using var upStream = upstreamStream;
            await PipeAsync(localStream, upStream, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Сработал 10-секундный таймаут (не остановка сервера)
            LogMessage?.Invoke($"[TO] {targetHost}:{targetPort} — timeout 10s");
        }
        catch (OperationCanceledException) { /* нормальная остановка */ }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[ERR] {targetHost}:{targetPort} — {ex.Message}");
        }
        finally
        {
            upstreamStream?.Dispose();
            _tracker.Remove(srcPort);
        }
    }

    /// <summary>
    /// Двунаправленная передача данных между двумя потоками.
    /// </summary>
    private async Task PipeAsync(NetworkStream a, NetworkStream b, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

        Task t1 = CopyAsync(a, b, linked);
        Task t2 = CopyAsync(b, a, linked);

        await Task.WhenAny(t1, t2);
        linked.Cancel(); // останавливаем вторую сторону
        await Task.WhenAll(t1, t2).ConfigureAwait(false);
    }

    private async Task CopyAsync(NetworkStream src, NetworkStream dst,
                                  CancellationTokenSource cts)
    {
        byte[] buf = new byte[81920]; // 80 KB буфер
        try
        {
            int read;
            while ((read = await src.ReadAsync(buf, cts.Token)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, read), cts.Token);
                Interlocked.Add(ref _totalBytes, read);
                BytesTransferred?.Invoke(read);
            }
        }
        catch { /* соединение закрыто с одной из сторон */ }
        finally { cts.Cancel(); }
    }

    // ---- Безопасность ----

    /// <summary>
    /// Проверяет, разрешено ли туннелировать соединение к указанному хосту.
    /// Блокируем: loopback-адреса (SSRF), невалидные порты.
    /// </summary>
    private static bool IsTargetAllowed(string host, int port)
    {
        if (port <= 0 || port > 65535) return false;

        // Блокируем "localhost" и любые варианты написания
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return false;
        if (host.Equals("127.0.0.1"))  return false;
        if (host.Equals("::1"))        return false;
        if (host.StartsWith("127."))   return false;

        // Блокируем IP-адреса loopback-диапазона
        if (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip)) return false;

        return true;
    }

    // ---- HTTP CONNECT helpers ----

    /// <summary>
    /// Читает первые 7 байт и определяет режим соединения:
    /// HTTP CONNECT, plain HTTP proxy (GET/POST/...) или WinDivert.
    /// Возвращает прочитанные байты, чтобы они не были потеряны.
    /// </summary>
    private static async Task<(bool isConnect, bool isPlainHttp, byte[] peeked)> PeekIsHttpConnect(
        NetworkStream s, CancellationToken ct)
    {
        byte[] peekedBuf = new byte[7];
        int bytesRead = 0;
        try { bytesRead = await s.ReadAsync(peekedBuf.AsMemory(0, 7), ct); }
        catch { return (false, false, Array.Empty<byte>()); }

        byte[] peeked = peekedBuf[..bytesRead];
        bool isConnect = bytesRead >= 7 &&
            peekedBuf[0] == 'C' && peekedBuf[1] == 'O' && peekedBuf[2] == 'N' &&
            peekedBuf[3] == 'N' && peekedBuf[4] == 'E' && peekedBuf[5] == 'C' && peekedBuf[6] == 'T';

        // Plain HTTP proxy: GET, POST, PUT, DELETE, HEAD, OPTIONS, PATCH
        bool isPlainHttp = !isConnect && bytesRead >= 3 &&
            (peekedBuf[0] == 'G' || peekedBuf[0] == 'P' || peekedBuf[0] == 'D' ||
             peekedBuf[0] == 'H' || peekedBuf[0] == 'O');

        return (isConnect, isPlainHttp, peeked);
    }

    /// <summary>
    /// Читает полный plain HTTP запрос и извлекает целевой хост и порт.
    /// Возвращает все прочитанные байты для последующей отправки upstream.
    /// </summary>
    private static async Task<(string host, int port, byte[] requestBytes, bool ok)>
        ReadPlainHttpRequest(NetworkStream s, byte[] peeked, CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(System.Text.Encoding.ASCII.GetString(peeked));

        try
        {
            byte[] buf = new byte[1];
            while (!sb.ToString().Contains("\r\n\r\n"))
            {
                int n = await s.ReadAsync(buf, ct);
                if (n == 0) return ("", 0, Array.Empty<byte>(), false);
                sb.Append((char)buf[0]);
                if (sb.Length > 8192) return ("", 0, Array.Empty<byte>(), false);
            }
        }
        catch { return ("", 0, Array.Empty<byte>(), false); }

        // Парсим первую строку: "GET http://host[:port]/path HTTP/1.1"
        string firstLine = sb.ToString().Split('\n')[0].Trim();
        var parts = firstLine.Split(' ');
        if (parts.Length < 2) return ("", 0, Array.Empty<byte>(), false);

        string url = parts[1];
        string hostPart;
        int port;

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            string rest = url.Substring(7);
            int slashIdx = rest.IndexOf('/');
            string hostPortStr = slashIdx >= 0 ? rest[..slashIdx] : rest;
            (hostPart, port) = ParseHostPort(hostPortStr, 80);
        }
        else
        {
            // Берём из заголовка Host:
            string headers = sb.ToString();
            int hostIdx = headers.IndexOf("Host:", StringComparison.OrdinalIgnoreCase);
            if (hostIdx < 0) return ("", 0, Array.Empty<byte>(), false);
            int eol = headers.IndexOf('\n', hostIdx);
            string hostLine = (eol >= 0
                ? headers.Substring(hostIdx + 5, eol - hostIdx - 5)
                : headers[(hostIdx + 5)..]).Trim().TrimEnd('\r');
            (hostPart, port) = ParseHostPort(hostLine, 80);
        }

        byte[] requestBytes = System.Text.Encoding.ASCII.GetBytes(sb.ToString());
        return (hostPart, port, requestBytes, true);
    }

    // Вспомогательный метод: извлекает host и port из строки вида "host:port" или "host"
    private static (string host, int port) ParseHostPort(string hostPort, int defaultPort)
    {
        int colonIdx = hostPort.LastIndexOf(':');
        if (colonIdx >= 0 && int.TryParse(hostPort[(colonIdx + 1)..], out int p))
            return (hostPort[..colonIdx], p);
        return (hostPort, defaultPort);
    }

    private static async Task<(string host, int port, bool ok)> ReadHttpConnectHeader(
        NetworkStream s, byte[] peeked, CancellationToken ct)
    {
        // Начинаем с уже прочитанных байт (первые 7 = "CONNECT")
        var sb = new System.Text.StringBuilder();
        sb.Append(System.Text.Encoding.ASCII.GetString(peeked));

        try
        {
            byte[] buf = new byte[1];
            while (!sb.ToString().Contains("\r\n\r\n"))
            {
                int n = await s.ReadAsync(buf, ct);
                if (n == 0) return ("", 0, false);
                sb.Append((char)buf[0]);
                if (sb.Length > 4096) return ("", 0, false);
            }
        }
        catch { return ("", 0, false); }

        // Парсим первую строку: "CONNECT host:port HTTP/1.1"
        string firstLine = sb.ToString().Split('\n')[0].Trim();
        var parts = firstLine.Split(' ');
        if (parts.Length < 2) return ("", 0, false);

        var hostPort = parts[1].Split(':');
        if (hostPort.Length != 2) return ("", 0, false);
        if (!int.TryParse(hostPort[1], out int port)) return ("", 0, false);

        return (hostPort[0], port, true);
    }

    public void Dispose() => Stop();
}
