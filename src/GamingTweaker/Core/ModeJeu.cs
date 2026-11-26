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
            // Anti-triche : les fermer déconnecte la partie en cours et peut valoir une sanction ; Vanguard exige un redémarrage pour repartir.
            "vgtray", "vgc", "FACEITService", "FACEIT", "EasyAntiCheat", "EasyAntiCheat_EOS", "EasyAntiCheat_Setup", "BEService", "PnkBstrA", "PnkBstrB",
        };
        /// <summary>Jamais proposés ni arrêtés : services dont dépend le système, la sécurité, l'audio, l'affichage, le réseau, les anti-triche et le Game Pass.</summary>
        public static readonly string[] ServicesProteges =
        {
            "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "SamSs", "EventLog", "PlugPlay", "Power", "ProfSvc", "UserManager", "Winmgmt", "CryptSvc", "BFE", "MpsSvc",
            "Dhcp", "Dnscache", "nsi", "Themes", "WinDefend", "WdNisSvc", "Sense", "SecurityHealthService", "wscsvc", "AudioSrv", "AudioEndpointBuilder",
            "NVDisplay.ContainerLocalSystem", "NvContainerLocalSystem", "RtkAudioUniversalService", "TrustedInstaller", "gpsvc",
            // Anti-triche (Vanguard, Easy Anti-Cheat, BattlEye, FACEIT, ESEA, PunkBuster, nProtect, XIGNCODE) et services du Game Pass.
            "vgc", "vgk", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "BEDaisy", "FACEIT", "ESEADriver2", "PnkBstrA", "PnkBstrB", "npggsvc", "xhunter1",
            "GamingServices", "GamingServicesNet",
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
            [JsonProperty("nvidia", NullValueHandling = NullValueHandling.Ignore)] public List<EntreeNvidia> Nvidia { get; set; }
        }

        /// <summary>Réglage NVIDIA : valeur d'avant (défini dans le profil global ou non), valeur posée. etat : non-lance | en-cours | change | echec | retabli.</summary>
        private class EntreeNvidia
        {
            [JsonProperty("id")] public uint Id { get; set; }
            [JsonProperty("defini")] public bool Defini { get; set; }
            [JsonProperty("avant", NullValueHandling = NullValueHandling.Ignore)] public long? Avant { get; set; }
            [JsonProperty("voulu")] public uint Voulu { get; set; }
            [JsonProperty("etat")] public string Etat { get; set; }
        }

        private readonly string dossier;
        private readonly IModeJeuActions actions;
        private readonly string lockName;
        private readonly Action apresEtatEcrit;
        private readonly INvidiaReglages nvidia;

        /// <param name="machineDir">Dossier de la machine : modejeu.json y est écrit.</param>
        /// <param name="apresEtatEcrit">Appelé après chaque écriture de l'état (tests : vérifier qu'il précède toute action).</param>
        public ModeJeu(string machineDir, IModeJeuActions actions, string lockName = null, Action apresEtatEcrit = null, INvidiaReglages nvidia = null)
        {
            dossier = machineDir; this.actions = actions; this.lockName = lockName; this.apresEtatEcrit = apresEtatEcrit; this.nvidia = nvidia;
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
                if (c.Nvidia != ProfilNvidia.Aucun)
                {
                    if (nvidia == null || !nvidia.Disponible) bilan.Avertissements.Add("Profil NVIDIA ignoré : pilote NVIDIA introuvable.");
                    else etat.Nvidia = ProfilsNvidia.Reglages(c.Nvidia).Select(r => new EntreeNvidia { Id = r.Key, Voulu = r.Value, Etat = "non-lance" }).ToList();
                }

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
                if (etat.Nvidia != null)
                {
                    int n = 0;
                    foreach (var r in etat.Nvidia)
                    {
                        // Valeur d'avant lue et enregistrée AVANT l'écriture : un arrêt brutal laisse de quoi la remettre.
                        try
                        {
                            var avant = nvidia.Lire(r.Id);
                            r.Defini = avant[0] == 1; r.Avant = avant[1] < 0 ? (long?)null : avant[1];
                            r.Etat = "en-cours"; Ecrire(etat);
                            nvidia.Ecrire(r.Id, r.Voulu);
                            r.Etat = "change"; n++;
                            // Relecture : un pilote qui ignore l'écriture (identifiant inconnu de cette version) est signalé, pas annoncé comme appliqué.
                            var apres = nvidia.Lire(r.Id);
                            if (apres[1] != r.Voulu)
                            {
                                n--;
                                bilan.Avertissements.Add(ProfilsNvidia.NomReglage(r.Id) + " : le pilote n'a pas pris la valeur (lu : " + ProfilsNvidia.NomValeur(r.Id, apres[1]) + ").");
                                TraceAppli.Ecrire("NVIDIA : 0x" + r.Id.ToString("X8") + " écrit 0x" + r.Voulu.ToString("X") + ", relu " + apres[1]);
                            }
                        }
                        catch (Exception ex)
                        {
                            r.Etat = r.Etat == "en-cours" ? "echec" : r.Etat == "change" ? "change" : "non-lance";
                            bilan.Avertissements.Add(ProfilsNvidia.NomReglage(r.Id) + " : non appliqué (" + ex.Message + ").");
                            TraceAppli.Erreur("NVIDIA : réglage 0x" + r.Id.ToString("X8"), ex);
                        }
                        Ecrire(etat);
                    }
                    if (n > 0) bilan.Actions.Add("Profil NVIDIA " + ProfilsNvidia.Libelle(c.Nvidia) + " appliqué (" + n + " réglage(s)).");
                }
                if (c.LibererMemoire)
                {
                    var memoire = actions as IViderMemoire;
                    if (memoire == null) bilan.Avertissements.Add("Libération de mémoire indisponible.");
                    else
                    {
                        try
                        {
                            var mo = memoire.ViderMemoire(c.Exclusions);
                            bilan.Actions.Add(mo > 0 ? "Mémoire libérée : " + mo + " Mo disponibles en plus." : "Mémoire des programmes en arrière-plan libérée.");
                        }
                        catch (Exception ex) { bilan.Avertissements.Add("Mémoire non libérée : " + ex.Message); }
                    }
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

                if (etat.Nvidia != null && etat.Nvidia.Any(r => r.Etat == "change" || r.Etat == "en-cours" || r.Etat == "echec"))
                {
                    if (nvidia == null || !nvidia.Disponible) { echec = true; bilan.Avertissements.Add("Réglages NVIDIA non rétablis : pilote NVIDIA introuvable."); }
                    else
                    {
                        int n = 0;
                        foreach (var r in etat.Nvidia.Where(x => x.Etat == "change" || x.Etat == "en-cours" || x.Etat == "echec"))
                        {
                            try
                            {
                                // Seulement si la valeur est encore celle du Mode Jeu : un réglage changé depuis par l'utilisateur est conservé.
                                var actuel = nvidia.Lire(r.Id);
                                if (actuel[1] == r.Voulu)
                                {
                                    if (r.Defini && r.Avant.HasValue) nvidia.Ecrire(r.Id, (uint)r.Avant.Value); else nvidia.Retirer(r.Id);
                                    n++;
                                }
                                r.Etat = "retabli";
                            }
                            catch (Exception ex) { echec = true; bilan.Avertissements.Add(ProfilsNvidia.NomReglage(r.Id) + " : non rétabli (" + ex.Message + ")."); TraceAppli.Erreur("NVIDIA : rétablissement 0x" + r.Id.ToString("X8"), ex); }
                        }
                        if (n > 0) bilan.Actions.Add("Réglages NVIDIA rétablis (" + n + ").");
                    }
                }

                if (echec) { try { Ecrire(etat); } catch (Exception exc) { TraceAppli.Erreur("mode jeu : état non réécrit après un rétablissement incomplet", exc); } }
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
}
