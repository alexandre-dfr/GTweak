using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Anti-triche à pilote noyau en cours d'exécution (Vanguard, FACEIT, Easy Anti-Cheat, BattlEye, EA Javelin).
    /// Pendant qu'il tourne, le compteur en jeu ne lit pas la température du processeur : cette lecture passe par un pilote
    /// (PawnIO) que certains anti-triche surveillent. Les FPS (événements ETW de Windows), les charges et la température de la
    /// carte graphique (interface du pilote NVIDIA ou AMD) ne passent par aucun pilote tiers et restent affichés.
    /// </summary>
    public static class AntiTriche
    {
        /// <summary>Services Windows des anti-triche (nom du service, nom affiché).</summary>
        public static readonly IReadOnlyList<Tuple<string, string>> Services = new[]
        {
            Tuple.Create("vgc", "Riot Vanguard"),
            Tuple.Create("FACEIT", "FACEIT"),
            Tuple.Create("EasyAntiCheat", "Easy Anti-Cheat"),
            Tuple.Create("EasyAntiCheat_EOS", "Easy Anti-Cheat"),
            Tuple.Create("BEService", "BattlEye"),
            Tuple.Create("EAAntiCheatService", "EA Javelin"),
        };

        /// <summary>
        /// Nom affiché du premier anti-triche dont le service tourne ; null si aucun. <paramref name="etat"/> donne l'état
        /// d'un service (null s'il n'existe pas) ; par défaut, le gestionnaire de services de Windows.
        /// </summary>
        public static string Actif(Func<string, ServiceControllerStatus?> etat = null)
        {
            etat = etat ?? EtatService;
            foreach (var s in Services)
            {
                ServiceControllerStatus? e;
                try { e = etat(s.Item1); } catch (Exception) { e = null; }
                if (e == ServiceControllerStatus.Running || e == ServiceControllerStatus.StartPending) return s.Item2;
            }
            return null;
        }

        private static ServiceControllerStatus? EtatService(string nom)
        {
            try { using (var sc = new ServiceController(nom)) return sc.Status; }
            catch (InvalidOperationException) { return null; } // service absent
        }
    }
}
