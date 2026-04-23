using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace PatagoniaWings.Acars.Master.Helpers
{
    public sealed class UiPreferencesPayload
    {
        public bool AlwaysVisible { get; set; } = false;
        public bool UseKg { get; set; } = true;
        public string SimulatorIp { get; set; } = "127.0.0.1";
    }

    public static class UiPreferencesStore
    {
        private static string FolderPath
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(appData, "PatagoniaWings", "Acars", "ui");
            }
        }

        private static string FilePath => Path.Combine(FolderPath, "shell-preferences.json");

        public static UiPreferencesPayload Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return new UiPreferencesPayload();
                }

                var raw = File.ReadAllText(FilePath, Encoding.UTF8);
                var payload = JsonConvert.DeserializeObject<UiPreferencesPayload>(raw);
                return payload ?? new UiPreferencesPayload();
            }
            catch
            {
                return new UiPreferencesPayload();
            }
        }

        public static void Save(UiPreferencesPayload payload)
        {
            try
            {
                Directory.CreateDirectory(FolderPath);
                File.WriteAllText(
                    FilePath,
                    JsonConvert.SerializeObject(payload, Formatting.Indented),
                    Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}
