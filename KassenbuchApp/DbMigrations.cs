using System.Data.SQLite;

namespace KassenbuchApp
{
    public static class DbMigrations
    {
        public static void RunMigrations()
        {
            using var con = new SQLiteConnection(AppConfig.ConnectionString);
            con.Open();

            // Foreign Keys sicher aktivieren
            using (var fk = con.CreateCommand())
            {
                fk.CommandText = "PRAGMA foreign_keys = ON;";
                fk.ExecuteNonQuery();
            }

            // 1) Tabelle "Belege" anlegen (falls nicht vorhanden)
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Belege (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    KassenbuchId INTEGER NOT NULL,
    Seite TEXT NOT NULL,                  -- 'Einnahme' oder 'Ausgabe'
    Originalname TEXT NOT NULL,           -- z.B. Rechnung_123.pdf
    Dateiname TEXT NOT NULL,              -- interner eindeutiger Name
    RelPfad TEXT NOT NULL,                -- relativer Pfad unterhalb des Beleg-Ordners
    HashMd5 TEXT,                         -- optional für Dubletten
    HinzugefuegtAm TEXT NOT NULL,         -- ISO-8601
    FOREIGN KEY (KassenbuchId) REFERENCES Kassenbuch(Id) ON DELETE CASCADE
);";
                cmd.ExecuteNonQuery();
            }

            // 2) Nur übernehmen, WENN es die Tabelle "Kassenbuch" überhaupt gibt
            bool kassenbuchExists;
            using (var chk = con.CreateCommand())
            {
                chk.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='Kassenbuch' LIMIT 1;";
                kassenbuchExists = chk.ExecuteScalar() != null;
            }

            if (!kassenbuchExists)
            {
                // Nichts zu übernehmen – sauber beenden
                return;
            }

            // 3) (Optional) Vorhandene Einzel-Belege aus Kassenbuch.BelegPfad übernehmen
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO Belege (KassenbuchId, Seite, Originalname, Dateiname, RelPfad, HinzugefuegtAm)
SELECT Id,
       CASE 
           WHEN IFNULL(EinnahmeBrutto,0) <> 0 AND IFNULL(AusgabeBrutto,0) = 0 THEN 'Einnahme'
           ELSE 'Ausgabe'
       END,
       BelegPfad, BelegPfad, BelegPfad, strftime('%Y-%m-%dT%H:%M:%SZ','now')
FROM Kassenbuch
WHERE BelegPfad IS NOT NULL AND BelegPfad <> ''
  AND NOT EXISTS (
      SELECT 1 FROM Belege b
      WHERE b.KassenbuchId = Kassenbuch.Id AND b.RelPfad = Kassenbuch.BelegPfad
  );";
                cmd.ExecuteNonQuery();
            }

            // 4) Bestehende, falsch zugeordnete Belege korrigieren (Einnahme vs. Ausgabe)
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = @"
UPDATE Belege
SET Seite = 'Einnahme'
WHERE Seite = 'Ausgabe'
  AND KassenbuchId IN (
      SELECT Id FROM Kassenbuch
      WHERE IFNULL(EinnahmeBrutto,0) <> 0 AND IFNULL(AusgabeBrutto,0) = 0
  );";
                cmd.ExecuteNonQuery();
            }
        }
    }
}
