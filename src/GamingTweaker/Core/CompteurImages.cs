using System;
using System.Collections.Generic;

namespace GamingTweaker.Core
{
    /// <summary>Mesure des images d'un processus à un instant donné.</summary>
    public class MesureImages
    {
        /// <summary>Images par seconde sur la dernière seconde reçue.</summary>
        public double Fps { get; set; }
        /// <summary>Temps moyen d'une image sur la dernière seconde reçue, en millisecondes.</summary>
        public double TempsImageMs { get; set; }
        /// <summary>« 1 % low » : FPS correspondant au 99e centile du temps d'image sur les 10 dernières secondes (null : pas assez d'images).</summary>
        public double? UnPourcentBas { get; set; }
    }

    /// <summary>
    /// Instants d'affichage (« Present ») d'un processus, en secondes sur l'horloge haute résolution (QueryPerformanceCounter),
    /// et calcul des FPS, du temps d'image et du 1 % low. Pas sûr entre threads : l'appelant verrouille.
    ///
    /// Windows livre les événements par paquets (au plus une seconde de retard) : la mesure porte sur la dernière seconde
    /// <em>reçue</em> (avant la dernière image), pas sur la seconde qui précède l'horloge, sinon les FPS chutent entre deux paquets.
    /// Un instant arrivé en retard est inséré à sa place au lieu d'être ignoré.
    /// </summary>
    public class CompteurImages
    {
        /// <summary>Fenêtre conservée (1 % low).</summary>
        public const double FenetreSecondes = 10.0;
        /// <summary>Sans image reçue depuis ce délai (paquets compris), le processus est considéré comme ne rendant plus rien.</summary>
        public const double SilenceSecondes = 2.5;
        /// <summary>Nombre minimal d'écarts entre images pour un 1 % low significatif.</summary>
        public const int MinimumPourBas = 100;

        private readonly List<double> instants = new List<double>();
        private double recu = double.NegativeInfinity;

        public int Nombre { get { return instants.Count; } }
        /// <summary>Instant de l'image la plus récente.</summary>
        public double Dernier { get { return instants.Count == 0 ? double.NegativeInfinity : instants[instants.Count - 1]; } }

        /// <summary>
        /// Ajoute un instant d'affichage, à sa place chronologique. <paramref name="reception"/> : heure de réception (par défaut l'instant lui-même),
        /// qui sert à détecter le silence.
        /// </summary>
        public void Ajouter(double t, double? reception = null)
        {
            if (double.IsNaN(t) || double.IsInfinity(t)) return;
            int i = instants.Count;
            if (i == 0 || t >= instants[i - 1]) instants.Add(t);
            else
            {
                i = instants.BinarySearch(t);
                if (i < 0) i = ~i;
                if (Dernier - t > FenetreSecondes) return; // bien trop ancien
                instants.Insert(i, t);
            }
            recu = Math.Max(recu, reception ?? t);
            Purger();
        }

        private void Purger()
        {
            var limite = Dernier - FenetreSecondes;
            int n = 0;
            while (n < instants.Count && instants[n] < limite) n++;
            if (n > 0) instants.RemoveRange(0, n);
        }

        /// <summary>Mesure à l'instant <paramref name="maintenant"/> ; null si rien n'a été reçu depuis <see cref="SilenceSecondes"/>.</summary>
        public MesureImages Mesurer(double maintenant)
        {
            if (instants.Count < 2 || maintenant - recu > SilenceSecondes) return null;
            var fin = Dernier;
            int debut = instants.Count - 1;
            while (debut > 0 && instants[debut - 1] >= fin - 1.0) debut--;
            var nombre = instants.Count - debut;
            var duree = fin - instants[debut];
            if (nombre < 2 || duree <= 0) return null;
            var fps = (nombre - 1) / duree;

            double? bas = null;
            if (instants.Count - 1 >= MinimumPourBas)
            {
                var ecarts = new double[instants.Count - 1];
                for (int i = 1; i < instants.Count; i++) ecarts[i - 1] = instants[i] - instants[i - 1];
                Array.Sort(ecarts);
                var p99 = ecarts[Math.Max(0, (int)Math.Ceiling(0.99 * ecarts.Length) - 1)];
                if (p99 > 0) bas = 1.0 / p99;
            }
            return new MesureImages { Fps = fps, TempsImageMs = 1000.0 / fps, UnPourcentBas = bas };
        }
    }
}
