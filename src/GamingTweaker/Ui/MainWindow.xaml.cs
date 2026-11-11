using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using GamingTweaker.Core;

namespace GamingTweaker.Ui
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel vm;

        public MainWindow(MainViewModel vm)
        {
            this.vm = vm;
            InitializeComponent();
            DataContext = vm;
        }

        /// <summary>Dialogue de confirmation d'une option Risqué.</summary>
        public static bool ConfirmerRisque(Window owner, OptionViewModel o)
        {
            return MessageBox.Show(owner, o.TexteConfirmation, "Option risquée", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        private void Sauver_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new TexteWindow("Enregistrer le profil", "Nom du profil", vm.ProfilSelectionne ?? "") { Owner = this };
            if (dlg.ShowDialog() != true) return;
            var err = vm.SauverProfil(dlg.Valeur);
            if (err != null) MessageBox.Show(this, err, "Profil", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // « Aperçu » et « LANCER » ouvrent le même aperçu ; son bouton LANCER démarre l'exécution.
        private void Apercu_Click(object sender, RoutedEventArgs e) { ApercuPuisExecuter(); }
        private void Lancer_Click(object sender, RoutedEventArgs e) { ApercuPuisExecuter(); }

        private void ApercuPuisExecuter() { if (OuvrirApercu()) Executer(); }

        /// <summary>La liste d'éléments défile seule tant qu'elle peut ; au bout, la molette fait défiler la page.</summary>
        private void ListeScroll_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            var sv = (System.Windows.Controls.ScrollViewer)sender;
            var versHaut = e.Delta > 0;
            if ((versHaut && sv.VerticalOffset <= 0) || (!versHaut && sv.VerticalOffset >= sv.ScrollableHeight))
            {
                e.Handled = true;
                var parent = sv.Parent as UIElement;
                if (parent != null)
                    parent.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender });
            }
        }

        private void Recharger_Click(object sender, RoutedEventArgs e) { vm.RechargerProfil(); }

        private async void ToutRestaurer_Click(object sender, RoutedEventArgs e)
        {
            var ok = MessageBox.Show(this,
                "Toutes les options du menu Télémétrie déjà appliquées vont être annulées : par le journal de leur dernière exécution, sinon par un retour aux valeurs par défaut de Windows.\r\n\r\nContinuer ?",
                "Tout restaurer", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (ok != MessageBoxResult.Yes) return;
            try { await vm.ToutRestaurerAsync(); }
            catch (Exception ex) { vm.Message = "Tout restaurer a échoué : " + ex.Message; }
        }

        private void ShutUp_Click(object sender, RoutedEventArgs e)
        {
            try { Core.SystemExe.OuvrirUrl(MainViewModel.UrlShutUp10); }
            catch (Exception ex) { vm.Message = "Impossible d'ouvrir le navigateur (" + ex.Message + "). Adresse : " + MainViewModel.UrlShutUp10; }
        }

        /// <summary>Ouvre la page officielle proposée par un fournisseur de liste (https uniquement).</summary>
        private void OuvrirLien_Click(object sender, RoutedEventArgs e)
        {
            var url = (sender as System.Windows.Controls.Button)?.Tag as string;
            if (!Core.SystemExe.UrlAcceptee(url)) return;
            try { Core.SystemExe.OuvrirUrl(url); }
            catch (Exception ex) { vm.Message = "Impossible d'ouvrir le navigateur (" + ex.Message + "). Adresse : " + url; }
        }

        private bool OuvrirApercu()
        {
            if (vm.SelectedCount == 0) return false;
            return new PreviewWindow(vm) { Owner = this }.ShowDialog() == true;
        }

        private void Executer()
        {
            vm.ConfirmSansPointDeRestauration = msg => (bool)Dispatcher.Invoke(new Func<bool>(() =>
                MessageBox.Show(this, msg + "\r\n\r\nContinuer sans point de restauration ?", "Point de restauration", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes));
            new RunWindow(vm) { Owner = this }.ShowDialog();
            var _ = vm.DetectAsync();
        }

        /// <summary>Contrôles de démarrage (appelés par l'application, pas par les tests).</summary>
        public void LancerControlesDemarrage(DataPaths paths)
        {
            Task.Run(() => ElevationCheck.SameUserAsDesktop()).ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion && !t.Result)
                    vm.Bandeau = "Les options du profil utilisateur seront ignorées : l'application est exécutée sous un autre compte administrateur.";
            }, TaskScheduler.FromCurrentSynchronizationContext());

            SessionInterrompue s = null;
            try { s = RunPipeline.FindInterrupted(paths.MachineDir); } catch (Exception) { }
            if (s != null)
                MessageBox.Show(this, "Une session précédente (" + s.Debut.ToString("dd/MM/yyyy HH:mm") + ") s'est arrêtée avant la fin. "
                    + "Ses modifications restent annulables depuis l'historique.\r\n\r\nDossier : " + s.SessionDir,
                    "Session interrompue", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
