using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TaskManagerDark
{
    /// <summary>Tiny INI store in %APPDATA%\TaskManagerDark. Everything the app remembers between sessions lives here.</summary>
    public static class Settings
    {
        static readonly Dictionary<string, string> _v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskManagerDark");
        /// <summary>Tests point this at a scratch folder so they never touch the real settings.</summary>
        public static string OverrideDir;
        static string Dir => OverrideDir ?? DataDir;
        static string FilePath => Path.Combine(Dir, "settings.ini");
        static bool _dirty;

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0 || line.StartsWith(";")) continue;
                    _v[line.Substring(0, i).Trim()] = line.Substring(i + 1);
                }
            }
            catch (Exception ex) { ErrorLog.Write("Settings.Load", ex); }
        }

        public static void Save()
        {
            if (!_dirty) return;
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.AppendLine("; Task Manager - Dark Mode settings");
                var keys = new List<string>(_v.Keys);
                keys.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var k in keys) sb.Append(k).Append('=').AppendLine(_v[k]);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
                _dirty = false;
            }
            catch (Exception ex) { ErrorLog.Write("Settings.Save", ex); }
        }

        static void Put(string k, string v)
        {
            if (v == null) { if (_v.Remove(k)) _dirty = true; return; }
            if (_v.TryGetValue(k, out var old) && old == v) return;
            _v[k] = v; _dirty = true;
        }

        public static string Get(string k, string def = null) => _v.TryGetValue(k, out var s) ? s : def;
        public static void Set(string k, string v) => Put(k, v);
        public static int GetInt(string k, int def) => _v.TryGetValue(k, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : def;
        public static void SetInt(string k, int v) => Put(k, v.ToString(CultureInfo.InvariantCulture));
        public static bool GetBool(string k, bool def) => _v.TryGetValue(k, out var s) ? s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase) : def;
        public static void SetBool(string k, bool v) => Put(k, v ? "1" : "0");
        public static long GetLong(string k, long def) => _v.TryGetValue(k, out var s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long r) ? r : def;
        public static void SetLong(string k, long v) => Put(k, v.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Writes unexpected errors to %APPDATA%\TaskManagerDark\errors.log (repeats of the same error are collapsed).</summary>
    public static class ErrorLog
    {
        static readonly Dictionary<string, DateTime> _recent = new Dictionary<string, DateTime>();
        static readonly object _lock = new object();
        public static int Count;

        public static void Write(string where, Exception ex)
        {
            try
            {
                string key = where + "|" + ex.GetType().Name + "|" + ex.Message;
                lock (_lock)
                {
                    var now = DateTime.Now;
                    if (_recent.TryGetValue(key, out var last) && (now - last).TotalMinutes < 10) return;
                    _recent[key] = now;
                    Count++;
                    var dir = Settings.OverrideDir ?? Settings.DataDir;
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "errors.log"), now.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + where + "]  " + ex + Environment.NewLine + Environment.NewLine);
                }
            }
            catch { /* logging must never throw */ }
        }

        public static void Note(string where, string message) => Write(where, new Exception(message));
    }
}
