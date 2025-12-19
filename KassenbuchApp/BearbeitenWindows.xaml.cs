// BearbeitenWindow.xaml.cs
using System;
using System.Windows;

namespace KassenbuchApp
{
    public partial class BearbeitenWindow : Window
    {
        public MainWindow.KassenbuchEintrag GeänderterEintrag { get; private set; }

        public BearbeitenWindow(MainWindow.KassenbuchEintrag eintrag)
        {
            InitializeComponent();

            GeänderterEintrag = new MainWindow.KassenbuchEintrag();

            // Felder befüllen
            dpDatum.SelectedDate = DateTime.TryParse(eintrag.Datum, out DateTime d) ? d : DateTime.Now;
            txtEinnahme.Text = eintrag.EinnahmeBrutto?.Replace(" €", "") ?? "";
            txtZweckEinnahme.Text = eintrag.KäuferZweck ?? "";
            txtArtikel.Text = eintrag.VerkaufterArtikel ?? "";
            txtAusgabe.Text = eintrag.AusgabeBrutto?.Replace(" €", "") ?? "";
            txtZweckAusgabe.Text = eintrag.ZweckDerAusgabe ?? "";
            cmbBezahlmethode.Text = eintrag.Bezahlmethode ?? "";
        }

        private void Speichern_Click(object sender, RoutedEventArgs e)
        {
            GeänderterEintrag.Datum = dpDatum.SelectedDate?.ToString("yyyy-MM-dd") ?? "";
            GeänderterEintrag.EinnahmeBrutto = txtEinnahme.Text;
            GeänderterEintrag.KäuferZweck = txtZweckEinnahme.Text;
            GeänderterEintrag.VerkaufterArtikel = txtArtikel.Text;
            GeänderterEintrag.AusgabeBrutto = txtAusgabe.Text;
            GeänderterEintrag.ZweckDerAusgabe = txtZweckAusgabe.Text;
            GeänderterEintrag.Bezahlmethode = cmbBezahlmethode.Text;

            DialogResult = true;
            Close();
        }

        private void Abbrechen_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}