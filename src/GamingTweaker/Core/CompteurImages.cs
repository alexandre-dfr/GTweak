using System;
using System.Collections.Generic;
using System.Linq;

namespace GamingTweaker.Core
{
    /// <summary>Mesure des images d'un processus à un instant donné.</summary>
    public class MesureImages
    {
        /// <summary>Images par seconde sur la dernière seconde.</summary>
        public double Fps { get; set; }
        /// <summary>Temps moyen d'une image sur la dernière seconde, en millisecondes.</summary>
        public double TempsImageMs { get; set; }
        /// <summary>« 1 % low » : FPS correspondant au 99e centile du temps d'image sur les 10 dernières secondes (null : pas assez d'images).</summary>
        public double? UnPourcentBas { get; set; }
    }

    /// <summary>
    /// Instants d'affichage (« Present ») d'un processus, en secondes sur l'horloge haute résolution (QueryPerformanceCounter),
    /// et calcul des FPS, du temps d'image et du 1 % low. Pas sûr entre threads : l'appelant verrouille.
    /// </summary>
    public class CompteurImages
    {
        /// <summary>Fenêtre conservée (1 % low).</summary>
        public const double FenetreSecondes = 10.0;
        /// <summary>Sans image depuis ce délai, le processus est considéré comme ne rendant plus rien.</summary>
        public const double SilenceSecondes = 1.0;
        /// <summary>Nombre minimal d'écarts entre images pour un 1 % low significatif.</summary>
        public const int MinimumPourBas = 100;

        private readonly Queue<double> instants = new Queue<double>();
        private double dernier = double.NegativeInfinity;

        public int Nombre { get { return instants.Count; } }
        public double Dernier { get { return dernier; } }

        /// <summary>Ajoute un instant d'affichage. Un instant antérieur au précédent (horloge, ordre des tampons ETW) est ignoré.</summary>
        public void Ajouter(double t)
        {
            if (double.IsNaN(t) || t < dernier) return;
            instants.Enqueue(t);
            dernier = t;
            Purger(t);
        }

        private void Purger(double maintenant)
        {
            while (instants.Count > 0 && instants.Peek() < maintenant - FenetreSecondes) instants.Dequeue();
        }

        /// <summary>Mesure à l'instant <paramref name="maintenant"/> ; null si le processus n'a rien affiché depuis <see cref="SilenceSecondes"/>.</summary>
        public MesureImages Mesurer(double maintenant)
        {
            Purger(maintenant);
            if (instants.Count < 2 || maintenant - dernier > SilenceSecondes) return null;
            var tous = instants.ToArray();
            var recents = tous.Where(t => t >= maintenant - 1.0).ToArray();
            if (recents.Length < 2) return null;
            var duree = recents[recents.Length - 1] - recents[0];
            if (duree <= 0) return null;
            var fps = (recents.Length - 1) / duree;

            double? bas = null;
            if (tous.Length - 1 >= MinimumPourBas)
            {
                var ecarts = new double[tous.Length - 1];
                for (int i = 1; i < tous.Length; i++) ecarts[i - 1] = tous[i] - tous[i - 1];
                Array.Sort(ecarts);
                var p99 = ecarts[Math.Max(0, (int)Math.Ceiling(0.99 * ecarts.Length) - 1)];
                if (p99 > 0) bas = 1.0 / p99;
            }
            return new MesureImages { Fps = fps, TempsImageMs = 1000.0 / fps, UnPourcentBas = bas };
        }
    }
}
