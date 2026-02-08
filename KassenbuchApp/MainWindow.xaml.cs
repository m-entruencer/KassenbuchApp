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

            if (!int.TryParse(jahrText, out var jahr))
            {
                MessageBox.Show("Ungültiges Jahr ausgewählt.");
                return;
            }

            var monateZumExport = GetMonthsToExport(jahr, cmbMonat.SelectedIndex, monatText);
            if (monateZumExport.Count == 0)
            {
                MessageBox.Show("Keine Daten für den Export gefunden.");
                return;
            }

            string zipDateiname = monateZumExport.Count == 1
                ? $"Kassenbuch_{jahr}_{monateZumExport[0]:D2}.zip"
                : $"Kassenbuch_{jahr}_AlleMonate.zip";
            string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);

            foreach (var monat in monateZumExport)
            {
                var targetDir = monateZumExport.Count == 1
                    ? tempDir
                    : Path.Combine(tempDir, $"{jahr}-{monat:D2}");

                Directory.CreateDirectory(targetDir);
                var (anzahlBuchungen, anzahlBelege) = ExportMonthToFolder(jahr, monat, targetDir);
                Debug.WriteLine($"Exporting month {jahr}-{monat:D2}: {anzahlBuchungen} bookings, {anzahlBelege} receipts");
            }

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

        private sealed class ExportBooking
        {
            public int Id { get; set; }
            public DateTime? Datum { get; set; }
            public decimal? EinnahmeBrutto { get; set; }
            public string? KäuferZweck { get; set; }
            public string? VerkaufterArtikel { get; set; }
            public decimal? AusgabeBrutto { get; set; }
            public string? ZweckDerAusgabe { get; set; }
            public string? Bezahlmethode { get; set; }
        }

        private sealed class BelegInfo
        {
            public string Originalname { get; set; } = "";
            public string AbsolutePath { get; set; } = "";
        }

        private List<int> GetMonthsToExport(int year, int selectedIndex, string? monatText)
        {
            bool exportAllMonths = selectedIndex <= 0
                || string.Equals(monatText, "Alle Monate", StringComparison.OrdinalIgnoreCase);

            if (!exportAllMonths)
            {
                if (selectedIndex > 0 && selectedIndex <= 12)
                {
                    return new List<int> { selectedIndex };
                }

                if (TryParseMonth(monatText, out var monthFromText))
                {
                    return new List<int> { monthFromText };
                }
            }

            var months = new List<int>();
            using var con = new SQLiteConnection(AppConfig.ConnectionString);
            con.Open();
            using var cmd = new SQLiteCommand(@"
                SELECT DISTINCT CAST(strftime('%m', Datum) AS INTEGER) AS Monat
                FROM Kassenbuch
                WHERE strftime('%Y', Datum) = @Jahr
                ORDER BY Monat;", con);
            cmd.Parameters.AddWithValue("@Jahr", year.ToString());
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                months.Add(rd.GetInt32(0));
            }

            return months;
        }

        private bool TryParseMonth(string? monthText, out int month)
        {
            month = 0;
            if (string.IsNullOrWhiteSpace(monthText))
                return false;

            for (int m = 1; m <= 12; m++)
            {
                var name = new DateTime(2000, m, 1).ToString("MMMM", CultureInfo.CurrentCulture);
                if (string.Equals(name, monthText, StringComparison.CurrentCultureIgnoreCase))
                {
                    month = m;
                    return true;
                }
            }

            return false;
        }

        private (int bookings, int receipts) ExportMonthToFolder(int year, int month, string targetDir)
        {
            var csvDateiname = $"Kassenbuch_{year}_{month:D2}.csv";
            var csvPath = Path.Combine(targetDir, csvDateiname);
            var eintraege = new List<string>
            {
                "Datum;Einnahme (€);Käufer/Zweck;Artikel;Ausgabe (€);Zweck der Ausgabe;Bezahlmethode;Beleg"
            };

            var receiptsDir = Path.Combine(targetDir, "Belege");
            Directory.CreateDirectory(receiptsDir);

            var bookings = GetBookingsForMonth(year, month);
            int receiptCount = 0;

            foreach (var booking in bookings)
            {
                string datum = booking.Datum?.ToString("dd.MM.yyyy") ?? "";
                string einnahme = booking.EinnahmeBrutto?.ToString("F2", CultureInfo.InvariantCulture) ?? "";
                string ausgabe = booking.AusgabeBrutto?.ToString("F2", CultureInfo.InvariantCulture) ?? "";
                string zweckE = booking.KäuferZweck ?? "";
                string artikel = booking.VerkaufterArtikel ?? "";
                string zweckA = booking.ZweckDerAusgabe ?? "";
                string methode = booking.Bezahlmethode ?? "";

                var belegInfo = "";
                var belegAnzahl = CopyReceiptsForBooking(booking.Id, receiptsDir);
                if (belegAnzahl > 0)
                {
                    belegInfo = $"{belegAnzahl} Beleg(e)";
                    receiptCount += belegAnzahl;
                }

                eintraege.Add($"{datum};{einnahme};{zweckE};{artikel};{ausgabe};{zweckA};{methode};{belegInfo}");
            }

            File.WriteAllLines(csvPath, eintraege, Encoding.UTF8);
            return (bookings.Count, receiptCount);
        }

        private List<ExportBooking> GetBookingsForMonth(int year, int month)
        {
            var bookings = new List<ExportBooking>();
            using var con = new SQLiteConnection(AppConfig.ConnectionString);
            con.Open();
            using var cmd = new SQLiteCommand(@"
                SELECT Id, Datum, EinnahmeBrutto, KäuferZweck, VerkaufterArtikel,
                       AusgabeBrutto, ZweckDerAusgabe, Bezahlmethode
                FROM Kassenbuch
                WHERE strftime('%Y', Datum) = @Jahr
                  AND strftime('%m', Datum) = @Monat
                ORDER BY Datum DESC, Id DESC;", con);
            cmd.Parameters.AddWithValue("@Jahr", year.ToString());
            cmd.Parameters.AddWithValue("@Monat", month.ToString("D2"));
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var datumText = rd["Datum"]?.ToString();
                DateTime? datum = null;
                if (DateTime.TryParse(datumText, out var parsed))
                {
                    datum = parsed;
                }

                bookings.Add(new ExportBooking
                {
                    Id = Convert.ToInt32(rd["Id"]),
                    Datum = datum,
                    EinnahmeBrutto = rd["EinnahmeBrutto"] == DBNull.Value ? null : Convert.ToDecimal(rd["EinnahmeBrutto"]),
                    KäuferZweck = rd["KäuferZweck"]?.ToString(),
                    VerkaufterArtikel = rd["VerkaufterArtikel"]?.ToString(),
                    AusgabeBrutto = rd["AusgabeBrutto"] == DBNull.Value ? null : Convert.ToDecimal(rd["AusgabeBrutto"]),
                    ZweckDerAusgabe = rd["ZweckDerAusgabe"]?.ToString(),
                    Bezahlmethode = rd["Bezahlmethode"]?.ToString()
                });
            }

            return bookings;
        }

        private int CopyReceiptsForBooking(int kassenbuchId, string receiptsDir)
        {
            var belege = GetBelegeInfosForKassenbuchId(kassenbuchId);
            int index = 0;
            foreach (var beleg in belege)
            {
                if (!File.Exists(beleg.AbsolutePath))
                    continue;

                index++;
                string original = string.IsNullOrWhiteSpace(beleg.Originalname)
                    ? Path.GetFileName(beleg.AbsolutePath)
                    : beleg.Originalname;
                string safeOriginal = SanitizeFileName(original);
                string fileName = $"{kassenbuchId}_{index:D2}_{safeOriginal}";
                string zielPfad = GetUniqueFilePath(receiptsDir, fileName);
                File.Copy(beleg.AbsolutePath, zielPfad, overwrite: false);
            }

            return index;
        }

        private static string GetUniqueFilePath(string folder, string fileName)
        {
            string targetPath = Path.Combine(folder, fileName);
            if (!File.Exists(targetPath))
                return targetPath;

            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            string fallbackName = $"{baseName}_{Guid.NewGuid():N}{extension}";
            return Path.Combine(folder, fallbackName);
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "beleg";

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new StringBuilder(fileName.Length);
            foreach (var ch in fileName)
            {
                sanitized.Append(Array.IndexOf(invalidChars, ch) >= 0 ? '_' : ch);
            }

            return sanitized.ToString();
        }

        private List<BelegInfo> GetBelegeInfosForKassenbuchId(int kassenbuchId)
        {
            var list = new List<BelegInfo>();

            using var con = new SQLiteConnection(AppConfig.ConnectionString);
            con.Open();

            using var cmd = new SQLiteCommand("SELECT Originalname, RelPfad FROM Belege WHERE KassenbuchId = @Id ORDER BY Id", con);
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

                list.Add(new BelegInfo
                {
                    Originalname = rd["Originalname"]?.ToString() ?? "",
                    AbsolutePath = absolutePfad
                });
            }

            return list;
        }

