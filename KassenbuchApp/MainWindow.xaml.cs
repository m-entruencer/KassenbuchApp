// MainWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.ComponentModel;
using System.Windows.Controls;
using Microsoft.Win32;
using System.IO.Compression;
using System.Diagnostics;
using System.Windows.Input;
using System.Reflection;

namespace KassenbuchApp
{
    public partial class MainWindow : Window
    {
        private readonly List<string> ausgewählteBelegeEinnahme = new();
        private readonly List<string> ausgewählteBelegeAusgabe = new();
        private int aktuellerMonat = DateTime.Now.Month;
        private int aktuellesJahr = DateTime.Now.Year;
        public MainWindow()
        {
            InitializeComponent();
            var ver = Assembly.GetExecutingAssembly()
                  .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
          ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString();
            this.Title = $"Kassenbuch v{ver}";
            InitDatabase();
            LadeJahresauswahl();
            AktualisiereMonatsanzeige();
            LadeKassenbuch();
        }

        private Dictionary<int, int> LadeAlleBelegCounts()
        {
            var dict = new Dictionary<int, int>();

            using var con = new SQLiteConnection(AppConfig.ConnectionString);
            con.Open();

            // Query auf dieselbe Sicht einschränken wie dein Grid (Jahr + optional Monat)
            var sql = @"
        SELECT k.Id, IFNULL(COUNT(b.Id), 0) AS Cnt
        FROM Kassenbuch k
        LEFT JOIN Belege b ON b.KassenbuchId = k.Id
        WHERE strftime('%Y', k.Datum) = @Jahr
    ";
            if (aktuellerMonat != 0)
                sql += " AND strftime('%m', k.Datum) = @Monat ";
            sql += " GROUP BY k.Id;";

            using var cmd = new SQLiteCommand(sql, con);
            cmd.Parameters.AddWithValue("@Jahr", aktuellesJahr.ToString());
            if (aktuellerMonat != 0)
                cmd.Parameters.AddWithValue("@Monat", aktuellerMonat.ToString("D2"));

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
                dict[rd.GetInt32(0)] = rd.GetInt32(1);

            return dict;
        }


        public class KassenbuchEintrag
        {
            public int Id { get; set; }
            public string Datum { get; set; } = string.Empty;
            public DateTime DatumSort { get; set; }
            public string? EinnahmeBrutto { get; set; }
            public string? KäuferZweck { get; set; }
            public string? VerkaufterArtikel { get; set; }
            public string? AusgabeBrutto { get; set; }
            public string? ZweckDerAusgabe { get; set; }
            public string? Bezahlmethode { get; set; }
            public string? BelegPfad { get; set; }
            public int BelegCount { get; set; }
            public bool HasBelege => BelegCount > 0;

        }

        private void LadeJahresauswahl()
        {
            cmbJahr.Items.Clear();
            int aktuellesJahr = DateTime.Now.Year;

            for (int jahr = aktuellesJahr - 5; jahr <= aktuellesJahr + 5; jahr++)
            {
                var item = new ComboBoxItem { Content = jahr.ToString() };
                if (jahr == aktuellesJahr) item.IsSelected = true;
                cmbJahr.Items.Add(item);
            }
        }

        private void Logo_Click(object sender, MouseButtonEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://entruencer.de") { UseShellExecute = true });
        }

        private void BtnMonatZurueck_Click(object sender, RoutedEventArgs e)
        {
            aktuellerMonat--;
            if (aktuellerMonat < 1)
            {
                aktuellerMonat = 12;
                aktuellesJahr--;
            }

            AktualisiereMonatsanzeige();
            LadeKassenbuch();
        }

        private void BtnMonatVor_Click(object sender, RoutedEventArgs e)
        {
            aktuellerMonat++;
            if (aktuellerMonat > 12)
            {
                aktuellerMonat = 1;
                aktuellesJahr++;
            }

            AktualisiereMonatsanzeige();
            LadeKassenbuch();
        }


