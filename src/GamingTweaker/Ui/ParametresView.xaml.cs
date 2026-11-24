using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GamingTweaker.Ui
{
    /// <summary>Menu Paramètres : réglages de l'application (thème, démarrage, guide, version) et du compteur en jeu.</summary>
    public partial class ParametresView : UserControl
    {
        private bool captureRaccourci;
        private bool majTheme;

        public ParametresView()
        {
            InitializeComponent();
            PreviewKeyDown += CapturerRaccourci;
            Loaded += (s, e) => { MajTheme(); ThemeApplication.Change -= MajTheme; ThemeApplication.Change += MajTheme; };
            Unloaded += (s, e) => ThemeApplication.Change -= MajTheme;
        }

        private CompteurViewModel Compteur { get { var vm = DataContext as MainViewModel; return vm == null ? null : vm.Compteur; } }

        // ---------- Thème ----------

        /// <summary>Coche le choix enregistré (il change aussi par le bouton en haut à droite).</summary>
        private void MajTheme()
        {
            majTheme = true;
            try
            {
                var c = ThemeApplication.Choix;
                ThemeSysteme.IsChecked = c == ChoixTheme.Systeme;
                ThemeClair.IsChecked = c == ChoixTheme.Clair;
                ThemeSombre.IsChecked = c == ChoixTheme.Sombre;
            }
            finally { majTheme = false; }
        }

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (majTheme) return;
            ChoixTheme c;
            var tag = (sender as FrameworkElement)?.Tag as string;
            if (tag != null && Enum.TryParse(tag, out c) && c != ThemeApplication.Choix) ThemeApplication.Definir(c);
        }

        // ---------- Désinstaller ----------

        /// <summary>
        /// Propose « Tout restaurer » avant de supprimer le dossier de données : les journaux d'annulation disparaissent avec lui, et les
        /// réglages appliqués ne pourraient plus être annulés. Puis désinstalle (comme --uninstall --confirm) et ferme l'application.
        /// </summary>
        private async void Desinstaller_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            if (vm == null) return;
            var fenetre = Window.GetWindow(this);
            var choix = MessageBox.Show(fenetre,
                "Annuler d'abord tous les réglages appliqués par GamingTweaker (« Tout restaurer ») ?\r\n\r\n"
                + "Oui : tout restaurer, puis désinstaller.\r\n"
                + "Non : désinstaller en gardant les réglages appliqués (ils ne pourront plus être annulés par GamingTweaker).\r\n"
                + "Annuler : ne rien faire.\r\n\r\n"
                + "La désinstallation retire la routine planifiée, le démarrage avec Windows et le dossier de données protégé "
                + "(sessions, journaux d'annulation, quarantaine). Les nettoyages et les mises à jour installées ne sont pas concernés.",
                "Désinstaller GamingTweaker", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
            if (choix == MessageBoxResult.Cancel || choix == MessageBoxResult.None) return;

            if (choix == MessageBoxResult.Yes)
            {
                if (vm.Restauration == null || vm.RestaurationEnCours) { vm.Message = "Tout restaurer est indisponible pour le moment : rien n'a été désinstallé."; return; }
                ResultatRestauration r;
                try { r = await vm.ToutRestaurerAsync(); }
                catch (Exception ex) { vm.Message = "Tout restaurer a échoué : " + ex.Message + ". Rien n'a été désinstallé."; return; }
                if (r.Echecs.Count > 0 || r.IgnoreesAutreCompte.Count > 0)
                {
                    var suite = MessageBox.Show(fenetre, vm.Message + "\r\n\r\nCertains réglages n'ont pas été restaurés. Désinstaller quand même ?",
                        "Désinstaller GamingTweaker", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                    if (suite != MessageBoxResult.Yes) return;
                }
            }

            Core.BilanDesinstallation b;
            try { b = await System.Threading.Tasks.Task.Run(() => Core.Desinstallation.Executer(true)); }
            catch (Exception ex) { vm.Message = "Désinstallation impossible : " + ex.Message; return; }
            if (b.Code == 2) { vm.Message = string.Join(" ", b.Avertissements); return; }
            Core.TraceAppli.CesserEcriture();
            MessageBox.Show(fenetre, string.Join("\r\n", b.Actions)
                + (b.Avertissements.Count == 0 ? "" : "\r\n\r\n" + string.Join("\r\n", b.Avertissements))
                + "\r\n\r\nGamingTweaker va se fermer. Supprimez ensuite GamingTweaker.exe pour terminer.",
                "Désinstaller GamingTweaker", MessageBoxButton.OK, b.Code == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (vm.Quitter != null) vm.Quitter(); else Application.Current.Shutdown(0);
        }

        // ---------- Raccourci du compteur ----------

        private void ChangerRaccourci_Click(object sender, RoutedEventArgs e)
        {
            captureRaccourci = true;
            TexteRaccourci.Text = "Appuyez sur la combinaison voulue… (Échap pour annuler)";
            ErreurRaccourci.Visibility = Visibility.Collapsed;
            BoutonRaccourci.IsEnabled = false;
            Focus();
            Keyboard.Focus(this);
        }

        private void FinCapture()
        {
            captureRaccourci = false;
            BoutonRaccourci.IsEnabled = true;
            TexteRaccourci.SetBinding(TextBlock.TextProperty, "TexteRaccourci");
        }

        /// <summary>Capture de la nouvelle combinaison : modificateurs (Ctrl, Alt, Maj) + une touche ; Échap annule.</summary>
        private void CapturerRaccourci(object sender, KeyEventArgs e)
        {
            if (!captureRaccourci) return;
            e.Handled = true;
            var touche = e.Key == Key.System ? e.SystemKey : e.Key;
            if (touche == Key.Escape) { FinCapture(); return; }
            if (touche == Key.LeftCtrl || touche == Key.RightCtrl || touche == Key.LeftAlt || touche == Key.RightAlt
                || touche == Key.LeftShift || touche == Key.RightShift || touche == Key.LWin || touche == Key.RWin) return; // on attend la touche principale
            uint mods = 0;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods |= 1;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods |= 2;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods |= 4;
            var c = Compteur;
            var refus = c == null ? null : c.DefinirRaccourci(mods, (uint)KeyInterop.VirtualKeyFromKey(touche));
            FinCapture();
            if (refus != null) { ErreurRaccourci.Text = refus; ErreurRaccourci.Visibility = Visibility.Visible; }
        }

        // ---------- Couleur du compteur ----------

        private void Couleur_Click(object sender, RoutedEventArgs e)
        {
            var couleur = (sender as Button)?.Tag as string;
            var c = Compteur;
            if (couleur != null && c != null) c.Couleur = couleur;
        }

        private void CouleurLibre_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var t = sender as TextBox;
            if (t != null) t.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
        }
    }
}
