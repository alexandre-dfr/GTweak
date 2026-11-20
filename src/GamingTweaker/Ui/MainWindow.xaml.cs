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
            PreviewKeyDown += Raccourcis;
            MajIconeTheme();
            ThemeApplication.Change += MajIconeTheme;
            Closed += (s, e) => ThemeApplication.Change -= MajIconeTheme;
        }

        /// <summary>Icône du bouton de thème : palette (comme Windows), soleil (clair) ou lune (sombre) ; coche sur le choix actif.</summary>
        private void MajIconeTheme()
        {
            var c = ThemeApplication.Choix;
            IconeTheme.Text = c == ChoixTheme.Clair ? "\uE706" : c == ChoixTheme.Sombre ? "\uE708" : "\uE790";
            BoutonTheme.ToolTip = "Thème : " + (c == ChoixTheme.Clair ? "clair" : c == ChoixTheme.Sombre ? "sombre" : "comme Windows");
            foreach (var o in MenuTheme.Items)
            {
                var m = o as System.Windows.Controls.MenuItem;
                if (m != null) m.IsChecked = (string)m.Tag == c.ToString();
            }
        }

        private void BoutonTheme_Click(object sender, RoutedEventArgs e)
        {
            MenuTheme.PlacementTarget = BoutonTheme;
            MenuTheme.IsOpen = true;
        }

        private CompteurReglagesWindow reglagesCompteur;

        /// <summary>Réglages du compteur, non modaux : une fenêtre modale bloquerait le glisser-déposer du compteur.</summary>
        private void BoutonCompteur_Click(object sender, RoutedEventArgs e)
        {
            if (vm.Compteur == null) return;
            if (reglagesCompteur != null) { reglagesCompteur.Activate(); return; }
            reglagesCompteur = new CompteurReglagesWindow(vm.Compteur) { Owner = this };
            reglagesCompteur.Closed += (s, ev) => reglagesCompteur = null;
            reglagesCompteur.Show();
        }

        private void ChoixTheme_Click(object sender, RoutedEventArgs e)
        {
            var m = sender as System.Windows.Controls.MenuItem;
            ChoixTheme c;
            if (m != null && Enum.TryParse((string)m.Tag, out c)) ThemeApplication.Definir(c);
        }

        /// <summary>Ctrl+F : recherche ; Échap dans la recherche : l'efface.</summary>
        private void Raccourcis(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.F && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
            {
                Recherche.Focus(); Recherche.SelectAll(); e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape && Recherche.IsKeyboardFocusWithin && !string.IsNullOrEmpty(vm.Recherche))
            {
                vm.Recherche = ""; e.Handled = true;
            }
        }

        private void FermerMessage_Click(object sender, RoutedEventArgs e) { vm.Message = ""; }

        /// <summary>Dialogue de confirmation d'une option Risqué.</summary>
        public static bool ConfirmerRisque(Window owner, OptionViewModel o)
        {
            return MessageBox.Show(owner, o.TexteConfirmation, "Option risquée", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        private void Sauver_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new TexteWindow("Enregistrer le profil", "Nom du profil", vm.NomPourEnregistrer) { Owner = this };
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

        // ---------- Partage de profil (fichier .json) ----------

        private const string FiltreProfil = "Profil GamingTweaker (*.json)|*.json";

        private void BoutonProfil_Click(object sender, RoutedEventArgs e)
        {
            MenuProfil.PlacementTarget = BoutonProfil;
            // Le menu contextuel n'hérite pas du DataContext de la fenêtre quand il est ouvert par un clic gauche.
            MenuProfil.DataContext = vm;
            MenuProfil.IsOpen = true;
        }

        private void ExporterProfil_Click(object sender, RoutedEventArgs e)
        {
            var nom = MainViewModel.NettoyerNom(vm.NomPourEnregistrer);
            var d = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exporter la sélection", Filter = FiltreProfil, DefaultExt = ".json", AddExtension = true, OverwritePrompt = true,
                FileName = nom.Length > 0 ? nom : "Mon profil",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            };
            if (d.ShowDialog(this) != true) return;
            var err = vm.ExporterProfil(d.FileName);
            if (err != null) MessageBox.Show(this, err, "Exporter un profil", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void ImporterProfil_Click(object sender, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Importer un profil", Filter = FiltreProfil, CheckFileExists = true };
            if (d.ShowDialog(this) != true) return;
            var err = vm.ImporterProfil(d.FileName, nom => MessageBox.Show(this,
                "Un profil « " + nom + " » existe déjà. Le remplacer par le profil importé ?", "Importer un profil",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes);
            if (err != null) MessageBox.Show(this, err, "Importer un profil", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ---------- Nouvelle version ----------

        private void VoirNouvelleVersion_Click(object sender, RoutedEventArgs e)
        {
            var url = vm.NouvelleVersion == null ? null : vm.NouvelleVersion.Url;
            if (!Core.SystemExe.UrlAcceptee(url)) return;
            try { Core.SystemExe.OuvrirUrl(url); }
            catch (Exception ex) { vm.Message = "Impossible d'ouvrir le navigateur (" + ex.Message + "). Adresse : " + url; }
        }

        private void MasquerNouvelleVersion_Click(object sender, RoutedEventArgs e) { vm.NouvelleVersionMasquee = true; }

        private void Planifier_Click(object sender, RoutedEventArgs e)
        {
            if (vm.Routine == null) return;
            new PlanifierWindow(vm.Routine, vm.ProfilSelectionne) { Owner = this }.ShowDialog();
        }

        private async void ToutRestaurer_Click(object sender, RoutedEventArgs e)
        {
            var ok = MessageBox.Show(this,
                "Toutes les options appliquées par GamingTweaker, dans tous les menus, vont être annulées : par le journal de leurs exécutions, sinon par un retour aux valeurs par défaut de Windows. "
                + "Les nettoyages (définitifs) et les mises à jour installées ne sont pas concernés.\r\n\r\nContinuer ?",
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
        public const string BandeauPortable = "PC portable détecté : les options d'alimentation (plans Performances, ASPM, veille des périphériques, veille prolongée) augmentent la consommation sur batterie. Appliquez-les branché sur secteur, ou laissez-les décochées.";

        public void LancerControlesDemarrage(DataPaths paths, MachineFacts facts = null)
        {
            if (facts != null && facts.EstPortable) vm.Bandeau = BandeauPortable;
            Task.Run(() => ElevationCheck.SameUserAsDesktop()).ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion && !t.Result)
                    vm.Bandeau = ("Les options du profil utilisateur seront ignorées : l'application est exécutée sous un autre compte administrateur. " + vm.Bandeau).Trim();
            }, TaskScheduler.FromCurrentSynchronizationContext());

            MiseAJour.VerifierAsync().ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion && t.Result != null) vm.NouvelleVersion = t.Result;
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
