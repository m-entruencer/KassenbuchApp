using System.Windows;

namespace KassenbuchApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Beim Start einmal prüfen, ob alle Tabellen vorhanden sind
            DbMigrations.RunMigrations();
        }
    }
}
