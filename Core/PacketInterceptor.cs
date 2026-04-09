using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace ProxyMaster.Core;

/// <summary>
/// Перехватывает TCP через WinDivert с помощью двух хэндлов:
///
///   Handle A — исходящий (outbound, не loopback):
///     SYN  → сохраняем srcPort→{originalDst, clientIp}, меняем src→127.0.0.1, dst→127.0.0.1:proxyPort
///     Data → для отслеживаемых соединений меняем src→127.0.0.1, dst→127.0.0.1:proxyPort
///     При реинжекции: Loopback=true, IfIdx=loopback-адаптера
///
///   Handle B — ответы локального прокси (outbound, src == 127.0.0.1:proxyPort):
///     * → восстанавливаем src из таблицы (оригинальный сервер)
///           восстанавливаем dst из _clientIpCache (реальный IP NIC приложения)
///           реинжектируем как Outbound=true, IfIdx=0 (re-routing, local delivery)
///
/// Весь путь SYN → ProxyMaster pure loopback → SYN-ACK восстановлен и доставлен
/// приложению через локальную маршрутизацию от оригинального сервера.
/// </summary>
internal sealed class PacketInterceptor : IDisposable
{
    private const int  BufSize    = 65535;
    private const byte FlagSyn    = 0x02;
    private const byte FlagAck    = 0x10;
    private const byte FlagSynAck = 0x12;

    private readonly ConnectionTracker _tracker;
    private readonly ushort _localProxyPort;
    private readonly int    _proxyPort;
    private readonly byte[]? _proxyIpBytes;

    // Реальный IPv4-адрес машины (fallback, если _clientIpCache не содержит запись)
    private readonly byte[]? _localNicIp;

    // IfIdx физического NIC (для Handle B, inbound-реинжекция к приложению)
    // Обновляется из первого захваченного реального пакета (WinDivert-значение)
    private volatile uint _physicalNicIfIdx;

    // IfIdx loopback-адаптера (для Handle A, loopback-инжекция SYN к ProxyMaster)
    // WinDivert требует loopback-IfIdx при Loopback=true; значение 1 — типичный дефолт Windows
    private readonly uint _loopbackIfIdx;

    // Карта srcPort → IP-адрес NIC приложения (из SYN-пакета, src до изменения)
    // Используется Handle B для восстановления dst
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ushort, byte[]>
        _clientIpCache = new();

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

