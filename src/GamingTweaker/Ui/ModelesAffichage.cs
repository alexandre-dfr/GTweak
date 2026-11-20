using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using GamingTweaker.Core;
using GamingTweaker.Lists;

namespace GamingTweaker.Ui
{
    public class MenuEntry : ObservableObject
    {
        private int coches;
        public string Nom { get; set; }
        /// <summary>Options cochées dans ce menu (affiché à côté du nom, 0 : rien).</summary>
        public int Coches { get { return coches; } set { if (Set(ref coches, value)) Raise("Libelle"); } }
        public string Libelle { get { return coches > 0 ? Nom + "  ·  " + coches : Nom; } }
        /// <summary>Icône Windows (Segoe Fluent Icons) de la barre latérale.</summary>
        public string Icone
        {
            get
            {
                switch (Nom)
                {
                    case "Accueil": return "\uE80F";
                    case "Diagnostic": return "\uE9D9";
                    case "Mises à jour": return "\uE895";
                    case "Télémétrie": return "\uE72E";
                    case "Nettoyage": return "\uE74D";
                    case "Optimisation": return "\uE945";
                    case "Bloatware": return "\uE71D";
                    case "Mode Jeu": return "\uE7FC";
                    case "Historique": return "\uE81C";
                    case "Informations": return "\uE946";
                    case "Paramètres": return "\uE713";
                    default: return "\uE8B7";
                }
            }
        }
        public bool EstSeparateur { get; set; }
        /// <summary>Vue réservée aux tâches suivantes : panneau neutre avec le seul titre du menu.</summary>
        public bool EstEmplacement { get; set; }
    }

    /// <summary>Vrai → masqué, faux → visible.</summary>
    public class BoolVersVisibiliteInverse : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool && (bool)value ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }

    /// <summary>Vrai si un nombre est supérieur à zéro (déclencheurs XAML).</summary>
    public class EstNonNul : System.Windows.Data.IValueConverter
    {
        public static readonly EstNonNul Instance = new EstNonNul();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return value is int && (int)value > 0; }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }

    /// <summary>Réglage hors catalogue trouvé par la recherche (Paramètres, Mode Jeu, Historique…) : un clic ouvre son menu.</summary>
    public class ResultatPage
    {
        public string Titre { get; set; }
        public string Description { get; set; }
        public string Menu { get; set; }
        /// <summary>Mots recherchés (titre, description et synonymes), normalisés.</summary>
        public string Texte { get; set; }
    }

    /// <summary>Ligne « état de ce PC » de l'écran d'accueil : options déjà appliquées sur le total du menu.</summary>
    public class LigneAccueil
    {
        public string Menu { get; set; }
        public int Appliquees { get; set; }
        public int Total { get; set; }
        public string Texte { get { return Appliquees + " / " + Total + " déjà appliquée" + (Appliquees > 1 ? "s" : ""); } }
        public double Proportion { get { return Total == 0 ? 0 : (double)Appliquees / Total; } }
    }

    /// <summary>Tuile de profil intégré de l'écran d'accueil.</summary>
    public class TuileProfil
    {
        public string Nom { get; set; }
        public string Accroche { get; set; }
        public string Description { get; set; }
        public int Nombre { get; set; }
        public string NombreTexte { get { return Nombre + " option" + (Nombre > 1 ? "s" : ""); } }
        /// <summary>0 : Gaming léger, 1 : Gaming max, 2 : Confidentialité (couleur de la tuile).</summary>
        public int Rang { get; set; }
        /// <summary>Icône Windows (Segoe Fluent Icons) : bouclier, éclair, cadenas.</summary>
        public string Icone { get { return Rang == 0 ? "\uEA18" : Rang == 1 ? "\uE945" : "\uE72E"; } }
    }

    public class PreviewRow : ObservableObject
    {
        private string taille = "—";
        public string Id { get; set; }
        /// <summary>Menu de l'option (regroupement de l'aperçu).</summary>
        public string Menu { get; set; }
        public string Titre { get; set; }
        public string Risque { get; set; }
        public string Reversibilite { get; set; }
        public string Redemarrage { get; set; }
        /// <summary>Estimation de taille ; « — » tant qu'elle n'est pas connue ou si l'option n'en a pas.</summary>
        public string Taille { get { return taille; } set { Set(ref taille, value); } }
    }

    /// <summary>Bilan de « Tout restaurer ».</summary>
    public class ResultatRestauration
    {
        public int AnnuleesParJournal { get; set; }
        public int AnnuleesParRevert { get; set; }
        /// <summary>Une ligne « id : message » par option dont l'annulation a échoué.</summary>
        public List<string> Echecs { get; } = new List<string>();
        /// <summary>Options appliquées mais impossibles à restaurer (ni journal, ni script revert).</summary>
        public List<string> Ignorees { get; } = new List<string>();
        /// <summary>Annulations réussies avec une réserve (par exemple une réinstallation manuelle).</summary>
        public List<string> Remarques { get; } = new List<string>();
        /// <summary>Options HKCU ignorées parce que l'application tourne sous un autre compte que le bureau.</summary>
        public List<string> IgnoreesAutreCompte { get; } = new List<string>();
    }
}
