using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace KassenbuchApp
{
    public static class BelegStorage
    {
        // Basisordner: %AppData%\KassenbuchApp\Belege
        public static string BaseFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "KassenbuchApp", "Belege");

        // Legt z.B. ...\Belege\2025\11\{KassenbuchId}\ an und gibt den Pfad zurück
        public static string EnsureEntryFolder(int year, int month, int kassenbuchId)
        {
            var dir = Path.Combine(BaseFolder, year.ToString("0000"),
                                   month.ToString("00"), kassenbuchId.ToString());
            Directory.CreateDirectory(dir);
            return dir;
        }

        // Optional: Hash für Dublettencheck
        public static string ComputeMd5(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            return string.Concat(hash.Select(b => b.ToString("x2")));
        }
    }
}
