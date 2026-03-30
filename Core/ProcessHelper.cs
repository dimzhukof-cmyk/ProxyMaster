using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ProxyMaster.Core;

internal static class ProcessHelper
{
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder,
        int ulAf, TcpTableClass tableClass, uint reserved);

    private enum TcpTableClass { TcpTableOwnerPidAll = 5 }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;   // сетевой порядок байт в младших 16 битах
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    /// <summary>Возвращает PID процесса, владеющего локальным TCP-портом, или null.</summary>
    public static int? GetProcessIdByLocalPort(ushort port)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2 /*AF_INET*/,
                            TcpTableClass.TcpTableOwnerPidAll, 0);

        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, 2,
                                    TcpTableClass.TcpTableOwnerPidAll, 0) != 0)
                return null;

            int count   = Marshal.ReadInt32(buf);
            IntPtr row0 = buf + 4;
            int rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();

            for (int i = 0; i < count; i++)
            {
                var r = Marshal.PtrToStructure<MibTcpRowOwnerPid>(row0 + i * rowSize);
                // ntohs: меняем байты младшего слова
                ushort localPort = (ushort)r.LocalPort;
                localPort = (ushort)((localPort >> 8) | ((localPort & 0xFF) << 8));
                if (localPort == port)
                    return (int)r.OwningPid;
            }
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    /// <summary>Возвращает список запущенных процессов (уникальных по имени), отсортированный по имени.</summary>
    public static List<(string Name, string Path)> GetRunningProcesses()
    {
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string Name, string Path)>();

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                string name = p.ProcessName + ".exe";
                if (!seen.Add(name)) continue;

                string path = "";
                try { path = p.MainModule?.FileName ?? ""; } catch { }
                result.Add((Name: name, Path: path));
            }
            catch { }
            finally { p.Dispose(); }
        }

        result.Sort((a, b) =>
            string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    // ---- Извлечение иконок (Shell32) ----

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSFI, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int    iIcon;
        public uint   dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON      = 0x100;
    private const uint SHGFI_SMALLICON = 0x001;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource?>
        _iconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Возвращает маленькую иконку (16×16) приложения по пути к .exe.</summary>
    public static ImageSource? GetIcon(string exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        return _iconCache.GetOrAdd(exePath, ExtractIcon);
    }

    private static ImageSource? ExtractIcon(string path)
    {
        var info = new SHFILEINFO();
        SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_ICON | SHGFI_SMALLICON);
        if (info.hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze(); // безопасно для других потоков
            return src;
        }
        catch { return null; }
        finally { DestroyIcon(info.hIcon); }
    }

    /// <summary>Возвращает имя процесса по PID (ProcessName + ".exe"), или null.</summary>
    public static string? GetProcessNameById(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName + ".exe";
        }
        catch { return null; }
    }
}
