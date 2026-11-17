using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GamingTweaker.Core;

namespace GamingTweaker.Ui
{
    /// <summary>Choix des jeux trouvés dans une bibliothèque (Steam, Epic) : aucun n'est coché d'avance.</summary>
    public partial class ChoixJeuxWindow : Window
    {
        private readonly List<ChoixModeJeu> choix;
        private readonly IReadOnlyList<JeuSurveille> jeux;

        public ChoixJeuxWindow(string titre, IReadOnlyList<JeuSurveille> jeux)
        {
            InitializeComponent();
            Title = titre;
            this.jeux = jeux;
            choix = jeux.Select(j => new ChoixModeJeu { Nom = j.Nom, Affichage = j.Nom, Detail = j.Chemin, Chemin = j.Chemin }).ToList();
            Liste.ItemsSource = choix;
        }

        public IReadOnlyList<JeuSurveille> Choisis
        {
            get { return jeux.Where((j, i) => choix[i].IsChecked).ToList(); }
        }

        private void ToutCocher_Click(object sender, RoutedEventArgs e) { foreach (var c in choix) c.IsChecked = true; }

        private void Ok_Click(object sender, RoutedEventArgs e) { DialogResult = true; }
    }
}
