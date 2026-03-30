using System.Net;
using System.Runtime.InteropServices;

namespace ProxyMaster.Core;

/// <summary>
/// Перехватывает TCP через WinDivert с помощью двух хэндлов:
///
///   Handle A — исходящий (outbound, не loopback):
///     SYN  → записываем srcPort→originalDst, меняем dst на 127.0.0.1:proxyPort
///     Data → для уже отслеживаемых соединений меняем dst на 127.0.0.1:proxyPort
///
///   Handle B — входящий от нашего локального прокси (loopback, src == 127.0.0.1:proxyPort):
///     * → восстанавливаем src из таблицы (так OS видит ответ от originalDst)
///
/// Без Handle B OS отбрасывает SYN-ACK: соединение не устанавливается.
/// </summary>
internal sealed class PacketInterceptor : IDisposable
{
    private readonly ConnectionTracker _tracker;
    private readonly ushort _localProxyPort;
    private readonly int    _proxyPort;
    private readonly byte[]? _proxyIpBytes;

    private IntPtr _handleOut = WinDivert.INVALID_HANDLE;
    private IntPtr _handleIn  = WinDivert.INVALID_HANDLE;

    private Thread? _threadOut;
    private Thread? _threadIn;
    private volatile bool _running;

    /// <summary>
    /// Если не null — пропускаем через прокси только процессы с именами из этого набора.
    /// null = все процессы.
    /// </summary>
    public IReadOnlySet<string>? AllowedProcessNames { get; set; }

    // Кэш PID → имя процесса чтобы не вызывать Process.GetProcessById на каждый пакет
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, string?> _pidCache = new();

    public event Action<string>? LogMessage;

    public PacketInterceptor(ConnectionTracker tracker, ushort localProxyPort,
                             string proxyHost, int proxyPort)
    {
        _tracker        = tracker;
        _localProxyPort = localProxyPort;
        _proxyPort      = proxyPort;

        // Резолвим IP прокси один раз при старте
        if (IPAddress.TryParse(proxyHost, out var ip))
            _proxyIpBytes = ip.GetAddressBytes();
        else
        {
            try { _proxyIpBytes = Dns.GetHostAddresses(proxyHost).FirstOrDefault()?.GetAddressBytes(); }
            catch { _proxyIpBytes = null; }
        }
    }

    // ---- Запуск / Остановка ----

    public void Start()
    {
        if (_running) return;

        // Исключаем loopback на уровне фильтра; прокси-сервер исключаем в коде
        string filterOut = "outbound and tcp and ip.DstAddr != 127.0.0.1";

        // Handle B: входящий TCP от нашего локального прокси
        string filterIn = $"inbound and tcp and tcp.SrcPort == {_localProxyPort}";

        _handleOut = WinDivert.WinDivertOpen(filterOut, WinDivert.LAYER_NETWORK, -100, WinDivert.FLAG_DEFAULT);
        if (_handleOut == WinDivert.INVALID_HANDLE)
            throw new InvalidOperationException($"WinDivertOpen (outbound) failed: {Marshal.GetLastWin32Error()}. Запустите от администратора.");

        _handleIn = WinDivert.WinDivertOpen(filterIn, WinDivert.LAYER_NETWORK, -101, WinDivert.FLAG_DEFAULT);
        if (_handleIn == WinDivert.INVALID_HANDLE)
        {
            WinDivert.WinDivertClose(_handleOut);
            _handleOut = WinDivert.INVALID_HANDLE;
            throw new InvalidOperationException($"WinDivertOpen (inbound) failed: {Marshal.GetLastWin32Error()}");
        }

        _running = true;

        _threadOut = new Thread(() => CaptureLoop(_handleOut, ProcessOutbound))
            { IsBackground = true, Name = "WinDivert-Out" };
        _threadIn  = new Thread(() => CaptureLoop(_handleIn, ProcessInbound))
            { IsBackground = true, Name = "WinDivert-In" };

        _threadOut.Start();
        _threadIn.Start();

        LogMessage?.Invoke($"WinDivert: два хэндла открыты (порт {_localProxyPort})");
    }

    public void Stop()
    {
        _running = false;
        CloseHandle(ref _handleOut);
        CloseHandle(ref _handleIn);
        _threadOut?.Join(2000);
        _threadIn?.Join(2000);
        _pidCache.Clear();
        LogMessage?.Invoke("WinDivert: остановлен");
    }

    private static void CloseHandle(ref IntPtr h)
    {
        if (h != WinDivert.INVALID_HANDLE)
        {
            WinDivert.WinDivertClose(h);
            h = WinDivert.INVALID_HANDLE;
        }
    }

    // ---- Цикл захвата (общий для обоих хэндлов) ----

