using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GamingTweaker.Ui
{
    /// <summary>Réglages du compteur en jeu (bouton en haut à droite de la fenêtre principale).</summary>
    public partial class CompteurReglagesWindow : Window
    {
        private readonly CompteurViewModel vm;

        public CompteurReglagesWindow(CompteurViewModel vm, bool raccourciDisponible)
        {
            this.vm = vm;
            InitializeComponent();
            DataContext = vm;
            TexteRaccourci.Text = raccourciDisponible ? CompteurViewModel.Raccourci : CompteurViewModel.Raccourci + " (indisponible : déjà utilisé par une autre application)";
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
