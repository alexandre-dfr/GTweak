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
        public PreviewWindow(MainViewModel vm)
        {
            InitializeComponent();
            var rows = vm.BuildPreview();
            Lignes.ItemsSource = rows;
            Titre.Text = rows.Count + (rows.Count > 1 ? " options seront appliquées" : " option sera appliquée");
            if (rows.Any(r => r.Reversibilite == "définitif"))
                Sous.Text += " Les options marquées « définitif » ne pourront pas être annulées.";
            try { Script.Text = vm.BuildPreviewScript(); }
            catch (Exception ex) { Script.Text = "Le script n'a pas pu être généré : " + ex.Message; }
        }

        private void Exporter_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Title = "Exporter le script", Filter = "Script PowerShell (*.ps1)|*.ps1", FileName = "GamingTweaker.ps1", DefaultExt = ".ps1" };
            if (dlg.ShowDialog(this) != true) return;
            try { File.WriteAllText(dlg.FileName, Script.Text, new UTF8Encoding(true)); }
            catch (Exception ex) { MessageBox.Show(this, "Écriture impossible : " + ex.Message, "Exporter", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void Lancer_Click(object sender, RoutedEventArgs e) { DialogResult = true; }
    }
}
