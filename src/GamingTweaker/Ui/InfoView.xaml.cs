using System;
using System.Windows;
using System.Windows.Controls;

namespace GamingTweaker.Ui
{
    public partial class InfoView : UserControl
    {
        public InfoView() { InitializeComponent(); }

        private void Exporter_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as InfoViewModel;
            if (vm == null) return;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exporter les informations", Filter = "Fichier texte (*.txt)|*.txt", FileName = "GamingTweaker-informations.txt"
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try { vm.Exporter(dlg.FileName); }
            catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), "Export impossible : " + ex.Message, "Informations", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }
}
