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
    /// <summary>Implémentation réelle : ServiceController avec délai, Process.Kill, powercfg.</summary>
    public class ModeJeuSysteme : IModeJeuActions, IViderMemoire
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [System.Runtime.InteropServices.DllImport("psapi.dll", SetLastError = true)] private static extern bool EmptyWorkingSet(IntPtr h);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        private const uint QueryLimited = 0x1000, SetQuota = 0x0100;

        /// <summary>
        /// Vide le jeu de travail des processus de la session (pas les services), sauf : processus protégés, GamingTweaker, fenêtre au premier
        /// plan et exécutables situés sous un chemin exclu (jeux). Aucun processus n'est fermé : Windows recharge les pages à la demande.
        /// </summary>
        public long ViderMemoire(IEnumerable<string> exclusions)
        {
            var excl = (exclusions ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.TrimEnd('\\')).ToList();
            int session = 0, moi = 0; uint premierPlan = 0;
            try { using (var p = Process.GetCurrentProcess()) { session = p.SessionId; moi = p.Id; } } catch (Exception) { }
            try { GetWindowThreadProcessId(GetForegroundWindow(), out premierPlan); } catch (Exception) { }
            var avant = MemoireDisponibleMo();
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        if (p.Id == moi || p.Id == premierPlan || p.SessionId != session || ModeJeu.EstProcessusProtege(p.ProcessName)) continue;
                        var h = OpenProcess(QueryLimited | SetQuota, false, p.Id);
                        if (h == IntPtr.Zero) continue;
                        try
                        {
                            var sb = new StringBuilder(1024); int n = sb.Capacity;
                            var chemin = QueryFullProcessImageNameW(h, 0, sb, ref n) ? sb.ToString(0, n) : null;
                            if (chemin != null && excl.Any(e => string.Equals(chemin, e, StringComparison.OrdinalIgnoreCase) || chemin.StartsWith(e + "\\", StringComparison.OrdinalIgnoreCase))) continue;
                            EmptyWorkingSet(h);
                        }
                        finally { CloseHandle(h); }
                    }
                    catch (Exception) { }
                }
            }
            return Math.Max(0, MemoireDisponibleMo() - avant);
        }

        private static long MemoireDisponibleMo()
        {
            try { return Mesures.RamDisponibleMo(); } catch (Exception) { return 0; }
        }

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
            // Borné (ProcessusBorne) : les deux flux sont lus en parallèle (plus de blocage sur un tampon plein) et la limite s'applique.
            var r = ProcessusBorne.Executer(psi, 15000, System.Threading.CancellationToken.None, "powercfg");
            if (r.CodeSortie != 0) throw new InvalidOperationException("powercfg " + args + " : " + (r.Erreur + r.Sortie).Trim());
            return r.Sortie;
        }
    }
}
