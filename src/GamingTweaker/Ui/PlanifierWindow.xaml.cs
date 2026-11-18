using System.Windows;

namespace GamingTweaker.Ui
{
    /// <summary>Réglage de la routine hebdomadaire (profil sélectionné en bas de la fenêtre principale).</summary>
    public partial class PlanifierWindow : Window
    {
        public PlanifierWindow(RoutineViewModel routine, string profil)
        {
            InitializeComponent();
            DataContext = routine;
            Explication.Text = string.IsNullOrEmpty(profil)
                ? "Choisissez d'abord un profil en bas de la fenêtre : la routine l'applique chaque semaine, sans interface, avec les droits administrateur."
                : "Applique chaque semaine le profil « " + profil + " », sans interface, avec les droits administrateur. Rien n'est appliqué tant que vous ne cliquez pas sur « Planifier ».";
        }
    }
}