        // Находим первый физический NIC с IPv4 (не loopback)
        (_localNicIp, _physicalNicIfIdx) = GetPhysicalNicInfo();
        // IfIdx loopback-адаптера нужен Handle A для корректной loopback-инжекции
        _loopbackIfIdx = GetLoopbackIfIdx();
    }

    /// <summary>Windows IfIdx loopback-адаптера (обычно 1, но может отличаться).</summary>
    private static uint GetLoopbackIfIdx()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType != NetworkInterfaceType.Loopback) continue;
                try
                {
                    var v4 = nic.GetIPProperties().GetIPv4Properties();
                    if (v4 != null) return (uint)v4.Index;
                }
                catch { }
            }
        }
        catch { }
        return 1; // Windows loopback IfIdx по умолчанию
    }

    /// <summary>
    /// Возвращает IPv4-байты и Windows-интерфейсный индекс первого физического NIC.
    /// Использует вложенный try/catch — виртуальные и VPN-адаптеры могут бросать исключения.
    /// </summary>
    private static (byte[]? ip, uint ifIdx) GetPhysicalNicInfo()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (nic.OperationalStatus != OperationalStatus.Up) continue;

                try
                {
                    var ipProps = nic.GetIPProperties();
                    var unicast = ipProps.UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                                          && !IPAddress.IsLoopback(a.Address));
                    if (unicast == null) continue;

                    var v4 = ipProps.GetIPv4Properties();
                    if (v4 == null) continue;

                    return (unicast.Address.GetAddressBytes(), (uint)v4.Index);
                }
                catch { /* пропускаем проблемный адаптер */ }
            }
        }
        catch { }
        return (null, 0);
    }

    // ---- Запуск / Остановка ----

    public void Start()
    {
        if (_running) return;

        // Исключаем loopback на уровне фильтра; прокси-сервер исключаем в коде
        string filterOut = "outbound and tcp and ip.DstAddr != 127.0.0.1";

        // Handle B: ответы локального прокси клиентам (loopback = всегда outbound в WinDivert)
        string filterIn = $"outbound and tcp and tcp.SrcPort == {_localProxyPort}";

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

        _threadOut = new Thread(() => CaptureLoop(_handleOut, ProcessOutbound, flipToInbound: false))
            { IsBackground = true, Name = "WinDivert-Out" };
        _threadIn  = new Thread(() => CaptureLoop(_handleIn, ProcessInbound, flipToInbound: true))
            { IsBackground = true, Name = "WinDivert-In" };

        _threadOut.Start();
        _threadIn.Start();

        string nicInfo = _localNicIp != null
            ? $"{_localNicIp[0]}.{_localNicIp[1]}.{_localNicIp[2]}.{_localNicIp[3]} ifIdx={_physicalNicIfIdx}"
            : "auto (из первого пакета)";
        LogMessage?.Invoke($"WinDivert: открыты (порт {_localProxyPort}), phys ifIdx={_physicalNicIfIdx}, lo ifIdx={_loopbackIfIdx}, NIC: {nicInfo}");
    }

    public void Stop()
    {
        _running = false;
        CloseHandle(ref _handleOut);
        CloseHandle(ref _handleIn);
        _threadOut?.Join(2000);
        _threadIn?.Join(2000);
        _pidCache.Clear();
        _clientIpCache.Clear();
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

    private void CaptureLoop(IntPtr handle, Func<byte[], int, bool> processor, bool flipToInbound = false)
    {
        byte[] buf = new byte[BufSize];
        WinDivertAddress addr = new();

        while (_running)
        {
            if (!WinDivert.WinDivertRecv(handle, buf, BufSize, out uint len, ref addr))
            {
                int err = Marshal.GetLastWin32Error();
                if (_running)
                    LogMessage?.Invoke($"[WinDivert] CaptureLoop завершился неожиданно. Ошибка Win32: {err}");
                break;
            }

            // Обновляем IfIdx физического NIC из первого реального пакета Handle A.
            // Условие addr.IfIdx != _loopbackIfIdx критично: некоторые loopback-пакеты имеют
            // IsLoopback=false в WinDivert, но IfIdx=loopback-адаптера — без этой проверки
            // _physicalNicIfIdx мгновенно перезаписывается loopback-индексом и Handle B
            // реинжектирует SYN-ACK на loopback вместо физического NIC.
            if (!flipToInbound && _physicalNicIfIdx == 0
                && addr.IsOutbound && !addr.IsLoopback
                && addr.IfIdx != 0 && addr.IfIdx != _loopbackIfIdx)
                _physicalNicIfIdx = addr.IfIdx;

            bool modified = processor(buf, (int)len);
            if (modified)
            {
                WinDivert.WinDivertHelperCalcChecksums(buf, len, ref addr, 0);

                if (flipToInbound)
                {
                    // Handle B: реинжектируем как outbound с IfIdx=0 (force re-routing).
                    // dst=192.168.0.141 (свой IP) → routing доставляет локально (loopback delivery),
                    // минуя WFP stateful-проверку файрволла, которая отвергала inbound-инжекцию на NIC 8.
                    addr.SetOutbound(true);
                    addr.SetLoopback(false);
                    addr.IfIdx    = 0;
                    addr.SubIfIdx = 0;
                }
                else
                {
                    // Handle A: пакет теперь идёт на 127.0.0.1 — помечаем как loopback.
                    // WinDivert требует Loopback=true + IfIdx=loopback-адаптера (не физического NIC!).
                    // Если IfIdx останется физическим — инжекция на loopback не работает.
                    addr.SetLoopback(true);
                    addr.IfIdx    = _loopbackIfIdx;
                    addr.SubIfIdx = 0;
                }
            }

            if (handle != WinDivert.INVALID_HANDLE)
            {
                bool sent = WinDivert.WinDivertSend(handle, buf, len, out _, ref addr);
                if (!sent && modified && _running)
                {
                    int err = Marshal.GetLastWin32Error();
                    LogMessage?.Invoke($"[WinDivert] Send failed err={err} flip={flipToInbound} IfIdx={addr.IfIdx}");
                }
            }
        }
    }

    // ---- Обработка исходящих пакетов (Handle A) ----

    private bool ProcessOutbound(byte[] buf, int len)
    {
        if (!ParseIpTcp(buf, len, out _, out int tcpOff,
                        out int srcIpOff, out int dstIpOff,
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

        // Пропускаем трафик к приватным (RFC 1918) и link-local адресам —
        // локальная сеть должна работать напрямую, без прокси
        if (IsPrivateIp(buf, dstIpOff)) return false;

        bool isSyn = (flags & FlagSyn) != 0;
        bool isAck = (flags & FlagAck) != 0;

        if (isSyn && !isAck)
        {
            LogMessage?.Invoke($"[SYN] →{new System.Net.IPAddress(new byte[]{buf[dstIpOff],buf[dstIpOff+1],buf[dstIpOff+2],buf[dstIpOff+3]})}:{ReadU16BE(buf, tcpOff+2)}");

            // Фильтр по приложению (если включён режим Selected only)
            if (AllowedProcessNames != null)
            {
                int? pid = ProcessHelper.GetProcessIdByLocalPort((ushort)tcpSrcPort);
                if (pid == null) return false;

                string? procName = _pidCache.GetOrAdd(pid.Value,
                    id => ProcessHelper.GetProcessNameById(id));
                if (procName == null || !AllowedProcessNames.Contains(procName))
                    return false;
            }

            // Сохраняем оригинальный IP клиента (src до изменения) — Handle B использует его
            // для восстановления dst при реинжекции SYN-ACK обратно к приложению
            byte[] clientIp = new byte[4];
            Buffer.BlockCopy(buf, srcIpOff, clientIp, 0, 4);
            _clientIpCache[(ushort)tcpSrcPort] = clientIp;

            // Новое соединение — запоминаем оригинальный dst (до изменения)
            byte[] dstIp = new byte[4];
            Buffer.BlockCopy(buf, dstIpOff, dstIp, 0, 4);
            int dstPort = ReadU16BE(buf, tcpOff + 2);
            _tracker.Add((ushort)tcpSrcPort, new IPEndPoint(new IPAddress(dstIp), dstPort));
        }
        else if (!_tracker.TryGet((ushort)tcpSrcPort, out _))
        {
            return false; // не отслеживаемое соединение
        }

        // Делаем соединение чисто loopback: src и dst → 127.0.0.1
        // Без смены src Windows отбрасывает пакет с external src на loopback-интерфейсе
        WriteLoopbackIp(buf, srcIpOff);
        WriteLoopbackIp(buf, dstIpOff);
        WriteU16BE(buf, tcpOff + 2, _localProxyPort);
        return true;
    }

    // ---- Обработка входящих от прокси (Handle B) ----

    private bool ProcessInbound(byte[] buf, int len)
    {
        if (!ParseIpTcp(buf, len, out _, out int tcpOff,
                        out int srcIpOff, out int dstIpOff,
                        out _, out int tcpDstPort, out byte flags))
            return false;

        ushort appPort = (ushort)tcpDstPort;
        if (!_tracker.TryGet(appPort, out var originalDst) || originalDst == null)
            return false;

        bool isSynAck = IsSynAck(flags);
        if (isSynAck)
        {
            // Диагностика: показываем dst IP который будет вписан и IfIdx для реинжекции
            string dstDbg = _clientIpCache.TryGetValue(appPort, out var cip)
                ? $"{cip[0]}.{cip[1]}.{cip[2]}.{cip[3]}"
                : (_localNicIp != null
                   ? $"{_localNicIp[0]}.{_localNicIp[1]}.{_localNicIp[2]}.{_localNicIp[3]}(fb)"
                   : "null");
            LogMessage?.Invoke($"[HB] SYN-ACK port {appPort} dst→{dstDbg}");
        }

        // Восстанавливаем src: 127.0.0.1:proxyPort → оригинальный сервер
        byte[] origIp = originalDst.Address.GetAddressBytes();
        Buffer.BlockCopy(origIp, 0, buf, srcIpOff, 4);
        WriteU16BE(buf, tcpOff, (ushort)originalDst.Port);

        // Восстанавливаем dst: 127.0.0.1 → реальный IP NIC приложения
        // Используем IP сохранённый из SYN-пакета (точный IP для данного соединения)
        if (_clientIpCache.TryGetValue(appPort, out var clientIp))
            Buffer.BlockCopy(clientIp, 0, buf, dstIpOff, 4);
        else if (_localNicIp != null)
            Buffer.BlockCopy(_localNicIp, 0, buf, dstIpOff, 4);
        // Если ни того ни другого — dst остаётся 127.0.0.1 и до приложения не дойдёт

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

    private static bool IsSynAck(byte flags) => (flags & FlagSynAck) == FlagSynAck;

    /// <summary>
    /// Возвращает true для RFC 1918 приватных и link-local адресов.
    /// Эти адреса не нужно проксировать — локальная сеть работает напрямую.
    /// </summary>
    private static bool IsPrivateIp(byte[] buf, int offset)
    {
        byte a = buf[offset], b = buf[offset + 1];
        return a == 10                              // 10.0.0.0/8
            || (a == 172 && b >= 16 && b <= 31)    // 172.16.0.0/12
            || (a == 192 && b == 168)               // 192.168.0.0/16
            || (a == 169 && b == 254);              // 169.254.0.0/16 link-local
    }

    private static void WriteLoopbackIp(byte[] buf, int offset)
    {
        buf[offset]   = 127;
        buf[offset+1] = 0;
        buf[offset+2] = 0;
        buf[offset+3] = 1;
    }

    private static int    ReadU16BE(byte[] b, int o) => (b[o] << 8) | b[o + 1];
    private static void WriteU16BE(byte[] b, int o, ushort v)
        { b[o] = (byte)(v >> 8); b[o+1] = (byte)(v & 0xFF); }

    public void Dispose() => Stop();
}