        private void AktualisiereMonatsanzeige()
        {
            string monatName = new DateTime(aktuellesJahr, aktuellerMonat, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            txtMonatsAnzeige.Text = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(monatName);
        }

        private void InitDatabase()
        {
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kassenbuch.db");

            if (File.Exists(dbPath) && new FileInfo(dbPath).Length < 100)
                File.Delete(dbPath);

            using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
            conn.Open();
            string sql = @"
                CREATE TABLE IF NOT EXISTS Kassenbuch (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Datum TEXT,
                    EinnahmeBrutto REAL,
                    KäuferZweck TEXT,
                    VerkaufterArtikel TEXT,
                    Bezahlmethode TEXT,
                    AusgabeBrutto REAL,
                    ZweckDerAusgabe TEXT,
                    BelegPfad TEXT
                );";
            using var cmd = new SQLiteCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        private SQLiteConnection GetConnection()
        {
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kassenbuch.db");
            return new SQLiteConnection($"Data Source={dbPath};Version=3;");
        }

        private void LadeKassenbuch()
        {
            var eintraege = new List<KassenbuchEintrag>();
            decimal saldo = 0;

            using var conn = GetConnection();
            conn.Open();

            // 📌 1. Nur die Datensätze für das Grid (nach Monat UND Jahr)
            string sqlEintraege = "SELECT * FROM Kassenbuch WHERE strftime('%Y', Datum) = @Jahr";
            if (aktuellerMonat != 0)
            {
                sqlEintraege += " AND strftime('%m', Datum) = @Monat";
            }

            sqlEintraege += " ORDER BY Datum DESC, Id DESC";

            using var cmdEintraege = new SQLiteCommand(sqlEintraege, conn);
            cmdEintraege.Parameters.AddWithValue("@Jahr", aktuellesJahr.ToString());
            if (aktuellerMonat != 0)
            {
                string monatString = aktuellerMonat.ToString("D2");
                cmdEintraege.Parameters.AddWithValue("@Monat", monatString);
            }

            using var reader = cmdEintraege.ExecuteReader();
            while (reader.Read())
            {
                decimal? einnahme = reader["EinnahmeBrutto"] != DBNull.Value ? Convert.ToDecimal(reader["EinnahmeBrutto"]) : null;
                decimal? ausgabe = reader["AusgabeBrutto"] != DBNull.Value ? Convert.ToDecimal(reader["AusgabeBrutto"]) : null;

                var eintrag = new KassenbuchEintrag
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Datum = DateTime.TryParse(reader["Datum"].ToString(), out var d) ? d.ToString("dd.MM.yyyy") : "",
                    DatumSort = DateTime.TryParse(reader["Datum"].ToString(), out var sortDate) ? sortDate : DateTime.MinValue,
                    EinnahmeBrutto = einnahme.HasValue ? einnahme.Value.ToString("F2") + " €" : "",
                    KäuferZweck = reader["KäuferZweck"].ToString(),
                    VerkaufterArtikel = reader["VerkaufterArtikel"].ToString(),
                    AusgabeBrutto = ausgabe.HasValue ? ausgabe.Value.ToString("F2") + " €" : "",
                    ZweckDerAusgabe = reader["ZweckDerAusgabe"].ToString(),
                    Bezahlmethode = reader["Bezahlmethode"].ToString(),
                    BelegPfad = reader["BelegPfad"].ToString()
                };

                eintraege.Add(eintrag);
            }

            var counts = LadeAlleBelegCounts();
            foreach (var eintrag in eintraege)
            {
                counts.TryGetValue(eintrag.Id, out var c);
                eintrag.BelegCount = c;
            }

            // 📌 2. Jetzt Saldoberechnung unabhängig vom Monat
            string sqlSaldo = "SELECT EinnahmeBrutto, AusgabeBrutto FROM Kassenbuch WHERE strftime('%Y', Datum) = @Jahr";
            using var cmdSaldo = new SQLiteCommand(sqlSaldo, conn);
            cmdSaldo.Parameters.AddWithValue("@Jahr", aktuellesJahr.ToString());

            using var readerSaldo = cmdSaldo.ExecuteReader();
            while (readerSaldo.Read())
            {
                decimal? einnahme = readerSaldo["EinnahmeBrutto"] != DBNull.Value ? Convert.ToDecimal(readerSaldo["EinnahmeBrutto"]) : null;
                decimal? ausgabe = readerSaldo["AusgabeBrutto"] != DBNull.Value ? Convert.ToDecimal(readerSaldo["AusgabeBrutto"]) : null;

                saldo += einnahme ?? 0;
                saldo -= ausgabe ?? 0;
            }

            // Anzeige aktualisieren
            dataGrid.ItemsSource = eintraege;
            ApplyDefaultSort();
            txtKontostand.Text = saldo.ToString("F2") + " €";
        }

        private void ApplyDefaultSort()
        {
            var view = CollectionViewSource.GetDefaultView(dataGrid.ItemsSource);
            if (view == null)
                return;

            using (view.DeferRefresh())
            {
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription(nameof(KassenbuchEintrag.DatumSort), ListSortDirection.Descending));
            }
        }


        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            string? monatText = (cmbMonat.SelectedItem as ComboBoxItem)?.Content.ToString();
            string? jahrText = (cmbJahr.SelectedItem as ComboBoxItem)?.Content.ToString();