#if DEBUG
        private void RunExportRegressionScenario()
        {
            // Manuelles Debug-Szenario: 2 Monate, je 2 Buchungen, je Buchung 2 Belege.
            // Diese Methode wird NICHT automatisch aufgerufen.
            int year = DateTime.Now.Year;
            int[] months = { 1, 2 };
            var createdBookingIds = new List<int>();
            var createdFiles = new List<string>();
            string exportRoot = Path.Combine(Path.GetTempPath(), $"KassenbuchExportDebug_{Guid.NewGuid():N}");
            Directory.CreateDirectory(exportRoot);

            using var con = new SQLiteConnection(AppConfig.ConnectionString);
            con.Open();
            try
            {
                foreach (var month in months)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        using var cmd = con.CreateCommand();
                        cmd.CommandText = @"
                            INSERT INTO Kassenbuch
                            (Datum, EinnahmeBrutto, KäuferZweck, VerkaufterArtikel, Bezahlmethode, AusgabeBrutto, ZweckDerAusgabe, BelegPfad)
                            VALUES (@Datum, @Einnahme, @Kaeufer, @Artikel, @Methode, NULL, NULL, NULL);";
                        cmd.Parameters.AddWithValue("@Datum", new DateTime(year, month, 1 + i).ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("@Einnahme", 10 + i);
                        cmd.Parameters.AddWithValue("@Kaeufer", "Debug Käufer");
                        cmd.Parameters.AddWithValue("@Artikel", "Debug Artikel");
                        cmd.Parameters.AddWithValue("@Methode", "Bar");
                        cmd.ExecuteNonQuery();

                        using var idCmd = con.CreateCommand();
                        idCmd.CommandText = "SELECT last_insert_rowid();";
                        int bookingId = Convert.ToInt32(idCmd.ExecuteScalar());
                        createdBookingIds.Add(bookingId);

                        var belegDir = BelegStorage.EnsureEntryFolder(year, month, bookingId);
                        for (int b = 1; b <= 2; b++)
                        {
                            string fileName = $"debug_{bookingId}_{b}.txt";
                            string filePath = Path.Combine(belegDir, fileName);
                            File.WriteAllText(filePath, $"Debug-Beleg {bookingId}-{b}");
                            createdFiles.Add(filePath);

                            string relPath = Path.GetRelativePath(BelegStorage.BaseFolder, filePath)
                                .Replace(Path.DirectorySeparatorChar, '/');

                            using var belegCmd = con.CreateCommand();
                            belegCmd.CommandText = @"
                                INSERT INTO Belege
                                (KassenbuchId, Seite, Originalname, Dateiname, RelPfad, HashMd5, HinzugefuegtAm)
                                VALUES (@Kid, 'Einnahme', @Original, @Dateiname, @RelPfad, NULL, @Added);";
                            belegCmd.Parameters.AddWithValue("@Kid", bookingId);
                            belegCmd.Parameters.AddWithValue("@Original", fileName);
                            belegCmd.Parameters.AddWithValue("@Dateiname", fileName);
                            belegCmd.Parameters.AddWithValue("@RelPfad", relPath);
                            belegCmd.Parameters.AddWithValue("@Added", DateTime.UtcNow.ToString("O"));
                            belegCmd.ExecuteNonQuery();
                        }
                    }
                }

                foreach (var month in months)
                {
                    var targetDir = Path.Combine(exportRoot, $"{year}-{month:D2}");
                    Directory.CreateDirectory(targetDir);
                    ExportMonthToFolder(year, month, targetDir);
                }

                Debug.WriteLine($"Regression export scenario completed. Output: {exportRoot}");
            }
            finally
            {
                if (createdBookingIds.Count > 0)
                {
                    using var delBelege = con.CreateCommand();
                    using var delBookings = con.CreateCommand();
                    var idList = new StringBuilder();
                    for (int i = 0; i < createdBookingIds.Count; i++)
                    {
                        string param = "@id" + i;
                        if (i > 0) idList.Append(",");
                        idList.Append(param);
                        delBelege.Parameters.AddWithValue(param, createdBookingIds[i]);
                        delBookings.Parameters.AddWithValue(param, createdBookingIds[i]);
                    }

                    delBelege.CommandText = $"DELETE FROM Belege WHERE KassenbuchId IN ({idList})";
                    delBelege.ExecuteNonQuery();
                    delBookings.CommandText = $"DELETE FROM Kassenbuch WHERE Id IN ({idList})";
                    delBookings.ExecuteNonQuery();
                }

                foreach (var file in createdFiles)
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
            }
        }
#endif


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
