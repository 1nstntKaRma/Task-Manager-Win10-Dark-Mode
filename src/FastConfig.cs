using System;
using System.Configuration;
using System.Configuration.Internal;
using System.Reflection;

namespace TaskManagerDark
{
    /// <summary>
    /// Keeps the .NET configuration files off the startup path. The first control Windows Forms creates reads
    /// appSettings (its legacy DPI switch), which makes the configuration system parse machine.config: 70-100 ms, a
    /// third of the time to the first window. This program has no .config file, so appSettings is always empty and the
    /// optional Windows Forms / System.Drawing sections never exist (their callers accept "no section"). Those lookups
    /// are answered directly; any other one goes to the real configuration system, created on first use.
    /// </summary>
    sealed class FastConfig : IInternalConfigSystem
    {
        readonly object _lock = new object();
        IInternalConfigSystem _real;
        readonly Type _clientType;

        FastConfig(Type clientType) { _clientType = clientType; }

        public static void Install()
        {
            try
            {
                var setter = typeof(ConfigurationManager).GetMethod("SetConfigurationSystem", BindingFlags.NonPublic | BindingFlags.Static);
                var client = typeof(ConfigurationManager).Assembly.GetType("System.Configuration.ClientConfigurationSystem");
                if (setter == null || client == null) { ErrorLog.Write("FastConfig", new MissingMethodException("configuration internals not found; using the standard configuration")); return; }
                setter.Invoke(null, new object[] { new FastConfig(client), true });
            }
            catch (Exception ex) { ErrorLog.Write("FastConfig", ex); }
        }

        IInternalConfigSystem Real
        {
            get
            {
                lock (_lock)
                    return _real ?? (_real = (IInternalConfigSystem)Activator.CreateInstance(_clientType, true));
            }
        }

        sealed class EmptySettings : System.Collections.Specialized.NameValueCollection
        {
            public EmptySettings() { IsReadOnly = true; }
        }

        static readonly EmptySettings NoAppSettings = new EmptySettings();

        public object GetSection(string configKey)
        {
            switch (configKey)
            {
                case "appSettings": return NoAppSettings;
                case "System.Windows.Forms.ApplicationConfigurationSection":
                case "system.drawing": return null;
            }
            StartupLog.Mark("configuration read: " + configKey);
            return Real.GetSection(configKey);
        }

        public void RefreshConfig(string sectionName) => Real.RefreshConfig(sectionName);
        public bool SupportsUserConfig => Real.SupportsUserConfig;
    }
}
