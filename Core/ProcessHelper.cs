using System.Diagnostics;
using System.Runtime.InteropServices;

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
