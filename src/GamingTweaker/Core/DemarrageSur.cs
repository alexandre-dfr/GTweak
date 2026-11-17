using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Contrôles du tout début du démarrage. N'utilise que des types du framework : appelé avant le premier chargement
    /// d'une DLL embarquée (Newtonsoft.Json, LibreHardwareMonitor…).
    /// </summary>
    public static class DemarrageSur
    {
        public const string MessageOccupe = "Une autre exécution de GamingTweaker est en cours.";

        /// <summary>Noms des DLL embarquées en ressources (ex. Newtonsoft.Json.dll).</summary>
        public static List<string> DllsEmbarquees()
        {
            return typeof(DemarrageSur).Assembly.GetManifestResourceNames()
                .Where(n => n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && n.IndexOf('/') < 0).ToList();
        }

        /// <summary>
        /// DLL du dossier de l'exe qui portent le nom d'une DLL embarquée : le chargeur .NET les prendrait AVANT la copie embarquée
        /// (AssemblyResolve n'est appelé qu'après le sondage du dossier). Décision pure, testée.
        /// </summary>
        public static List<string> DllsEnConflit(string dossierExe, IEnumerable<string> embarquees)
        {
            var res = new List<string>();
            if (string.IsNullOrEmpty(dossierExe) || !Directory.Exists(dossierExe)) return res;
            var noms = new HashSet<string>(embarquees ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.GetFiles(dossierExe, "*.dll"))
                if (noms.Contains(Path.GetFileName(f))) res.Add(Path.GetFileName(f));
            res.Sort(StringComparer.OrdinalIgnoreCase);
            return res;
        }

        public static string MessageDllEnConflit(IEnumerable<string> dlls, string dossierExe)
        {
            return "Par sécurité, GamingTweaker refuse de démarrer : " + string.Join(", ", dlls) + " se trouve à côté de l'exe (" + dossierExe
                + "). Cette DLL serait chargée à la place de la copie intégrée à l'exe. Supprimez-la ou placez GamingTweaker.exe seul dans un dossier.";
        }

        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800, LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400;

        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetDefaultDllDirectories(uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr AddDllDirectory(string dir);

        /// <summary>
        /// Recherche des DLL natives limitée à System32 (plus le dossier du framework .NET et son sous-dossier WPF, réservés aux administrateurs) :
        /// le dossier de l'exe n'est plus cherché.
        /// </summary>
        public static void RestreindreRechercheDll()
        {
            try
            {
                var fw = RuntimeEnvironment.GetRuntimeDirectory();
                foreach (var d in new[] { fw, Path.Combine(fw, "WPF") })
                    if (Directory.Exists(d)) AddDllDirectory(d);
                SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32 | LOAD_LIBRARY_SEARCH_USER_DIRS);
            }
            catch (Exception ex) { TraceAppli.Erreur("restriction de la recherche des DLL natives impossible", ex); }
        }

        /// <summary>
        /// Avant le parcours de %ProgramFiles%\GamingTweaker : verrou d'instance d'abord (occupé : message, aucun parcours), puis contrôle
        /// du dossier protégé, verrou libéré ensuite. Renvoie null si tout va bien, sinon le message d'erreur.
        /// </summary>
        public static string VerifierDossierProtege(string racine, Func<bool> estAdministrateur, string lockName = null, bool definirProprietaire = true)
        {
            using (var verrou = SingleInstance.TryAcquire(lockName))
            {
                if (verrou == null) return MessageOccupe;
                var protection = DossierProtege.SecuriserAuDemarrage(racine, estAdministrateur, definirProprietaire);
                return protection.Erreur;
            }
        }
    }
}
