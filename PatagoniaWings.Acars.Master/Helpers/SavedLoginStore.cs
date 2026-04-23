using System;
using System.IO;
using System.Text;

namespace PatagoniaWings.Acars.Master.Helpers
{
    public static class SavedLoginStore
    {
        public sealed class Payload
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        private static string FolderPath
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(appData, "PatagoniaWings", "Acars", "auth");
            }
        }

        private static string FilePath => Path.Combine(FolderPath, "remembered-login.dat");

        public static void SaveOrClear(string email, string password, bool rememberMe)
        {
            if (!rememberMe)
            {
                Clear();
                return;
            }

            Save(email, password);
        }

        public static void Save(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                Clear();
                return;
            }

            Directory.CreateDirectory(FolderPath);

            var raw = string.Format("{0}\n{1}", email.Trim(), password);
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            File.WriteAllText(FilePath, encoded, Encoding.UTF8);
        }

        public static Payload? Load()
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var encoded = File.ReadAllText(FilePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(encoded))
            {
                return null;
            }

            var plainBytes = Convert.FromBase64String(encoded.Trim());
            var raw = Encoding.UTF8.GetString(plainBytes);
            var parts = raw.Split(new[] { '\n' }, 2);

            return new Payload
            {
                Email = parts.Length > 0 ? parts[0] : string.Empty,
                Password = parts.Length > 1 ? parts[1] : string.Empty
            };
        }

        public static void Clear()
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
    }
}