            if (string.IsNullOrWhiteSpace(jahrText))
            {
                MessageBox.Show("Bitte ein Jahr auswählen.");
                return;
            }

            string csvDateiname = $"Kassenbuch_{jahrText}_{monatText}.csv";
            string zipDateiname = Path.ChangeExtension(csvDateiname, ".zip");
            string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);

            string csvPath = Path.Combine(tempDir, csvDateiname);
            var eintraege = new List<string>();
            eintraege.Add("Datum;Einnahme (€);Käufer/Zweck;Artikel;Ausgabe (€);Zweck der Ausgabe;Bezahlmethode;Beleg");

            var belegDateien = new List<string>();

            // Export aus der aktuellen Ansicht (inkl. Sortierung wie im Grid)
            var view = CollectionViewSource.GetDefaultView(dataGrid.ItemsSource);

            foreach (var item in view)
            {
                if (item is not KassenbuchEintrag row)
                    continue;

                // Datum im Export: du hast im Grid Datum als "dd.MM.yyyy"
                string datum = row.Datum ?? "";

                // Beträge: du hast "12.34 €" als Text im Grid -> wir exportieren ohne " €"
                string einnahme = (row.EinnahmeBrutto ?? "").Replace(" €", "");
                string ausgabe = (row.AusgabeBrutto ?? "").Replace(" €", "");

                string zweckE = row.KäuferZweck ?? "";
                string artikel = row.VerkaufterArtikel ?? "";
                string zweckA = row.ZweckDerAusgabe ?? "";
                string methode = row.Bezahlmethode ?? "";

                // Belege: NEU -> statt BelegPfad (alt) packen wir jetzt ALLE Belege ins ZIP
                // und schreiben in die CSV z.B. "2 Belege" oder die Dateinamenliste.
                string belegInfo = "";

                // Beleg-Dateien einsammeln (alle Belege zu diesem KassenbuchId)
                var files = GetBelegDateienForKassenbuchId(row.Id);
                if (files.Count > 0)
                {
                    belegInfo = $"{files.Count} Beleg(e)";
                    foreach (var f in files)
                    {
                        if (File.Exists(f))
                        {
                            string zielPfad = Path.Combine(tempDir, Path.GetFileName(f));
                            File.Copy(f, zielPfad, overwrite: true);
                            belegDateien.Add(zielPfad);
                        }
                    }
                }

                eintraege.Add($"{datum};{einnahme};{zweckE};{artikel};{ausgabe};{zweckA};{methode};{belegInfo}");
            }


            // CSV schreiben
            File.WriteAllLines(csvPath, eintraege, Encoding.UTF8);

            // ZIP erstellen
            SaveFileDialog dlg = new SaveFileDialog
            {
                Filter = "ZIP-Datei (*.zip)|*.zip",
                FileName = zipDateiname
            };

            if (dlg.ShowDialog() == true)
            {
                if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
                ZipFile.CreateFromDirectory(tempDir, dlg.FileName);
                MessageBox.Show("Export erfolgreich als ZIP!", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            // Aufräumen
            Directory.Delete(tempDir, recursive: true);
        }


        private List<string> GetBelegDateienForKassenbuchId(int kassenbuchId)
        {
            var list = new List<string>();

            using var con = new SQLiteConnection(AppConfig.ConnectionString);
            con.Open();

            using var cmd = new SQLiteCommand("SELECT RelPfad FROM Belege WHERE KassenbuchId = @Id", con);
            cmd.Parameters.AddWithValue("@Id", kassenbuchId);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var relPfad = rd["RelPfad"]?.ToString();
                if (string.IsNullOrWhiteSpace(relPfad))
                    continue;

                var absolutePfad = Path.Combine(
                    BelegStorage.BaseFolder,
                    relPfad.Replace('/', Path.DirectorySeparatorChar));

                list.Add(absolutePfad);
            }

            return list;
        }


