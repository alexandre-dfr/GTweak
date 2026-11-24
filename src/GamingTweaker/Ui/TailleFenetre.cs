using System;

namespace GamingTweaker.Ui
{
    /// <summary>Tailles d'une fenêtre ramenées à la zone de travail de l'écran (décision pure, testée).</summary>
    public static class TailleFenetre
    {
        public struct Resultat
        {
            public double Largeur, Hauteur, MinLargeur, MinHauteur;
        }

        /// <summary>
        /// Rien ne dépasse la zone de travail : le minimum est abaissé si besoin (les vues défilent), la taille de départ aussi.
        /// Zone illisible (0 ou négative) : tailles inchangées.
        /// </summary>
        public static Resultat Adapter(double largeur, double hauteur, double minLargeur, double minHauteur, double zoneLargeur, double zoneHauteur)
        {
            var r = new Resultat { Largeur = largeur, Hauteur = hauteur, MinLargeur = minLargeur, MinHauteur = minHauteur };
            if (zoneLargeur > 0)
            {
                r.MinLargeur = Math.Min(minLargeur, zoneLargeur);
                r.Largeur = Math.Max(r.MinLargeur, Math.Min(largeur, zoneLargeur));
            }
            if (zoneHauteur > 0)
            {
                r.MinHauteur = Math.Min(minHauteur, zoneHauteur);
                r.Hauteur = Math.Max(r.MinHauteur, Math.Min(hauteur, zoneHauteur));
            }
            return r;
        }
    }
}
