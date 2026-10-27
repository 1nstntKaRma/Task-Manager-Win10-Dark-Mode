using System;
using System.Globalization;

namespace TaskManagerDark
{
    /// <summary>Number formats the way Task Manager shows them.</summary>
    public static class Fmt
    {
        static readonly CultureInfo C = CultureInfo.CurrentCulture;
        const double MB = 1024.0 * 1024.0, GB = MB * 1024.0, TB = GB * 1024.0;

        /// <summary>"0%", "0.1%", "12.3%" (Processes tab cells).</summary>
        public static string Pct1(double v) => v < 0.05 ? "0%" : v.ToString("0.0", C) + "%";
        /// <summary>"12%" (headers, Performance tab).</summary>
        public static string Pct0(double v) => Math.Round(v).ToString("0", C) + "%";
        /// <summary>Details tab CPU column: "00", "07", "93".</summary>
        public static string Pct2Digits(double v) => Math.Min(99, (int)Math.Round(v)).ToString("00", C);

        /// <summary>"12.3 MB", "1,234.5 MB" (Processes tab memory).</summary>
        public static string MBytes(long bytes) => (bytes / MB).ToString("#,##0.0", C) + " MB";
        /// <summary>"0 MB/s", "0.1 MB/s".</summary>
        public static string MBps(double bytesPerSec) { double v = bytesPerSec / MB; return v < 0.05 ? "0 MB/s" : v.ToString("#,##0.0", C) + " MB/s"; }
        /// <summary>"0 Mbps", "0.1 Mbps".</summary>
        public static string Mbps(double bitsPerSec) { double v = bitsPerSec / 1e6; return v < 0.05 ? "0 Mbps" : v.ToString("#,##0.0", C) + " Mbps"; }
        /// <summary>Details tab memory: "12,345 K".</summary>
        public static string K(long bytes) => (bytes / 1024).ToString("#,##0", C) + " K";
        public static string Num(long v) => v.ToString("#,##0", C);

        /// <summary>Performance tab sizes: "512 MB", "7.5 GB", "1.2 TB".</summary>
        public static string Size(double bytes)
        {
            if (bytes >= TB) return (bytes / TB).ToString("0.0", C) + " TB";
            if (bytes >= GB) return (bytes / GB).ToString("0.0", C) + " GB";
            return (bytes / MB).ToString("0", C) + " MB";
        }

        /// <summary>"7.5/15.9 GB" (both in the unit of the larger value).</summary>
        public static string Pair(double used, double total)
        {
            if (total >= GB) return (used / GB).ToString("0.0", C) + "/" + (total / GB).ToString("0.0", C) + " GB";
            return (used / MB).ToString("0", C) + "/" + (total / MB).ToString("0", C) + " MB";
        }

        /// <summary>Capacity of disks: "932 GB", "1.8 TB".</summary>
        public static string Capacity(double bytes)
        {
            if (bytes >= TB) return (bytes / TB).ToString("0.0", C) + " TB";
            if (bytes >= GB) return (bytes / GB).ToString("0", C) + " GB";
            return (bytes / MB).ToString("0", C) + " MB";
        }

        /// <summary>Disk rates: "0 KB/s", "54.2 KB/s", "162 KB/s", "1.2 MB/s".</summary>
        public static string Rate(double bytesPerSec)
        {
            double kb = bytesPerSec / 1024.0;
            if (kb < 0.05) return "0 KB/s";
            if (kb < 1024) return Sig(kb) + " KB/s";
            double mb = kb / 1024.0;
            if (mb < 1024) return Sig(mb) + " MB/s";
            return Sig(mb / 1024.0) + " GB/s";
        }

        /// <summary>Network rates in bits: "0 Kbps", "8.0 Kbps", "1.2 Mbps".</summary>
        public static string Bits(double bitsPerSec)
        {
            double kb = bitsPerSec / 1000.0;
            if (kb < 0.05) return "0 Kbps";
            if (kb < 1000) return Sig(kb) + " Kbps";
            double mb = kb / 1000.0;
            if (mb < 1000) return Sig(mb) + " Mbps";
            return Sig(mb / 1000.0) + " Gbps";
        }

        static string Sig(double v) => v >= 100 ? v.ToString("0", C) : v.ToString("0.0", C);

        /// <summary>"0:01:23" (CPU time).</summary>
        public static string Hms(long ticks100ns)
        {
            long s = ticks100ns / 10000000;
            return (s / 3600).ToString(C) + ":" + (s / 60 % 60).ToString("00", C) + ":" + (s % 60).ToString("00", C);
        }

        /// <summary>"1:02:33:45" (up time, d:hh:mm:ss).</summary>
        public static string Uptime(long seconds) =>
            (seconds / 86400).ToString(C) + ":" + (seconds / 3600 % 24).ToString("00", C) + ":" + (seconds / 60 % 60).ToString("00", C) + ":" + (seconds % 60).ToString("00", C);

        public static string Ghz(double mhz) => (mhz / 1000.0).ToString("0.00", C) + " GHz";

        public static string Cache(long bytes) => bytes >= 1024 * 1024 ? (bytes / MB).ToString("0.0", C) + " MB" : (bytes / 1024).ToString("0", C) + " KB";
    }
}
