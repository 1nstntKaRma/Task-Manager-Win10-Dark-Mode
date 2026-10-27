using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskManagerDark
{
    /// <summary>
    /// "Operating system context" column: the newest Windows version listed in the program's manifest
    /// (compatibility/supportedOS), which is what decides the context Windows runs it in. No entry means Vista.
    /// </summary>
    public static class OsContext
    {
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr FindResource(IntPtr h, IntPtr name, IntPtr type);
        [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr h, IntPtr r);
        [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr r);
        [DllImport("kernel32.dll")] static extern int SizeofResource(IntPtr h, IntPtr r);

        static readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly KeyValuePair<string, string>[] Os =
        {
            new KeyValuePair<string, string>("8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a", "Windows 10"),
            new KeyValuePair<string, string>("1f676c76-80e1-4239-95bb-83d0f6d0da78", "Windows 8.1"),
            new KeyValuePair<string, string>("4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38", "Windows 8"),
            new KeyValuePair<string, string>("35138b9a-5d96-4fbd-8e2d-a2440225f93a", "Windows 7"),
            new KeyValuePair<string, string>("e2011457-1546-43c5-a5fe-008deee3d3f0", "Windows Vista"),
        };

        public static string For(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            lock (_cache) if (_cache.TryGetValue(path, out var c)) return c;
            string result = "Windows Vista";
            IntPtr h = Sys.LoadLibraryEx(path, IntPtr.Zero, 0x2 | 0x20 /*AS_DATAFILE | AS_IMAGE_RESOURCE*/);
            if (h != IntPtr.Zero)
            {
                try
                {
                    var r = FindResource(h, (IntPtr)1, (IntPtr)24 /*RT_MANIFEST*/);
                    if (r == IntPtr.Zero) r = FindResource(h, (IntPtr)2, (IntPtr)24);
                    if (r != IntPtr.Zero)
                    {
                        int size = SizeofResource(h, r);
                        var p = LockResource(LoadResource(h, r));
                        var bytes = new byte[size];
                        Marshal.Copy(p, bytes, 0, size);
                        var xml = Encoding.UTF8.GetString(bytes).ToLowerInvariant();
                        foreach (var kv in Os) if (xml.Contains(kv.Key)) { result = kv.Value; break; }
                    }
                }
                catch (Exception ex) { ErrorLog.Write("OsContext " + path, ex); }
                finally { Sys.FreeLibrary(h); }
            }
            else result = "";
            lock (_cache) _cache[path] = result;
            return result;
        }
    }
}
