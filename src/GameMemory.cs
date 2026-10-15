// 遊戲執行中的記憶體讀寫（單機遊戲用的即時修改）
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GG2Editor
{
    class GameMemory : IDisposable
    {
        const int PROCESS_VM_READ = 0x0010, PROCESS_VM_WRITE = 0x0020, PROCESS_VM_OPERATION = 0x0008, PROCESS_QUERY_INFORMATION = 0x0400;

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr written);

        IntPtr handle;
        Process proc;
        public long GameAssembly { get; private set; }
        public int Pid { get { return proc.Id; } }

        public static GameMemory Attach()
        {
            Process[] ps;
            try { ps = Process.GetProcessesByName("Genome Guardian 2"); } catch { return null; }
            if (ps.Length == 0) return null;
            var p = ps[0];
            long ga = 0;
            try
            {
                foreach (ProcessModule m in p.Modules)
                    if (string.Equals(m.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase)) { ga = m.BaseAddress.ToInt64(); break; }
            }
            catch { return null; }          // 剛啟動、模組還沒載入
            if (ga == 0) return null;
            var h = OpenProcess(PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION, false, p.Id);
            if (h == IntPtr.Zero) return null;
            return new GameMemory { handle = h, proc = p, GameAssembly = ga };
        }

        public bool Alive
        {
            get { try { proc.Refresh(); return !proc.HasExited; } catch { return false; } }
        }

        byte[] Read(long addr, int n)
        {
            if (addr < 0x10000) return null;
            var b = new byte[n]; IntPtr r;
            return ReadProcessMemory(handle, new IntPtr(addr), b, new IntPtr(n), out r) && r.ToInt64() == n ? b : null;
        }
        bool Write(long addr, byte[] b)
        {
            if (addr < 0x10000) return false;
            IntPtr w;
            return WriteProcessMemory(handle, new IntPtr(addr), b, new IntPtr(b.Length), out w) && w.ToInt64() == b.Length;
        }

        public long Ptr(long addr) { var b = Read(addr, 8); return b == null ? 0 : BitConverter.ToInt64(b, 0); }
        public int? Int(long addr) { var b = Read(addr, 4); return b == null ? (int?)null : BitConverter.ToInt32(b, 0); }
        public float? Float(long addr) { var b = Read(addr, 4); return b == null ? (float?)null : BitConverter.ToSingle(b, 0); }
        public bool? Bool(long addr) { var b = Read(addr, 1); return b == null ? (bool?)null : b[0] != 0; }
        public bool SetInt(long addr, int v) { return Write(addr, BitConverter.GetBytes(v)); }
        public bool SetFloat(long addr, float v) { return Write(addr, BitConverter.GetBytes(v)); }
        public bool SetBool(long addr, bool v) { return Write(addr, new[] { (byte)(v ? 1 : 0) }); }

        // Il2CppClass* 的類別名稱（+0x10 是 char* name），用來確認指標沒有指錯
        public string KlassName(long klass)
        {
            var b = Read(Ptr(klass + 0x10), 32);
            if (b == null) return null;
            int n = Array.IndexOf(b, (byte)0);
            return System.Text.Encoding.ASCII.GetString(b, 0, n < 0 ? b.Length : n);
        }
        public string ClassName(long obj) { return KlassName(Ptr(obj)); }

        public void Dispose()
        {
            if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
        }
    }
}
