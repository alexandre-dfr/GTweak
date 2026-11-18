using System.Windows.Controls;

namespace GamingTweaker.Ui
{
    /// <summary>Écran d'accueil : synthèse de ce PC, profils intégrés en un clic, état par menu, Mode Jeu, raccourcis et conseils.</summary>
    public partial class AccueilView : UserControl
    {
        public const string Conseil1 = "Seules les options cochées s'exécutent. « Aperçu » montre tout ce qui va être fait avant de lancer.";
        public const string Conseil2 = "Badges : Sûr (sans risque), Avancé (à connaître), Risqué (confirmation demandée). « Recommandé » ne coche que du Sûr.";
        public const string Conseil3 = "Tout s'annule : session par session dans l'Historique, ou d'un coup avec « Tout restaurer ».";
        public const string Conseil4 = "Ctrl+F cherche une option dans tous les menus. Un clic sur un groupe le déplie.";

        public AccueilView() { InitializeComponent(); }
    }
}
