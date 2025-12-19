using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace KassenbuchApp
{
    public class BelegItem
    {
        public int Id { get; set; }
        public int KassenbuchId { get; set; }
        public string Seite { get; set; } = "Einnahme"; // oder "Ausgabe"
        public string Originalname { get; set; } = "";
        public string Dateiname { get; set; } = "";
        public string RelPfad { get; set; } = "";
        public string HinzugefuegtAm { get; set; } = "";
        public string AbsolutePath =>
            Path.Combine(BelegStorage.BaseFolder, RelPfad.Replace('/', Path.DirectorySeparatorChar));
    }

    public partial class BelegeWindow : Window
    {
        public int KassenbuchId { get; }
        public string Seite { get; }
        public ObservableCollection<BelegItem> Belege { get; } = new();

        public BelegeWindow(int kassenbuchId, string seite)
        {
            InitializeComponent();
            DataContext = this;

            KassenbuchId = kassenbuchId;
            Seite = seite;
            LoadBelege();
        }

        private void LoadBelege()
        {
            Belege.Clear();
            using var con = new SQLiteConnection(AppConfig.ConnectionString);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT Id, KassenbuchId, Seite, Originalname, Dateiname, RelPfad, HinzugefuegtAm FROM Belege WHERE KassenbuchId=@id AND Seite=@seite ORDER BY Id";
            cmd.Parameters.AddWithValue("@id", KassenbuchId);
            cmd.Parameters.AddWithValue("@seite", Seite);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                Belege.Add(new BelegItem
                {
                    Id = rd.GetInt32(0),
                    KassenbuchId = rd.GetInt32(1),
                    Seite = rd.GetString(2),
                    Originalname = rd.GetString(3),
                    Dateiname = rd.GetString(4),
                    RelPfad = rd.GetString(5),
                    HinzugefuegtAm = rd.GetString(6),
                });
            }
            ListBelege.ItemsSource = Belege;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            using var con = new SQLiteConnection(AppConfig.ConnectionString);
            con.Open();

            // 1) Sicherheit: passt die Seite (Einnahme/Ausgabe) zum Eintrag?
            using (var check = con.CreateCommand())
            {
                check.CommandText = @"
            SELECT 
                CASE WHEN IFNULL(EinnahmeBrutto,0) <> 0 THEN 1 ELSE 0 END AS IsEinnahme,
                CASE WHEN IFNULL(AusgabeBrutto,0)  <> 0 THEN 1 ELSE 0 END AS IsAusgabe
            FROM Kassenbuch
            WHERE Id = @id;";
                check.Parameters.AddWithValue("@id", KassenbuchId);

                using var r = check.ExecuteReader();
                if (r.Read())
                {
                    bool isE = r.GetInt64(0) == 1;
                    bool isA = r.GetInt64(1) == 1;

                    if ((Seite == "Einnahme" && !isE) || (Seite == "Ausgabe" && !isA))
                    {
                        MessageBox.Show(
                            $"Dieser Eintrag ist keine {Seite}. Daher können hier keine {Seite}-Belege hinterlegt werden.",
                            "Falscher Belegtyp",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }

            // 2) Dateien auswählen
            var dlg = new OpenFileDialog { Title = "Belege auswählen", Multiselect = true };
            if (dlg.ShowDialog() != true) return;

            var now = DateTime.Now;
            var targetDir = BelegStorage.EnsureEntryFolder(now.Year, now.Month, KassenbuchId);

            int laufNr = Belege.Count;

            foreach (var srcPath in dlg.FileNames)
            {
                var ext = Path.GetExtension(srcPath);
                var baseName = Path.GetFileName(srcPath);
                var md5Src = BelegStorage.ComputeMd5(srcPath);

                // 3) Duplikatprüfung (gleicher Hash in gleicher Seite)
                using (var dup = con.CreateCommand())
                {
                    dup.CommandText = "SELECT COUNT(1) FROM Belege WHERE KassenbuchId=@kid AND Seite=@seite AND HashMd5=@md5";
                    dup.Parameters.AddWithValue("@kid", KassenbuchId);
                    dup.Parameters.AddWithValue("@seite", Seite);
                    dup.Parameters.AddWithValue("@md5", md5Src);

                    if (Convert.ToInt32(dup.ExecuteScalar()) > 0)
                    {
                        var ask = MessageBox.Show(
                            $"Dieser Beleg scheint bereits vorhanden zu sein:\n{baseName}\n\nTrotzdem erneut speichern?",
                            "Möglicher Duplikat-Beleg",
                            MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (ask == MessageBoxResult.No) continue;
                    }
                }

                // 4) Freien Dateinamen finden
                int idx = ++laufNr;
                string fileName, dstPath;
                while (true)
                {
                    fileName = $"{now:yyyy-MM-dd}_{KassenbuchId}_{idx:D2}{ext}";
                    dstPath = Path.Combine(targetDir, fileName);
                    if (!File.Exists(dstPath)) break;
                    idx++;
                }
                laufNr = idx;

                // 5) Kopieren (nur wenn noch nicht vorhanden)
                if (!File.Exists(dstPath))
                    File.Copy(srcPath, dstPath, overwrite: false);

                var relPath = Path.GetRelativePath(BelegStorage.BaseFolder, dstPath).Replace('\\', '/');

                // 6) DB-Insert
                using (var ins = con.CreateCommand())
                {
                    ins.CommandText = @"
                INSERT INTO Belege
                    (KassenbuchId, Seite, Originalname, Dateiname, RelPfad, HashMd5, HinzugefuegtAm)
                VALUES
                    (@kid, @seite, @orig, @datei, @rel, @md5, strftime('%Y-%m-%dT%H:%M:%SZ','now'))";
                    ins.Parameters.AddWithValue("@kid", KassenbuchId);
                    ins.Parameters.AddWithValue("@seite", Seite);
                    ins.Parameters.AddWithValue("@orig", baseName);
                    ins.Parameters.AddWithValue("@datei", fileName);
                    ins.Parameters.AddWithValue("@rel", relPath);
                    ins.Parameters.AddWithValue("@md5", md5Src);
                    ins.ExecuteNonQuery();
                }
            }

            LoadBelege();
        }

        private BelegItem? Selected => (BelegItem?)ListBelege.SelectedItem;

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is null) return;
            if (File.Exists(Selected.AbsolutePath))
                Process.Start(new ProcessStartInfo(Selected.AbsolutePath) { UseShellExecute = true });
            else
                MessageBox.Show("Datei nicht gefunden.");
        }

        private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is null) return;
            if (File.Exists(Selected.AbsolutePath))
                Process.Start("explorer.exe", $"/select,\"{Selected.AbsolutePath}\"");
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is null) return;
            if (MessageBox.Show("Beleg wirklich entfernen?", "Löschen",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            using var con = new SQLiteConnection(AppConfig.ConnectionString);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM Belege WHERE Id=@id";
            cmd.Parameters.AddWithValue("@id", Selected.Id);
            cmd.ExecuteNonQuery();

            try { if (File.Exists(Selected.AbsolutePath)) File.Delete(Selected.AbsolutePath); } catch { }

            LoadBelege();
        }
    }
}
