using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace GamingTweaker.Core
{
    public enum ServiceState { Absent, Stopped, Running }

    /// <summary>
    /// Actions système du Mode Jeu. StopService renvoie l'état AVANT l'arrêt (Running : arrêté par l'appel ; Stopped : ne tournait pas ; Absent : inexistant)
    /// et lève une exception si l'arrêt échoue (ServiceRefuseException : refusé avant toute action, par exemple dépendants en cours d'exécution).
    /// KillProcess prend le nom du processus et le chemin enregistré de son exécutable ; RestartProcess ce chemin.
    /// SetPlan renvoie le GUID du plan actif avant le changement ; GetPlan le GUID du plan actif.
    /// </summary>
    public interface IModeJeuActions
    {
        ServiceState StopService(string nom);
        void StartService(string nom);
        bool KillProcess(string nom, string chemin);
        void RestartProcess(string chemin);
        string SetPlan(string guid);
        string GetPlan();
    }

    /// <summary>
    /// Libération de mémoire (facultative pour une implémentation d'IModeJeuActions) : vide le jeu de travail des processus de la session
    /// en arrière-plan, hors chemins exclus (jeux) et fenêtre au premier plan. Renvoie les Mo de mémoire disponible gagnés.
    /// </summary>
    public interface IViderMemoire
    {
        long ViderMemoire(IEnumerable<string> exclusions);
    }

    /// <summary>Arrêt refusé avant toute action : rien n'a été arrêté.</summary>
    public class ServiceRefuseException : InvalidOperationException
    {
        public ServiceRefuseException(string message) : base(message) { }
    }

    public class ProcessusChoix
    {
        public string Nom { get; set; }
        public string Chemin { get; set; }
        public string Detail { get; set; }
    }

    public class ServiceChoix
    {
        public string Nom { get; set; }
        public string Affichage { get; set; }
    }

    public class ModeJeuConfig
    {
        public List<string> Services { get; } = new List<string>();
        public List<ProcessusChoix> Processus { get; } = new List<ProcessusChoix>();
        /// <summary>Plan d'alimentation à activer ; vide : le plan n'est pas changé.</summary>
        public string PlanGuid { get; set; }
        /// <summary>Profil du pilote NVIDIA appliqué le temps du Mode Jeu (Aucun : pilote inchangé).</summary>
        public ProfilNvidia Nvidia { get; set; }
        /// <summary>Vider le jeu de travail des processus en arrière-plan à l'activation (rien à rétablir : les pages reviennent à la demande).</summary>
        public bool LibererMemoire { get; set; }
        /// <summary>Exécutables ou dossiers jamais touchés par la libération de mémoire (les jeux surveillés).</summary>
        public List<string> Exclusions { get; } = new List<string>();
    }

    public class ModeJeuBilan
    {
        public bool Verrouille { get; set; }
        public List<string> Actions { get; } = new List<string>();
        public List<string> Avertissements { get; } = new List<string>();
    }
}
