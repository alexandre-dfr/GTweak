using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GamingTweaker.Ui
{
    /// <summary>Réglages du compteur en jeu (bouton en haut à droite de la fenêtre principale).</summary>
    public partial class CompteurReglagesWindow : Window
    {
        private readonly CompteurViewModel vm;

        private bool captureRaccourci;

        public CompteurReglagesWindow(CompteurViewModel vm)
        {
            this.vm = vm;
            InitializeComponent();
            DataContext = vm;
            PreviewKeyDown += CapturerRaccourci;
            // Tant que les réglages sont ouverts, le compteur est visible et se déplace à la souris.
            Loaded += (s, e) => vm.ModePlacement = true;
            Closed += (s, e) => { vm.ModePlacement = false; vm.Enregistrer(); };
        }

        private void ChangerRaccourci_Click(object sender, RoutedEventArgs e)
        {
            captureRaccourci = true;
            TexteRaccourci.Text = "Appuyez sur la combinaison voulue… (Échap pour annuler)";
            ErreurRaccourci.Visibility = Visibility.Collapsed;
            BoutonRaccourci.IsEnabled = false;
            Focus();
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
            var refus = vm.DefinirRaccourci(mods, (uint)KeyInterop.VirtualKeyFromKey(touche));
            FinCapture();
            if (refus != null) { ErreurRaccourci.Text = refus; ErreurRaccourci.Visibility = Visibility.Visible; }
        }

        private void Couleur_Click(object sender, RoutedEventArgs e)
        {
            var c = (sender as Button)?.Tag as string;
            if (c != null) vm.Couleur = c;
        }

        private void CouleurLibre_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var t = sender as TextBox;
            if (t != null) t.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
        }

        private void Fermer_Click(object sender, RoutedEventArgs e) { vm.Enregistrer(); Close(); }
    }
}
