using System.Runtime.InteropServices;

namespace ProxyMaster.Core;

/// <summary>
/// P/Invoke обёртки для WinDivert 2.2.x
/// Документация: https://reqrypt.org/windivert-doc.html
/// </summary>
internal static class WinDivert
{
    private const string DLL = "WinDivert.dll";

    public const uint  LAYER_NETWORK   = 0;
    public const uint  FLAG_DEFAULT    = 0;
    public const short PRIORITY_DEFAULT = 0;

    [DllImport(DLL, SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern IntPtr WinDivertOpen(
        string filter, uint layer, short priority, ulong flags);

    [DllImport(DLL, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertRecv(
        IntPtr handle, byte[] pPacket, uint packetLen,
        out uint pRecvLen, ref WinDivertAddress pAddr);

    [DllImport(DLL, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertSend(
        IntPtr handle, byte[] pPacket, uint packetLen,
        out uint pSendLen, ref WinDivertAddress pAddr);

    [DllImport(DLL, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertClose(IntPtr handle);

    [DllImport(DLL, SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertHelperCalcChecksums(
        byte[] pPacket, uint packetLen,
        ref WinDivertAddress pAddr, ulong flags);

    public static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);
}

/// <summary>
/// WinDivert 2.2.x WINDIVERT_ADDRESS — 88 байт.
///
/// Layout:
///   [0]  INT64  Timestamp
///   [8]  UINT32 Flags   (битовое поле: Outbound = бит 17)
///   [12] UINT32 Reserved2
///   [16] UINT64 Reserved3
///   [24] UINT8  Reserved4[64]  (union: Network/Flow/Socket/Reflect)
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 88)]
internal struct WinDivertAddress
{
    [FieldOffset(0)]  public long   Timestamp;
    [FieldOffset(8)]  public uint   Flags;      // битовое поле
    [FieldOffset(12)] public uint   Reserved2;
    [FieldOffset(16)] public ulong  Reserved3;
    // [24..87] = union data (IfIdx, SubIfIdx, etc.)

    // Outbound = бит 17 поля Flags
    // Layer [7:0], Event [15:8], Sniff[16], Outbound[17], Loopback[18] ...
    public bool IsOutbound => (Flags & (1u << 17)) != 0;
    public bool IsLoopback => (Flags & (1u << 18)) != 0;

    /// <summary>Установить флаг Outbound.</summary>
    public void SetOutbound(bool value)
    {
        if (value) Flags |=  (1u << 17);
        else       Flags &= ~(1u << 17);
    }
}
