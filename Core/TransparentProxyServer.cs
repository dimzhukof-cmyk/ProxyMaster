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

        // --- Защита: принимаем только соединения с loopback ---
        var remoteEp = client.Client.RemoteEndPoint as IPEndPoint;
        if (remoteEp == null || !IPAddress.IsLoopback(remoteEp.Address))
        {
            LogMessage?.Invoke($"[SEC] Отказ: не localhost ({remoteEp?.Address})");
            return;
        }

        ushort srcPort    = (ushort)remoteEp.Port;
        var localStream   = client.GetStream();
        string targetHost;
        int    targetPort;

        // Определяем режим: HTTP CONNECT (системный прокси) или WinDivert (прозрачный)
        bool isHttpConnect = await PeekIsHttpConnect(localStream, ct);

        if (isHttpConnect)
        {
            // --- Режим системного прокси: браузер шлёт HTTP CONNECT ---
            var (host, port, ok) = await ReadHttpConnectHeader(localStream, ct);
            if (!ok) return;

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
        else
        {
            // --- Режим WinDivert: ищем dst по srcPort ---
            if (!_tracker.TryGet(srcPort, out var originalDst) || originalDst == null)
            {
                LogMessage?.Invoke($"[WARN] Нет записи для порта {srcPort}");
                return;
            }
            targetHost = originalDst.Address.ToString();
            targetPort = originalDst.Port;
        }

        NetworkStream? upstreamStream = null;
        try
        {
            upstreamStream = cfg.Type switch
            {
                ProxyType.Socks5 => await Socks5Client.ConnectAsync(
                    cfg.Host, cfg.Port, targetHost, targetPort,
                    cfg.Username, cfg.Password, ct),

                ProxyType.Http => await HttpProxyClient.ConnectAsync(
                    cfg.Host, cfg.Port, targetHost, targetPort,
                    cfg.Username, cfg.Password, ct),

                _ => throw new NotSupportedException()
            };

            // Для HTTP CONNECT браузер ждёт подтверждения "200 Connection established"
            if (isHttpConnect)
            {
                byte[] ok200 = System.Text.Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 Connection established\r\n\r\n");
                await localStream.WriteAsync(ok200, ct);
            }

            LogMessage?.Invoke($"[→] {targetHost}:{targetPort} via {cfg.Type}");

            // Пайпим трафик в обе стороны параллельно
            using var upStream = upstreamStream;
            await PipeAsync(localStream, upStream, ct);
        }
        catch (OperationCanceledException) { }
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

    private static async Task<bool> PeekIsHttpConnect(NetworkStream s, CancellationToken ct)
    {
        // Читаем первые 7 байт без извлечения (peek через буфер)
        byte[] peek = new byte[7];
        try
        {
            int n = await s.ReadAsync(peek.AsMemory(0, 7), ct);
            // Кладём обратно в _peekBuf для последующего чтения
            // (NetworkStream не поддерживает peek, используем обёртку)
            Array.Resize(ref peek, n);
        }
        catch { return false; }

        _peekBuf = peek;
        return peek.Length >= 7 &&
               peek[0] == 'C' && peek[1] == 'O' && peek[2] == 'N' &&
               peek[3] == 'N' && peek[4] == 'E' && peek[5] == 'C' && peek[6] == 'T';
    }

    [ThreadStatic] private static byte[]? _peekBuf;

    private async Task<(string host, int port, bool ok)> ReadHttpConnectHeader(
        NetworkStream s, CancellationToken ct)
    {
        // Читаем заголовок до \r\n\r\n
        var sb = new System.Text.StringBuilder();
        if (_peekBuf != null)
            sb.Append(System.Text.Encoding.ASCII.GetString(_peekBuf));

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
