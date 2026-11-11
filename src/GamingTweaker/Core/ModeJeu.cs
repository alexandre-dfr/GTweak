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
    }

    public class ModeJeuBilan
    {
        public bool Verrouille { get; set; }
        public List<string> Actions { get; } = new List<string>();
        public List<string> Avertissements { get; } = new List<string>();
    }

    public class ModeJeu
    {
        public const string PlanPerformances = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        /// <summary>Jamais proposés ni tués : processus système critiques.</summary>
        public static readonly string[] ProcessusCritiques = { "csrss", "wininit", "winlogon", "lsass", "services", "smss", "dwm" };
        /// <summary>Jamais proposés ni tués : sécurité, audio, pilotes d'affichage, shell.</summary>
        public static readonly string[] ProcessusProteges =
        {
            "MsMpEng", "NisSrv", "MpDefenderCoreService", "SecurityHealthService", "SecurityHealthSystray", "audiodg",
            "nvcontainer", "NVDisplay.Container", "RtkAudUService", "RtkAudUService64", "atiesrxx", "atieclxx", "explorer", "System", "Registry", "svchost", "fontdrvhost", "GamingTweaker",
            "sihost", "ctfmon", "ShellExperienceHost", "StartMenuExperienceHost", "TextInputHost", "SearchHost", "RuntimeBroker", "conhost", "igfxEM", "RadeonSoftware",
        };
        /// <summary>Jamais proposés ni arrêtés : services dont dépend le système, la sécurité, l'audio, l'affichage et le réseau.</summary>
        public static readonly string[] ServicesProteges =
        {
            "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "SamSs", "EventLog", "PlugPlay", "Power", "ProfSvc", "UserManager", "Winmgmt", "CryptSvc", "BFE", "MpsSvc",
            "Dhcp", "Dnscache", "nsi", "Themes", "WinDefend", "WdNisSvc", "Sense", "SecurityHealthService", "wscsvc", "AudioSrv", "AudioEndpointBuilder",
            "NVDisplay.ContainerLocalSystem", "NvContainerLocalSystem", "RtkAudioUniversalService", "TrustedInstaller", "gpsvc",
        };

        public static bool EstProcessusProtege(string nom)
        {
            nom = Normaliser(nom);
            if (nom.Length == 0) return true;
            try { if (string.Equals(nom, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase)) return true; } catch (Exception) { }
            return ProcessusCritiques.Concat(ProcessusProteges).Any(p => string.Equals(p, nom, StringComparison.OrdinalIgnoreCase));
        }

        public static bool EstServiceProtege(string nom)
        {
            return string.IsNullOrWhiteSpace(nom) || ServicesProteges.Any(p => string.Equals(p, nom.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static string Normaliser(string nom)
        {
            nom = (nom ?? "").Trim();
            return nom.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? nom.Substring(0, nom.Length - 4) : nom;
        }

        private class Entree
        {
            [JsonProperty("nom")] public string Nom { get; set; }
            [JsonProperty("chemin", NullValueHandling = NullValueHandling.Ignore)] public string Chemin { get; set; }
            /// <summary>en-cours | arrete | deja-arrete | absent | refuse | echec | tue | non-lance | retabli</summary>
            [JsonProperty("etat")] public string Etat { get; set; }
        }

        private class Etat
        {
            [JsonProperty("debut")] public string Debut { get; set; }
            [JsonProperty("plan")] public string Plan { get; set; }
            [JsonProperty("planPrecedent", NullValueHandling = NullValueHandling.Ignore)] public string PlanPrecedent { get; set; }
            [JsonProperty("planEtat")] public string PlanEtat { get; set; }
            [JsonProperty("services")] public List<Entree> Services { get; set; } = new List<Entree>();
            [JsonProperty("processus")] public List<Entree> Processus { get; set; } = new List<Entree>();
        }

        private readonly string dossier;
        private readonly IModeJeuActions actions;
        private readonly string lockName;
        private readonly Action apresEtatEcrit;

        /// <param name="machineDir">Dossier de la machine : modejeu.json y est écrit.</param>
        /// <param name="apresEtatEcrit">Appelé après chaque écriture de l'état (tests : vérifier qu'il précède toute action).</param>
        public ModeJeu(string machineDir, IModeJeuActions actions, string lockName = null, Action apresEtatEcrit = null)
        {
            dossier = machineDir; this.actions = actions; this.lockName = lockName; this.apresEtatEcrit = apresEtatEcrit;
        }

        private string Fichier { get { return Path.Combine(dossier, "modejeu.json"); } }

        public bool IsActive { get { return File.Exists(Fichier); } }

        private void Ecrire(Etat e)
        {
            Directory.CreateDirectory(dossier);
            var tmp = Fichier + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(e, Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(Fichier)) File.Replace(tmp, Fichier, null); else File.Move(tmp, Fichier);
            if (apresEtatEcrit != null) apresEtatEcrit();
        }

        private Etat Lire()
        {
            try { return JsonConvert.DeserializeObject<Etat>(File.ReadAllText(Fichier, Encoding.UTF8)); }
            catch (Exception) { return null; }
        }

        public ModeJeuBilan Activate(ModeJeuConfig c)
        {
            var bilan = new ModeJeuBilan();
            using (var v = SingleInstance.TryAcquire(lockName))
            {
                if (v == null)
                {
                    bilan.Verrouille = true;
                    bilan.Avertissements.Add("Une autre exécution de GamingTweaker est en cours : le Mode Jeu n'a pas été activé.");
                    return bilan;
                }
                if (IsActive)
                {
                    bilan.Avertissements.Add("Le Mode Jeu est déjà actif : rétablissez-le d'abord.");
                    return bilan;
                }
                var etat = new Etat { Debut = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"), PlanEtat = "aucun" };
                foreach (var s in c.Services.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (EstServiceProtege(s)) { bilan.Avertissements.Add("Service protégé, ignoré : " + s); continue; }
                    etat.Services.Add(new Entree { Nom = s, Etat = "non-lance" });
                }
                foreach (var p in c.Processus.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Nom)).GroupBy(x => Normaliser(x.Nom), StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
                {
                    if (EstProcessusProtege(p.Nom)) { bilan.Avertissements.Add("Processus protégé, ignoré : " + p.Nom); continue; }
                    etat.Processus.Add(new Entree { Nom = Normaliser(p.Nom), Chemin = p.Chemin, Etat = "non-lance" });
                }
                var planVoulu = (c.PlanGuid ?? "").Trim();
                if (planVoulu.Length > 0) { etat.Plan = planVoulu; etat.PlanEtat = "non-lance"; }

                // L'état précède toute action : un arrêt brutal laisse de quoi rétablir.
                Ecrire(etat);

                foreach (var s in etat.Services)
                {
                    s.Etat = "en-cours"; Ecrire(etat);
                    try
                    {
                        var avant = actions.StopService(s.Nom);
                        s.Etat = avant == ServiceState.Running ? "arrete" : avant == ServiceState.Absent ? "absent" : "deja-arrete";
                        if (avant == ServiceState.Running) bilan.Actions.Add("Service arrêté : " + s.Nom);
                    }
                    catch (ServiceRefuseException ex)
                    {
                        s.Etat = "refuse";
                        bilan.Avertissements.Add("Service " + s.Nom + " non arrêté : " + ex.Message);
                    }
                    catch (Exception ex)
                    {
                        s.Etat = "echec";
                        bilan.Avertissements.Add("Service " + s.Nom + " non arrêté : " + ex.Message);
                    }
                    Ecrire(etat);
                }
                foreach (var p in etat.Processus)
                {
                    p.Etat = "en-cours"; Ecrire(etat);
                    try
                    {
                        if (actions.KillProcess(p.Nom, p.Chemin)) { p.Etat = "tue"; bilan.Actions.Add("Processus fermé : " + p.Nom); }
                        else p.Etat = "non-lance";
                    }
                    catch (Exception ex)
                    {
                        p.Etat = "echec";
                        bilan.Avertissements.Add("Processus " + p.Nom + " non fermé : " + ex.Message);
                    }
                    Ecrire(etat);
                }
                if (etat.PlanEtat == "non-lance")
                {
                    etat.PlanEtat = "en-cours"; Ecrire(etat);
                    try
                    {
                        etat.PlanPrecedent = actions.SetPlan(etat.Plan);
                        etat.PlanEtat = "change";
                        bilan.Actions.Add("Plan d'alimentation changé.");
                    }
                    catch (Exception ex)
                    {
                        etat.PlanEtat = "echec";
                        bilan.Avertissements.Add("Plan d'alimentation non changé : " + ex.Message);
                    }
                    Ecrire(etat);
                }
            }
            return bilan;
        }

        /// <summary>Idempotent : sans état, ne fait rien. Les éléments rétablis sont marqués ; en cas d'échec l'état est conservé pour réessayer.</summary>
        public ModeJeuBilan Restore()
        {
            var bilan = new ModeJeuBilan();
            using (var v = SingleInstance.TryAcquire(lockName))
            {
                if (v == null)
                {
                    bilan.Verrouille = true;
                    bilan.Avertissements.Add("Une autre exécution de GamingTweaker est en cours : le Mode Jeu n'a pas été rétabli.");
                    return bilan;
                }
                if (!IsActive) return bilan;
                var etat = Lire();
                if (etat == null)
                {
                    bilan.Avertissements.Add("Le fichier d'état du Mode Jeu est illisible (" + Fichier + ") : rien n'a été rétabli.");
                    return bilan;
                }
                bool echec = false;
                foreach (var s in etat.Services)
                {
                    if (s.Etat == "en-cours" || s.Etat == "echec")
                    {
                        // État incertain : on ne démarre pas un service qui ne tournait peut-être pas.
                        bilan.Avertissements.Add("État incertain du service " + s.Nom + " (arrêt interrompu ou en échec) : vérifiez-le dans les services Windows.");
                        s.Etat = "retabli"; continue;
                    }
                    if (s.Etat != "arrete") continue;
                    try { actions.StartService(s.Nom); s.Etat = "retabli"; bilan.Actions.Add("Service redémarré : " + s.Nom); }
                    catch (Exception ex) { echec = true; bilan.Avertissements.Add("Service " + s.Nom + " non redémarré : " + ex.Message); }
                }
                foreach (var p in etat.Processus)
                {
                    if (p.Etat == "en-cours" || p.Etat == "echec") { p.Etat = "retabli"; continue; }
                    if (p.Etat != "tue") continue;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(p.Chemin)) actions.RestartProcess(p.Chemin);
                        else bilan.Avertissements.Add("Processus " + p.Nom + " : chemin inconnu, à relancer à la main.");
                        p.Etat = "retabli"; bilan.Actions.Add("Processus relancé : " + p.Nom);
                    }
                    catch (Exception ex) { echec = true; bilan.Avertissements.Add("Processus " + p.Nom + " non relancé : " + ex.Message); }
                }
                if (etat.PlanEtat == "change" && !string.IsNullOrWhiteSpace(etat.PlanPrecedent))
                {
                    try
                    {
                        // Seulement si le plan actif est encore celui du Mode Jeu : un plan choisi depuis par l'utilisateur est conservé.
                        var actuel = actions.GetPlan();
                        if (string.Equals((actuel ?? "").Trim(), (etat.Plan ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            actions.SetPlan(etat.PlanPrecedent);
                            bilan.Actions.Add("Plan d'alimentation rétabli.");
                        }
                        else bilan.Avertissements.Add("Le plan d'alimentation a été changé depuis l'activation du Mode Jeu : il est conservé.");
                        etat.PlanEtat = "retabli";
                    }
                    catch (Exception ex) { echec = true; bilan.Avertissements.Add("Plan d'alimentation non rétabli : " + ex.Message); }
                }
                else if (etat.PlanEtat == "en-cours")
                    bilan.Avertissements.Add("Le changement de plan d'alimentation a été interrompu : vérifiez le plan actif dans les options d'alimentation.");

                if (echec) { try { Ecrire(etat); } catch (Exception) { } }
                else { try { File.Delete(Fichier); } catch (Exception ex) { bilan.Avertissements.Add("Fichier d'état non supprimé : " + ex.Message); } }
            }
            return bilan;
        }

        /// <summary>
        /// Abandonne l'état enregistré (après un « Rétablir » en échec ou un état illisible) : ce qui reste arrêté ne sera plus rétabli par l'outil.
        /// Sous le verrou d'instance.
        /// </summary>
        public ModeJeuBilan Oublier()
        {
            var bilan = new ModeJeuBilan();
            using (var v = SingleInstance.TryAcquire(lockName))
            {
                if (v == null)
                {
                    bilan.Verrouille = true;
                    bilan.Avertissements.Add("Une autre exécution de GamingTweaker est en cours : l'état du Mode Jeu n'a pas été oublié.");
                    return bilan;
                }
                if (File.Exists(Fichier)) File.Delete(Fichier);
                bilan.Actions.Add("État du Mode Jeu oublié.");
            }
            return bilan;
        }
    }

    /// <summary>Listes proposées dans l'interface : services en cours d'exécution et processus, sans les éléments protégés.</summary>
    public static class ModeJeuListes
    {
        public static List<ServiceChoix> ListerServices()
        {
            var res = new List<ServiceChoix>();
            try
            {
                foreach (var s in ServiceController.GetServices())
                {
                    using (s)
                        if (s.Status == ServiceControllerStatus.Running && s.CanStop && !ModeJeu.EstServiceProtege(s.ServiceName))
                            res.Add(new ServiceChoix { Nom = s.ServiceName, Affichage = s.DisplayName });
                }
            }
            catch (Exception) { }
            return res.OrderBy(s => s.Affichage, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static List<ProcessusChoix> ListerProcessus()
        {
            var res = new Dictionary<string, ProcessusChoix>(StringComparer.OrdinalIgnoreCase);
            int session = 0;
            try { session = Process.GetCurrentProcess().SessionId; } catch (Exception) { }
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        if (p.SessionId != session || ModeJeu.EstProcessusProtege(p.ProcessName)) continue;
                        string chemin = null;
                        try { chemin = p.MainModule.FileName; } catch (Exception) { }
                        if (string.IsNullOrEmpty(chemin)) continue; // sans chemin il ne pourrait pas être relancé
                        if (res.ContainsKey(p.ProcessName)) continue;
                        res[p.ProcessName] = new ProcessusChoix { Nom = p.ProcessName, Chemin = chemin, Detail = chemin };
                    }
                    catch (Exception) { }
                }
            }
            return res.Values.OrderBy(x => x.Nom, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    /// <summary>Implémentation réelle : ServiceController avec délai, Process.Kill, powercfg.</summary>
    public class ModeJeuSysteme : IModeJeuActions
    {
        private static readonly TimeSpan Delai = TimeSpan.FromSeconds(20);

        public ServiceState StopService(string nom)
        {
            if (ModeJeu.EstServiceProtege(nom)) throw new InvalidOperationException("service protégé");
            ServiceController sc;
            try { sc = new ServiceController(nom); var _ = sc.Status; }
            catch (InvalidOperationException) { return ServiceState.Absent; }
            using (sc)
            {
                if (sc.Status == ServiceControllerStatus.Stopped) return ServiceState.Stopped;
                if (sc.Status != ServiceControllerStatus.Running) throw new InvalidOperationException("le service n'est pas dans un état stable (" + sc.Status + ")");
                // ServiceController.Stop arrête aussi les dépendants, qui ne seraient ni enregistrés ni redémarrés : refusé.
                var deps = new List<Tuple<string, ServiceControllerStatus>>();
                foreach (var d in sc.DependentServices) using (d) deps.Add(Tuple.Create(d.ServiceName, d.Status));
                var actifs = DependantsActifs(deps);
                if (actifs.Count > 0) throw new ServiceRefuseException("des services qui en dépendent sont en cours d'exécution (" + string.Join(", ", actifs) + ")");
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, Delai); // TimeoutException si trop long : signalé, non redémarré automatiquement
                return ServiceState.Running;
            }
        }

        public void StartService(string nom)
        {
            ServiceController sc;
            try { sc = new ServiceController(nom); var _ = sc.Status; }
            catch (InvalidOperationException) { return; } // service disparu
            using (sc)
            {
                if (sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending) return;
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, Delai);
            }
        }

        /// <summary>Dépendants qui ne sont pas arrêtés (en cours, en démarrage, en pause…).</summary>
        public static List<string> DependantsActifs(IEnumerable<Tuple<string, ServiceControllerStatus>> dependants)
        {
            return dependants.Where(d => d.Item2 != ServiceControllerStatus.Stopped).Select(d => d.Item1).ToList();
        }

        /// <summary>Seul un processus de la session courante dont l'exécutable est exactement celui enregistré est fermé.</summary>
        public static bool DoitTuer(int sessionProcessus, string cheminProcessus, int sessionCourante, string cheminEnregistre)
        {
            if (sessionProcessus != sessionCourante || string.IsNullOrWhiteSpace(cheminProcessus) || string.IsNullOrWhiteSpace(cheminEnregistre)) return false;
            try { return string.Equals(Path.GetFullPath(cheminProcessus), Path.GetFullPath(cheminEnregistre), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        public bool KillProcess(string nom, string chemin)
        {
            if (ModeJeu.EstProcessusProtege(nom)) throw new InvalidOperationException("processus protégé");
            if (string.IsNullOrWhiteSpace(chemin)) throw new InvalidOperationException("chemin de l'exécutable inconnu");
            int session, moi;
            using (var courant = Process.GetCurrentProcess()) { session = courant.SessionId; moi = courant.Id; }
            bool any = false;
            foreach (var p in Process.GetProcessesByName(nom))
            {
                using (p)
                {
                    if (p.Id == moi) continue;
                    string cheminP = null;
                    try { cheminP = p.MainModule.FileName; } catch (Exception) { }
                    if (!DoitTuer(p.SessionId, cheminP, session, chemin)) continue;
                    p.Kill(); p.WaitForExit(5000); any = true;
                }
            }
            return any;
        }

        /// <summary>Relance via l'explorateur : le programme démarre avec les droits de l'utilisateur, pas ceux de l'outil.</summary>
        public void RestartProcess(string chemin)
        {
            if (!File.Exists(chemin)) throw new FileNotFoundException("exécutable introuvable : " + chemin);
            SystemExe.OuvrirAvecExplorateur(chemin);
        }

        public string SetPlan(string guid)
        {
            if (!Regex.IsMatch(guid ?? "", @"^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$")) throw new ArgumentException("GUID de plan invalide.");
            var avant = GetPlan();
            Powercfg("/setactive " + guid);
            return avant;
        }

        public string GetPlan()
        {
            var m = Regex.Match(Powercfg("/getactivescheme"), @"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
            if (!m.Success) throw new InvalidOperationException("plan actif illisible");
            return m.Value;
        }

        private static string Powercfg(string args)
        {
            var psi = new ProcessStartInfo(SystemExe.Chemin("powercfg.exe"), args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                var o = p.StandardOutput.ReadToEnd(); var e = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(15000)) { try { p.Kill(); } catch (Exception) { } throw new System.TimeoutException("powercfg n'a pas répondu"); }
                if (p.ExitCode != 0) throw new InvalidOperationException("powercfg " + args + " : " + (e + o).Trim());
                return o;
            }
        }
    }
}
