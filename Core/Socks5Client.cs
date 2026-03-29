using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ProxyMaster.Core;

/// <summary>
/// Устанавливает соединение через SOCKS5-прокси.
/// RFC 1928 + RFC 1929 (аутентификация user/pass).
/// </summary>
internal static class Socks5Client
{
    private const byte Version = 5;

    /// <summary>
    /// Подключается к targetHost:targetPort через SOCKS5 прокси.
    /// Возвращает готовый NetworkStream для обмена данными.
    /// </summary>
    public static async Task<NetworkStream> ConnectAsync(
        string proxyHost, int proxyPort,
        string targetHost, int targetPort,
        string? username, string? password,
        CancellationToken ct = default)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(proxyHost, proxyPort, ct);
        var stream = tcp.GetStream();

        // ---- Шаг 1: Приветствие ----
        bool hasAuth = !string.IsNullOrEmpty(username);
        byte[] greeting = hasAuth
            ? new byte[] { Version, 2, 0x00, 0x02 }  // No-auth + User/Pass
            : new byte[] { Version, 1, 0x00 };         // No-auth only

        await stream.WriteAsync(greeting, ct);

        byte[] greetResp = await ReadExactAsync(stream, 2, ct);
        if (greetResp[0] != Version)
            throw new Exception("SOCKS5: неверная версия в ответе");

        byte method = greetResp[1];
        if (method == 0xFF)
            throw new Exception("SOCKS5: сервер не поддерживает методы аутентификации");

        // ---- Шаг 2: Аутентификация (если нужна) ----
        if (method == 0x02)
        {
            if (!hasAuth)
                throw new Exception("SOCKS5: сервер требует аутентификацию");

            byte[] user = Encoding.UTF8.GetBytes(username!);
            byte[] pass = Encoding.UTF8.GetBytes(password ?? "");

            byte[] authMsg = new byte[3 + user.Length + pass.Length];
            authMsg[0] = 0x01; // версия суб-переговоров
            authMsg[1] = (byte)user.Length;
            Buffer.BlockCopy(user, 0, authMsg, 2, user.Length);
            authMsg[2 + user.Length] = (byte)pass.Length;
            Buffer.BlockCopy(pass, 0, authMsg, 3 + user.Length, pass.Length);

            await stream.WriteAsync(authMsg, ct);

            byte[] authResp = await ReadExactAsync(stream, 2, ct);
            if (authResp[1] != 0x00)
                throw new Exception("SOCKS5: аутентификация отклонена");
        }

        // ---- Шаг 3: Запрос CONNECT ----
        byte[] hostBytes = Encoding.ASCII.GetBytes(targetHost);
        bool isIp = IPAddress.TryParse(targetHost, out var ip);

        List<byte> req = new();
        req.Add(Version);
        req.Add(0x01); // CMD: CONNECT
        req.Add(0x00); // RSV

        if (isIp && ip!.AddressFamily == AddressFamily.InterNetwork)
        {
            req.Add(0x01); // ATYP: IPv4
            req.AddRange(ip.GetAddressBytes());
        }
        else
        {
            req.Add(0x03); // ATYP: Domain
            req.Add((byte)hostBytes.Length);
            req.AddRange(hostBytes);
        }

        req.Add((byte)(targetPort >> 8));
        req.Add((byte)(targetPort & 0xFF));

        await stream.WriteAsync(req.ToArray(), ct);

        // ---- Шаг 4: Ответ сервера ----
        byte[] resp = await ReadExactAsync(stream, 4, ct);
        if (resp[0] != Version)
            throw new Exception("SOCKS5: неверная версия в ответе CONNECT");
        if (resp[1] != 0x00)
            throw new Exception($"SOCKS5: CONNECT отклонён, код {resp[1]}");

        // Читаем BND.ADDR и BND.PORT (нам не нужны, но прочитать надо)
        int addrLen = resp[3] switch
        {
            0x01 => 4,
            0x04 => 16,
            0x03 => -1, // динамически
            _    => throw new Exception("SOCKS5: неизвестный ATYP в ответе")
        };

        if (addrLen == -1)
        {
            byte[] domainLenBuf = await ReadExactAsync(stream, 1, ct);
            addrLen = domainLenBuf[0];
        }

        await ReadExactAsync(stream, addrLen + 2, ct); // адрес + порт

        return stream;
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream s, int count, CancellationToken ct)
    {
        byte[] buf = new byte[count];
        int total = 0;
        while (total < count)
        {
            int read = await s.ReadAsync(buf.AsMemory(total, count - total), ct);
            if (read == 0)
                throw new EndOfStreamException("SOCKS5: соединение закрыто");
            total += read;
        }
        return buf;
    }
}

/// <summary>
/// Простой HTTP CONNECT клиент.
/// </summary>
internal static class HttpProxyClient
{
    public static async Task<NetworkStream> ConnectAsync(
        string proxyHost, int proxyPort,
        string targetHost, int targetPort,
        string? username, string? password,
        CancellationToken ct = default)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(proxyHost, proxyPort, ct);
        var stream = tcp.GetStream();

        string authHeader = "";
        if (!string.IsNullOrEmpty(username))
        {
            string creds = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{username}:{password}"));
            authHeader = $"\r\nProxy-Authorization: Basic {creds}";
        }

        string request =
            $"CONNECT {targetHost}:{targetPort} HTTP/1.1\r\n" +
            $"Host: {targetHost}:{targetPort}" +
            authHeader +
            "\r\n\r\n";

        byte[] reqBytes = Encoding.ASCII.GetBytes(request);
        await stream.WriteAsync(reqBytes, ct);

        // Читаем ответ до \r\n\r\n
        var sb = new StringBuilder();
        int b;
        while (true)
        {
            b = stream.ReadByte();
            if (b < 0) throw new EndOfStreamException("HTTP CONNECT: соединение закрыто");
            sb.Append((char)b);
            if (sb.Length >= 4 && sb.ToString().EndsWith("\r\n\r\n"))
                break;
        }

        string response = sb.ToString();
        if (!response.Contains("200"))
            throw new Exception($"HTTP CONNECT отклонён:\n{response}");

        return stream;
    }
}
