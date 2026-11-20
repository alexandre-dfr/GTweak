using System;
using System.Windows;
using System.Windows.Controls;

namespace GamingTweaker.Ui
{
    public partial class InfoView : UserControl
    {
        private InfoViewModel abonne;

        public InfoView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (abonne != null) { abonne.PropertyChanged -= SurChangement; abonne.Sections.CollectionChanged -= SurSections; }
                abonne = DataContext as InfoViewModel;
                if (abonne != null) { abonne.PropertyChanged += SurChangement; abonne.Sections.CollectionChanged += SurSections; }
            };
            IsVisibleChanged += (s, e) => AllerASection();
        }

        private void SurChangement(object sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == "SectionDemandee") AllerASection(); }
        private void SurSections(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) { AllerASection(); }

        /// <summary>Fait défiler la section demandée (tuile de l'accueil) en tête, dès qu'elle est affichée.</summary>
        private void AllerASection()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var vm = DataContext as InfoViewModel;
                if (vm == null || string.IsNullOrEmpty(vm.SectionDemandee) || !IsVisible) return;
                GamingTweaker.Info.InfoSection cible = null;
                foreach (var sec in vm.Sections) if (sec.Titre == vm.SectionDemandee) { cible = sec; break; }
                if (cible == null) return;   // fiche pas encore chargée : nouvel essai à son arrivée
                var conteneur = ListeSections.ItemContainerGenerator.ContainerFromItem(cible) as FrameworkElement;
                if (conteneur == null) return;
                var y = conteneur.TransformToAncestor(Defilement).Transform(new Point(0, 0)).Y;
                Defilement.ScrollToVerticalOffset(Defilement.VerticalOffset + y);
                vm.SectionDemandee = null;
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

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
