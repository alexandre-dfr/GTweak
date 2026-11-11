using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace GamingTweaker.Ui
{
    public partial class PreviewWindow : Window
    {
        private readonly MainViewModel vm;
        public PreviewWindow(MainViewModel vm)
        {
            InitializeComponent();
            var rows = vm.BuildPreview();
            Lignes.ItemsSource = rows;
            Titre.Text = "Aperçu du script";
            Sous.Text = rows.Count + (rows.Count > 1 ? " options sélectionnées. " : " option sélectionnée. ") + Sous.Text;
            if (rows.Any(r => r.Reversibilite == "définitif"))
                Sous.Text += " Les options marquées « définitif » ne pourront pas être annulées.";
            this.vm = vm;
            try { Script.Text = vm.BuildPreviewScript(); }
            catch (Exception ex) { Script.Text = "Le script n'a pas pu être généré : " + ex.Message; Exporter.IsEnabled = false; Lancer.IsEnabled = false; }
            Loaded += async (s, e) => await EstimerAsync(rows);
        }

        // Les estimations (scripts en lecture seule) tournent après l'ouverture : la fenêtre reste utilisable, les tailles apparaissent toutes ensemble à la fin des estimations.
        private async System.Threading.Tasks.Task EstimerAsync(System.Collections.Generic.IReadOnlyList<PreviewRow> rows)
        {
            try
            {
                if (!rows.Any()) return;
                Total.Text = "Estimation des tailles…";
                Total.Text = await vm.EstimerAsync(rows);
            }
            catch (Exception) { Total.Text = ""; }
        }

        private void Exporter_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Title = "Exporter le script", Filter = "Script PowerShell (*.ps1)|*.ps1", FileName = "GamingTweaker.ps1", DefaultExt = ".ps1" };
            if (dlg.ShowDialog(this) != true) return;
            try { File.WriteAllText(dlg.FileName, vm.BuildExportScript(), new UTF8Encoding(true)); }
            catch (Exception ex) { MessageBox.Show(this, "Écriture impossible : " + ex.Message, "Exporter", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void Lancer_Click(object sender, RoutedEventArgs e) { DialogResult = true; }
    }
}
