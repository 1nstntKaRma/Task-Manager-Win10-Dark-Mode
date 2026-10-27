using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TaskManagerDark
{
    /// <summary>Minimal PDH (performance counter) query wrapper. Owned and used by one thread.</summary>
    public sealed unsafe class PdhQuery : IDisposable
    {
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhOpenQueryW")] static extern int PdhOpenQuery(string src, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")] static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr query);
        [DllImport("pdh.dll")] static extern int PdhRemoveCounter(IntPtr counter);
        [DllImport("pdh.dll")] static extern int PdhGetFormattedCounterValue(IntPtr counter, int format, out int type, out FMT value);
        [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")] static extern int PdhGetFormattedCounterArray(IntPtr counter, int format, ref int size, out int count, IntPtr buf);

        [StructLayout(LayoutKind.Sequential)]
        struct FMT { public int CStatus; public int pad; public double Value; }

        public const int FMT_DOUBLE = 0x200, FMT_LARGE = 0x400, FMT_NOCAP100 = 0x8000;
        const int PDH_MORE_DATA = unchecked((int)0x800007D2);

        IntPtr _q;
        readonly List<IntPtr> _counters = new List<IntPtr>();
        IntPtr _arrBuf; int _arrSize;
        public bool Valid => _q != IntPtr.Zero;
        public int CollectCount;

        public PdhQuery()
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _q) != 0) _q = IntPtr.Zero;
        }

        /// <summary>Adds a counter by its English path. Returns IntPtr.Zero when the counter does not exist on this system.</summary>
        public IntPtr Add(string path)
        {
            if (_q == IntPtr.Zero) return IntPtr.Zero;
            int r = PdhAddEnglishCounter(_q, path, IntPtr.Zero, out var c);
            if (r != 0) return IntPtr.Zero;
            _counters.Add(c);
            return c;
        }

        public void Remove(IntPtr c) { if (c != IntPtr.Zero && _counters.Remove(c)) PdhRemoveCounter(c); }

        public bool Collect()
        {
            if (_q == IntPtr.Zero) return false;
            CollectCount++;
            return PdhCollectQueryData(_q) == 0;
        }

        public double Value(IntPtr c, double def = double.NaN, bool noCap = true)
        {
            if (c == IntPtr.Zero) return def;
            if (PdhGetFormattedCounterValue(c, FMT_DOUBLE | (noCap ? FMT_NOCAP100 : 0), out _, out var v) != 0) return def;
            if (v.CStatus != 0 && v.CStatus != 1) return def; // PDH_CSTATUS_VALID_DATA / NEW_DATA
            return v.Value;
        }

        public delegate void ItemVisitor(char* name, double value);

        /// <summary>Visits every instance of a wildcard counter. The name pointer is only valid during the call.</summary>
        public int ForEach(IntPtr c, ItemVisitor visit, bool noCap = true)
        {
            if (c == IntPtr.Zero) return 0;
            int size = _arrSize;
            int r = PdhGetFormattedCounterArray(c, FMT_DOUBLE | (noCap ? FMT_NOCAP100 : 0), ref size, out int count, _arrBuf);
            if (r == PDH_MORE_DATA || (r == 0 && _arrBuf == IntPtr.Zero && size > 0))
            {
                if (_arrBuf != IntPtr.Zero) Marshal.FreeHGlobal(_arrBuf);
                _arrSize = Math.Max(size, 4096) * 2;
                _arrBuf = Marshal.AllocHGlobal(_arrSize);
                size = _arrSize;
                r = PdhGetFormattedCounterArray(c, FMT_DOUBLE | (noCap ? FMT_NOCAP100 : 0), ref size, out count, _arrBuf);
            }
            if (r != 0) return 0;
            byte* p = (byte*)_arrBuf;
            for (int i = 0; i < count; i++)
            {
                byte* item = p + i * 24;
                char* name = *(char**)item;
                int status = *(int*)(item + 8);
                double val = *(double*)(item + 16);
                if (status == 0 || status == 1) visit(name, val);
            }
            return count;
        }

        /// <summary>Instance names of a wildcard counter (allocates; for rare use such as listing disks).</summary>
        public List<KeyValuePair<string, double>> Items(IntPtr c)
        {
            var l = new List<KeyValuePair<string, double>>();
            ForEach(c, (n, v) => l.Add(new KeyValuePair<string, double>(new string(n), v)));
            return l;
        }

        public void Dispose()
        {
            if (_q != IntPtr.Zero) { PdhCloseQuery(_q); _q = IntPtr.Zero; }
            if (_arrBuf != IntPtr.Zero) { Marshal.FreeHGlobal(_arrBuf); _arrBuf = IntPtr.Zero; }
        }

        // ---------------------------------------------------------------- tiny parsing helpers for instance names
        public static bool StartsWith(char* s, string prefix)
        {
            for (int i = 0; i < prefix.Length; i++) if (s[i] != prefix[i]) return false;
            return true;
        }

        /// <summary>Finds 'token' in s; returns the index after it or -1.</summary>
        public static int IndexAfter(char* s, string token, int from = 0)
        {
            for (int i = from; s[i] != '\0'; i++)
            {
                int k = 0;
                while (k < token.Length && s[i + k] == token[k]) k++;
                if (k == token.Length) return i + k;
            }
            return -1;
        }

        public static long ParseLong(char* s, ref int i)
        {
            long v = 0;
            while (s[i] >= '0' && s[i] <= '9') { v = v * 10 + (s[i] - '0'); i++; }
            return v;
        }

        public static long ParseHex(char* s, ref int i)
        {
            if (s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X')) i += 2;
            long v = 0;
            while (true)
            {
                char ch = s[i]; int d;
                if (ch >= '0' && ch <= '9') d = ch - '0';
                else if (ch >= 'a' && ch <= 'f') d = ch - 'a' + 10;
                else if (ch >= 'A' && ch <= 'F') d = ch - 'A' + 10;
                else break;
                v = (v << 4) | (uint)d; i++;
            }
            return v;
        }
    }
}
