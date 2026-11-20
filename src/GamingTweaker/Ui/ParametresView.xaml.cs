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

        // ---------- Raccourcis du compteur et de l'enregistrement ----------

        private bool captureEnregistrement;

        private void ChangerRaccourci_Click(object sender, RoutedEventArgs e) { DebutCapture(false); }
        private void ChangerRaccourciEnregistrement_Click(object sender, RoutedEventArgs e) { DebutCapture(true); }

        private void DebutCapture(bool enregistrement)
        {
            if (captureRaccourci) FinCapture();
            captureRaccourci = true; captureEnregistrement = enregistrement;
            const string invite = "Appuyez sur la combinaison voulue… (Échap pour annuler)";
            if (enregistrement) { TexteRaccourciEnregistrement.Text = invite; ErreurRaccourciEnregistrement.Visibility = Visibility.Collapsed; BoutonRaccourciEnregistrement.IsEnabled = false; }
            else { TexteRaccourci.Text = invite; ErreurRaccourci.Visibility = Visibility.Collapsed; BoutonRaccourci.IsEnabled = false; }
            Focus();
            Keyboard.Focus(this);
        }

        private void FinCapture()
        {
            captureRaccourci = false;
            BoutonRaccourci.IsEnabled = true; BoutonRaccourciEnregistrement.IsEnabled = true;
            TexteRaccourci.SetBinding(TextBlock.TextProperty, "TexteRaccourci");
            TexteRaccourciEnregistrement.SetBinding(System.Windows.Documents.Run.TextProperty, new System.Windows.Data.Binding("TexteRaccourciEnregistrement") { Mode = System.Windows.Data.BindingMode.OneWay });
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
            var vk = (uint)KeyInterop.VirtualKeyFromKey(touche);
            var enregistrement = captureEnregistrement;
            var refus = c == null ? null : enregistrement ? c.DefinirRaccourciEnregistrement(mods, vk) : c.DefinirRaccourci(mods, vk);
            FinCapture();
            if (refus == null) return;
            var cible = enregistrement ? ErreurRaccourciEnregistrement : ErreurRaccourci;
            cible.Text = refus; cible.Visibility = Visibility.Visible;
        }

        // ---------- Enregistrement des parties ----------

        private void BasculerEnregistrement_Click(object sender, RoutedEventArgs e)
        {
            var c = Compteur;
            if (c != null) c.BasculerEnregistrement();
        }

        private void OuvrirDossierSessions_Click(object sender, RoutedEventArgs e)
        {
            var c = Compteur;
            var dossier = c == null ? Core.SessionsJeu.DossierParDefaut : c.DossierSessions;
            try { System.IO.Directory.CreateDirectory(dossier); Core.SystemExe.OuvrirAvecExplorateur(dossier); }
            catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), "Impossible d'ouvrir le dossier : " + ex.Message, "Parties enregistrées", MessageBoxButton.OK, MessageBoxImage.Warning); }
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