        private static bool TryParseAmount(string? input, out decimal betrag)
        {
            // Korrigiert die Betragseingabe kulturunabhängig:
            // - bevorzugt deutsches Format ("," als Dezimaltrenner)
            // - akzeptiert auch "." als Dezimaltrenner
            // - verhindert implizite Skalierung (z.B. 10 -> 1000)
            betrag = 0m;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var trimmed = input.Trim();
            var german = new CultureInfo("de-DE");
            if (decimal.TryParse(trimmed, NumberStyles.Number, german, out betrag))
                return true;

            var normalized = trimmed.Replace(',', '.');
            return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out betrag);
        }

        private void BtnEinnahmeSpeichern_Click(object sender, RoutedEventArgs e)
        {
            if (TryParseAmount(txtEinnahme.Text, out decimal betrag))
            {
                using var conn = GetConnection();
                conn.Open();

                var cmd = new SQLiteCommand(@"INSERT INTO Kassenbuch (Datum, EinnahmeBrutto, KäuferZweck, VerkaufterArtikel, Bezahlmethode) 
                                              VALUES (@Datum, @Einnahme, @Zweck, @Artikel, @Methode);
                                              SELECT last_insert_rowid();", conn);
                cmd.Parameters.AddWithValue("@Datum", datePickerEinnahme.SelectedDate?.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@Einnahme", betrag);
                cmd.Parameters.AddWithValue("@Zweck", txtZweckEinnahme.Text);
                cmd.Parameters.AddWithValue("@Artikel", txtArtikel.Text);
                cmd.Parameters.AddWithValue("@Methode", (comboBezahlmethodeEinnahme.SelectedItem as ComboBoxItem)?.Content?.ToString());

                var eintragId = Convert.ToInt32(cmd.ExecuteScalar());
                SaveBelegeForEntry(eintragId, "Einnahme", ausgewählteBelegeEinnahme, datePickerEinnahme.SelectedDate);
                LadeKassenbuch();

                txtEinnahme.Text = "";
                txtZweckEinnahme.Text = "";
                txtArtikel.Text = "";
                comboBezahlmethodeEinnahme.SelectedIndex = -1;
                ausgewählteBelegeEinnahme.Clear();
                txtBelegEinnahme.Text = "Kein Beleg ausgewählt";
            }
            else
            {
                MessageBox.Show("Bitte einen gültigen Betrag eingeben.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnBelegEinnahme_Click(object sender, RoutedEventArgs e)
        {
            ausgewählteBelegeEinnahme.Clear();
            ausgewählteBelegeEinnahme.AddRange(SelectBelegDateien());
            txtBelegEinnahme.Text = GetBelegStatusText(ausgewählteBelegeEinnahme.Count);
        }

        private void BtnBelegAusgabe_Click(object sender, RoutedEventArgs e)
        {
            ausgewählteBelegeAusgabe.Clear();
            ausgewählteBelegeAusgabe.AddRange(SelectBelegDateien());
            txtBelegAusgabe.Text = GetBelegStatusText(ausgewählteBelegeAusgabe.Count);
        }

        private List<string> SelectBelegDateien()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PDF-Dateien (*.pdf)|*.pdf|Bilder (*.jpg;*.png)|*.jpg;*.png|Alle Dateien (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                return new List<string>(dialog.FileNames);
            }

            return new List<string>();
        }

        private static string GetBelegStatusText(int anzahl)
        {
            return anzahl switch
            {
                0 => "Kein Beleg ausgewählt",
                1 => "1 Beleg ausgewählt",
                _ => $"{anzahl} Belege ausgewählt"
            };
        }

        private void SaveBelegeForEntry(int kassenbuchId, string seite, List<string> dateien, DateTime? datum)
        {
            if (dateien.Count == 0)
                return;

            using var con = new SQLiteConnection(AppConfig.ConnectionString);
            con.Open();

            var now = datum ?? DateTime.Now;
            var targetDir = BelegStorage.EnsureEntryFolder(now.Year, now.Month, kassenbuchId);

            int laufNr = 0;
            foreach (var srcPath in dateien)
            {
                if (!File.Exists(srcPath))
                    continue;

                var ext = Path.GetExtension(srcPath);
                var baseName = Path.GetFileName(srcPath);
                var md5Src = BelegStorage.ComputeMd5(srcPath);

                using (var dup = con.CreateCommand())
                {
                    dup.CommandText = "SELECT COUNT(1) FROM Belege WHERE KassenbuchId=@kid AND Seite=@seite AND HashMd5=@md5";
                    dup.Parameters.AddWithValue("@kid", kassenbuchId);
                    dup.Parameters.AddWithValue("@seite", seite);
                    dup.Parameters.AddWithValue("@md5", md5Src);

                    if (Convert.ToInt32(dup.ExecuteScalar()) > 0)
                    {
                        continue;
                    }
                }

                int idx = ++laufNr;
                string fileName;
                string dstPath;
                while (true)
                {
                    fileName = $"{now:yyyy-MM-dd}_{kassenbuchId}_{idx:D2}{ext}";
                    dstPath = Path.Combine(targetDir, fileName);
                    if (!File.Exists(dstPath))
                        break;
                    idx++;
                }
                laufNr = idx;

                File.Copy(srcPath, dstPath, overwrite: false);

                var relPath = Path.GetRelativePath(BelegStorage.BaseFolder, dstPath).Replace('\\', '/');

                using var ins = con.CreateCommand();
                ins.CommandText = @"
                INSERT INTO Belege
                    (KassenbuchId, Seite, Originalname, Dateiname, RelPfad, HashMd5, HinzugefuegtAm)
                VALUES
                    (@kid, @seite, @orig, @datei, @rel, @md5, strftime('%Y-%m-%dT%H:%M:%SZ','now'))";
                ins.Parameters.AddWithValue("@kid", kassenbuchId);
                ins.Parameters.AddWithValue("@seite", seite);
                ins.Parameters.AddWithValue("@orig", baseName);
                ins.Parameters.AddWithValue("@datei", fileName);
                ins.Parameters.AddWithValue("@rel", relPath);
                ins.Parameters.AddWithValue("@md5", md5Src);
                ins.ExecuteNonQuery();
            }
        }


        private void BtnAusgabeSpeichern_Click(object sender, RoutedEventArgs e)
        {
            if (TryParseAmount(txtAusgabe.Text, out decimal betrag))
            {
                using var conn = GetConnection();
                conn.Open();

                var cmd = new SQLiteCommand(@"INSERT INTO Kassenbuch (Datum, AusgabeBrutto, ZweckDerAusgabe, KäuferZweck, Bezahlmethode, BelegPfad) 
                                              VALUES (@Datum, @Ausgabe, @Zweck, @Kaeufer, @Methode, @Beleg);
                                              SELECT last_insert_rowid();", conn);
                cmd.Parameters.AddWithValue("@Datum", datePickerAusgabe.SelectedDate?.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@Ausgabe", betrag);
                cmd.Parameters.AddWithValue("@Zweck", txtZweckAusgabe.Text);
                cmd.Parameters.AddWithValue("@Methode", (comboBezahlmethode.SelectedItem as ComboBoxItem)?.Content?.ToString());
                cmd.Parameters.AddWithValue("@Beleg", DBNull.Value);
                cmd.Parameters.AddWithValue("@Kaeufer", txtKaeuferAusgabe.Text);

                var eintragId = Convert.ToInt32(cmd.ExecuteScalar());
                SaveBelegeForEntry(eintragId, "Ausgabe", ausgewählteBelegeAusgabe, datePickerAusgabe.SelectedDate);
                LadeKassenbuch();

                txtAusgabe.Text = "";
                txtZweckAusgabe.Text = "";
                txtKaeuferAusgabe.Text = "";
                comboBezahlmethode.SelectedIndex = -1;
                ausgewählteBelegeAusgabe.Clear();
                txtBelegAusgabe.Text = "Kein Beleg ausgewählt";
            }
            else
            {
                MessageBox.Show("Bitte einen gültigen Betrag eingeben.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnBearbeiten_Click(object sender, RoutedEventArgs e)
        {
            if (dataGrid.SelectedItem is KassenbuchEintrag eintrag)
            {
                var bearbeitungsFenster = new BearbeitenWindow(eintrag);
                bearbeitungsFenster.ShowDialog();
                if (bearbeitungsFenster.DialogResult == true)
                {
                    using var conn = GetConnection();
                    conn.Open();

                    var cmd = new SQLiteCommand(@"UPDATE Kassenbuch SET 
                        Datum = @Datum, 
                        EinnahmeBrutto = @Einnahme, 
                        KäuferZweck = @ZweckE, 
                        VerkaufterArtikel = @Artikel, 
                        AusgabeBrutto = @Ausgabe, 
                        ZweckDerAusgabe = @ZweckA, 
                        Bezahlmethode = @Methode 
                        WHERE Id = @Id", conn);

                    cmd.Parameters.AddWithValue("@Datum", bearbeitungsFenster.GeänderterEintrag.Datum);
                    cmd.Parameters.AddWithValue("@Einnahme", string.IsNullOrWhiteSpace(bearbeitungsFenster.GeänderterEintrag.EinnahmeBrutto) ? (object)DBNull.Value : TryParseAmount(bearbeitungsFenster.GeänderterEintrag.EinnahmeBrutto, out var einnahme) ? einnahme : (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ZweckE", bearbeitungsFenster.GeänderterEintrag.KäuferZweck);
                    cmd.Parameters.AddWithValue("@Artikel", bearbeitungsFenster.GeänderterEintrag.VerkaufterArtikel);
                    cmd.Parameters.AddWithValue("@Ausgabe", string.IsNullOrWhiteSpace(bearbeitungsFenster.GeänderterEintrag.AusgabeBrutto) ? (object)DBNull.Value : TryParseAmount(bearbeitungsFenster.GeänderterEintrag.AusgabeBrutto, out var ausgabe) ? ausgabe : (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ZweckA", bearbeitungsFenster.GeänderterEintrag.ZweckDerAusgabe);
                    cmd.Parameters.AddWithValue("@Methode", bearbeitungsFenster.GeänderterEintrag.Bezahlmethode);
                    cmd.Parameters.AddWithValue("@Id", eintrag.Id);

                    cmd.ExecuteNonQuery();
                    LadeKassenbuch();
                }
            }
        }

        private void BtnLoeschen_Click(object sender, RoutedEventArgs e)
        {
            if (dataGrid.SelectedItem is KassenbuchEintrag eintrag)
            {
                if (MessageBox.Show("Eintrag wirklich löschen?", "Bestätigung", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    using var conn = GetConnection();
                    conn.Open();
                    var cmd = new SQLiteCommand("DELETE FROM Kassenbuch WHERE Id = @Id", conn);
                    cmd.Parameters.AddWithValue("@Id", eintrag.Id);
                    cmd.ExecuteNonQuery();
                    LadeKassenbuch();
                }
            }
        }

        private void BelegAnzeigen_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.DataContext is KassenbuchEintrag eintrag)
            {
                if (!string.IsNullOrWhiteSpace(eintrag.BelegPfad) && File.Exists(eintrag.BelegPfad))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = eintrag.BelegPfad,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Fehler beim Öffnen des Belegs:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Beleg wurde nicht gefunden oder ist ungültig.", "Beleg fehlt", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        private void OpenBelegeForRow(int kassenbuchId, bool istEinnahme)
        {
            var seite = istEinnahme ? "Einnahme" : "Ausgabe";
            var win = new BelegeWindow(kassenbuchId, seite) { Owner = this };
            win.ShowDialog();
        }

        private void BelegKlammer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                // Aktuelle Zeile ermitteln
                var row = btn.DataContext as KassenbuchEintrag;

                // Seite bestimmen (wenn du ein explizites Feld hast, nutze das)
                string seite = "Ausgabe";
                try
                {
                    bool hatEinnahme = !string.IsNullOrWhiteSpace(row?.EinnahmeBrutto);
                    bool hatAusgabe = !string.IsNullOrWhiteSpace(row?.AusgabeBrutto);
                    seite = (hatEinnahme && !hatAusgabe) ? "Einnahme" : "Ausgabe";
                }
                catch { /* bleibt 'Ausgabe' */ }

                var win = new BelegeWindow(id, seite) { Owner = this };
                win.ShowDialog();

                // Beleg-Zähler aktualisieren
                var counts = LadeAlleBelegCounts();
                counts.TryGetValue(id, out var c);
                if (row != null) row.BelegCount = c;
                dataGrid.Items.Refresh();
            }
        }

        private void dataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}