    private void CaptureLoop(IntPtr handle, Func<byte[], int, bool> processor)
    {
        const int BufSize = 65535;
        byte[] buf = new byte[BufSize];
        WinDivertAddress addr = new();

        while (_running)
        {
            if (!WinDivert.WinDivertRecv(handle, buf, BufSize, out uint len, ref addr))
                break;

            bool modified = processor(buf, (int)len);
            if (modified)
                WinDivert.WinDivertHelperCalcChecksums(buf, len, ref addr, 0);

            if (handle != WinDivert.INVALID_HANDLE)
                WinDivert.WinDivertSend(handle, buf, len, out _, ref addr);
        }
    }

    // ---- Обработка исходящих пакетов (Handle A) ----

    private bool ProcessOutbound(byte[] buf, int len)
    {
        if (!ParseIpTcp(buf, len, out _, out int tcpOff,
                        out _, out int dstIpOff,
                        out int tcpSrcPort, out int tcpDstPort, out byte flags))
            return false;

        // Пропускаем трафик к самому прокси-серверу (иначе будет петля)
        if (_proxyIpBytes != null && tcpDstPort == _proxyPort)
        {
            if (buf[dstIpOff]   == _proxyIpBytes[0] &&
                buf[dstIpOff+1] == _proxyIpBytes[1] &&
                buf[dstIpOff+2] == _proxyIpBytes[2] &&
                buf[dstIpOff+3] == _proxyIpBytes[3])
                return false;
        }

        bool isSyn = (flags & 0x02) != 0;
        bool isAck = (flags & 0x10) != 0;

        if (isSyn && !isAck)
        {
            // Фильтр по приложению (если включён)
            if (AllowedProcessNames != null)
            {
                int? pid = ProcessHelper.GetProcessIdByLocalPort((ushort)tcpSrcPort);
                if (pid == null) return false;

                string? procName = _pidCache.GetOrAdd(pid.Value,
                    id => ProcessHelper.GetProcessNameById(id));
                if (procName == null || !AllowedProcessNames.Contains(procName))
                    return false;
            }

            // Новое соединение — запоминаем оригинальный dst
            byte[] dstIp = new byte[4];
            Buffer.BlockCopy(buf, dstIpOff, dstIp, 0, 4);
            int dstPort = ReadU16BE(buf, tcpOff + 2);
            _tracker.Add((ushort)tcpSrcPort, new IPEndPoint(new IPAddress(dstIp), dstPort));
        }
        else if (!_tracker.TryGet((ushort)tcpSrcPort, out _))
        {
            return false; // не отслеживаемое соединение
        }

        // Перенаправляем на 127.0.0.1:localProxyPort
        buf[dstIpOff]   = 127; buf[dstIpOff+1] = 0;
        buf[dstIpOff+2] = 0;   buf[dstIpOff+3] = 1;
        WriteU16BE(buf, tcpOff + 2, _localProxyPort);
        return true;
    }

    // ---- Обработка входящих от прокси (Handle B) ----

    private bool ProcessInbound(byte[] buf, int len)
    {
        if (!ParseIpTcp(buf, len, out _, out int tcpOff,
                        out int srcIpOff, out _,
                        out _, out int tcpDstPort, out _))
            return false;

        ushort appPort = (ushort)tcpDstPort;
        if (!_tracker.TryGet(appPort, out var originalDst) || originalDst == null)
            return false;

        // Подменяем src на оригинальный сервер
        byte[] origIp = originalDst.Address.GetAddressBytes();
        Buffer.BlockCopy(origIp, 0, buf, srcIpOff, 4);
        WriteU16BE(buf, tcpOff, (ushort)originalDst.Port);
        return true;
    }

    // ---- Парсинг IPv4 + TCP заголовков ----

    private static bool ParseIpTcp(
        byte[] buf, int len,
        out int ipHdrLen, out int tcpOff,
        out int srcIpOff, out int dstIpOff,
        out int tcpSrcPort, out int tcpDstPort, out byte tcpFlags)
    {
        ipHdrLen = tcpOff = srcIpOff = dstIpOff = tcpSrcPort = tcpDstPort = 0;
        tcpFlags = 0;

        if (len < 40) return false;
        if ((buf[0] >> 4) != 4) return false; // только IPv4
        if (buf[9] != 6) return false;         // только TCP

        ipHdrLen   = (buf[0] & 0x0F) * 4;
        srcIpOff   = 12;
        dstIpOff   = 16;
        tcpOff     = ipHdrLen;

        if (len < tcpOff + 20) return false;

        tcpSrcPort = ReadU16BE(buf, tcpOff);
        tcpDstPort = ReadU16BE(buf, tcpOff + 2);
        tcpFlags   = buf[tcpOff + 13];
        return true;
    }

    private static int    ReadU16BE(byte[] b, int o) => (b[o] << 8) | b[o + 1];
    private static void WriteU16BE(byte[] b, int o, ushort v)
        { b[o] = (byte)(v >> 8); b[o+1] = (byte)(v & 0xFF); }

    public void Dispose() => Stop();
}
