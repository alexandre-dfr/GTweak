using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GamingTweaker.Core
{
    /// <summary>Dossiers de l'utilisateur du bureau dont l'outil nettoie les caches (déduits du registre ProfileList, pas de l'environnement).</summary>
    public class ProfilUtilisateur
    {
        public string Profil { get; set; }
        public string LocalAppData { get; set; }
        public string AppData { get; set; }
        public string Temp { get; set; }
    }

    /// <summary>Script écrit dans le dossier Run et verrouillé : poignée en lecture, partage lecture seule, gardée jusqu'à Dispose (puis supprimé).</summary>
    public sealed class ScriptVerrouille : IDisposable
    {
        private FileStream poignee;
        public string Chemin { get; private set; }

        internal ScriptVerrouille(string chemin, string contenu)
        {
            Chemin = chemin;
            // Écriture exclusive, puis réouverture en lecture avec partage lecture seule : personne ne peut plus écrire, renommer ou
            // supprimer le fichier tant que la poignée est tenue (relances après délai comprises) ; powershell.exe peut le lire.
            using (var w = new FileStream(chemin, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var octets = new UTF8Encoding(true).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(contenu)).ToArray();
                w.Write(octets, 0, octets.Length);
                w.Flush(true);
            }
            poignee = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public void Dispose()
        {
            if (poignee == null) return;
            poignee.Dispose(); poignee = null;
            try { File.Delete(Chemin); } catch (Exception) { }
        }
    }

    /// <summary>
    /// Fabrique unique des lancements de powershell.exe : chemin absolu (System32), environnement propre (aucune variable héritée de
    /// l'utilisateur), scripts écrits dans le dossier Run protégé (%ProgramFiles%\GamingTweaker\Run) quand l'outil est élevé.
    /// </summary>
    public static class PowerShellLanceur
    {
        /// <summary>Remplaçable par les tests : vrai si le processus est élevé (scripts sous la racine protégée).</summary>
        internal static Func<bool> EstAdministrateur = ElevationCheck.EstAdministrateur;
        /// <summary>Remplaçable par les tests : racine protégée (%ProgramFiles%\GamingTweaker).</summary>
        internal static Func<string> RacineProtegee = () => DataPaths.RacineProtegeeParDefaut;

        /// <summary>Ligne placée en tête de chaque script : modules uniquement depuis $PSHOME (jamais Documents ni Program Files).</summary>
        public const string LigneModules = "$env:PSModulePath = [IO.Path]::Combine($PSHOME, 'Modules')\r\n";

        private static readonly Lazy<ProfilUtilisateur> profilBureau = new Lazy<ProfilUtilisateur>(LireProfilBureau);

        /// <summary>
        /// Dossier des scripts exécutés. Élevé : &lt;racine protégée&gt;\Run (créé avec la DACL protégée, Utilisateurs en lecture seule).
        /// Non élevé (tests, build Debug) : %TEMP%\GamingTweaker\Run, sans enjeu puisque rien n'y est exécuté en administrateur.
        /// </summary>
        public static string ChoisirDossierRun(bool admin, string racineProtegee, string tempUtilisateur)
        {
            return admin ? Path.Combine(racineProtegee, DossierProtege.NomDossierJetable) : Path.Combine(tempUtilisateur, "GamingTweaker", "Run");
        }

        public static string PreparerDossierRun()
        {
            bool admin;
            try { admin = EstAdministrateur(); } catch (Exception) { admin = false; }
            var racine = RacineProtegee();
            var run = ChoisirDossierRun(admin, racine, Path.GetTempPath());
            if (admin)
            {
                // Racine absente (premier lancement sans passage par le contrôle de démarrage) : créée et vérifiée d'abord.
                if (!Directory.Exists(racine)) DossierProtege.Securiser(racine);
                // Run est aussi le TEMP des programmes lancés (winget, installateurs) : ce qu'ils y créent appartient au compte de l'utilisateur.
                // OWNER RIGHTS (hérité) empêche ce compte, sans élévation, d'en changer les droits ; au démarrage suivant, ces éléments
                // sont supprimés (contenu jetable) au lieu d'arrêter l'application.
                if (!Directory.Exists(run)) DossierProtege.CreerDossier(run, true, true);
                else
                {
                    try { DossierProtege.RestreindreProprietaire(run); }
                    catch (Exception ex) { TraceAppli.Erreur("dossier Run : restriction du propriétaire impossible", ex); }
                }
            }
            else Directory.CreateDirectory(run);
            NettoyerAnciens(run);
            return run;
        }

        /// <summary>Scripts laissés par une exécution interrompue (plus d'une heure) : supprimés ; un script en cours (poignée tenue) ne l'est pas.</summary>
        private static void NettoyerAnciens(string run)
        {
            try
            {
                foreach (var f in Directory.GetFiles(run, "*.ps1"))
                {
                    try { if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddHours(-1)) File.Delete(f); } catch (Exception) { }
                }
            }
            catch (Exception exc) { TraceAppli.Erreur("nettoyage des anciens scripts", exc); }
        }

        /// <summary>Écrit le script (UTF-8 avec BOM) dans le dossier Run et le garde verrouillé jusqu'à Dispose.</summary>
        private static readonly object verrouRun = new object();

        public static ScriptVerrouille EcrireScript(string prefixe, string contenu)
        {
            // Sérialisé : plusieurs détections en parallèle ne doivent pas créer, sécuriser ou nettoyer le dossier Run en même temps.
            lock (verrouRun)
            {
                var run = PreparerDossierRun();
                return new ScriptVerrouille(Path.Combine(run, prefixe + "-" + Guid.NewGuid().ToString("N") + ".ps1"), contenu);
            }
        }

        /// <summary>Arguments pour exécuter un fichier de script.</summary>
        public static string ArgumentsFichier(string chemin)
        {
            return "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + chemin + "\"";
        }

        /// <summary>Arguments pour un court script passé en -EncodedCommand (aucun fichier).</summary>
        public static string ArgumentsEncodes(string script)
        {
            var texte = LigneModules + script;
            return "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(texte));
        }

        /// <summary>ProcessStartInfo de powershell.exe (chemin absolu, fenêtre cachée, sorties redirigées en UTF-8, environnement propre).</summary>
        public static ProcessStartInfo Creer(string arguments)
        {
            var psi = new ProcessStartInfo(SystemExe.PowerShell, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = SystemExe.DossierSystem32,
            };
            string run;
            try { run = PreparerDossierRun(); } catch (Exception) { run = SystemExe.DossierSystem32; }
            ProfilUtilisateur profil;
            try { profil = profilBureau.Value; } catch (Exception) { profil = null; }
            var env = EnvironnementPropre(DossiersSysteme(), run, profil);
            psi.EnvironmentVariables.Clear();
            foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
            return psi;
        }

        /// <summary>Dossiers système lus par les API (dossiers connus, GetSystemDirectory), jamais par l'environnement.</summary>
        public static Dictionary<string, string> DossiersSysteme()
        {
            Func<Environment.SpecialFolder, string> f = sf => Environment.GetFolderPath(sf);
            var win = SystemExe.DossierWindows;
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Windows", win },
                { "System32", SystemExe.DossierSystem32 },
                { "ProgramFiles", f(Environment.SpecialFolder.ProgramFiles) },
                { "ProgramFilesX86", f(Environment.SpecialFolder.ProgramFilesX86) },
                { "CommonProgramFiles", f(Environment.SpecialFolder.CommonProgramFiles) },
                { "CommonProgramFilesX86", f(Environment.SpecialFolder.CommonProgramFilesX86) },
                { "ProgramData", f(Environment.SpecialFolder.CommonApplicationData) },
                { "SystemDrive", (Path.GetPathRoot(win) ?? "C:\\").TrimEnd('\\') },
            };
        }

        /// <summary>
        /// Environnement complet transmis à powershell.exe (rien n'est hérité). Décision pure, testée : SystemRoot, windir, ProgramFiles,
        /// ProgramData, SystemDrive depuis les dossiers connus ; TEMP/TMP = dossier Run ; PSModulePath = System32\WindowsPowerShell\v1.0\Modules ;
        /// PATH limité aux dossiers système ; dossiers de l'utilisateur du bureau depuis ProfileList (GT_USERTEMP = son dossier temporaire).
        /// </summary>
        public static Dictionary<string, string> EnvironnementPropre(IDictionary<string, string> sys, string dossierRun, ProfilUtilisateur profil)
        {
            var s32 = sys["System32"];
            var e = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "SystemRoot", sys["Windows"] },
                { "windir", sys["Windows"] },
                { "SystemDrive", sys["SystemDrive"] },
                { "ProgramFiles", sys["ProgramFiles"] },
                { "ProgramW6432", sys["ProgramFiles"] },
                { "ProgramFiles(x86)", sys["ProgramFilesX86"] },
                { "CommonProgramFiles", sys["CommonProgramFiles"] },
                { "CommonProgramW6432", sys["CommonProgramFiles"] },
                { "CommonProgramFiles(x86)", sys["CommonProgramFilesX86"] },
                { "ProgramData", sys["ProgramData"] },
                { "ALLUSERSPROFILE", sys["ProgramData"] },
                { "ComSpec", Path.Combine(s32, "cmd.exe") },
                { "PATH", string.Join(";", s32, sys["Windows"], Path.Combine(s32, "Wbem"), Path.Combine(s32, "WindowsPowerShell", "v1.0")) },
                { "PATHEXT", ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC" },
                { "PSModulePath", Path.Combine(s32, "WindowsPowerShell", "v1.0", "Modules") },
                { "TEMP", dossierRun },
                { "TMP", dossierRun },
                { "OS", "Windows_NT" },
                { "PROCESSOR_ARCHITECTURE", "AMD64" },
                { "NUMBER_OF_PROCESSORS", Environment.ProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { "COMPUTERNAME", Environment.MachineName },
                { "USERNAME", Environment.UserName },
                { "USERDOMAIN", Environment.UserDomainName },
            };
            if (profil != null && !string.IsNullOrEmpty(profil.Profil))
            {
                e["USERPROFILE"] = profil.Profil;
                var racine = Path.GetPathRoot(profil.Profil) ?? "";
                e["HOMEDRIVE"] = racine.TrimEnd('\\');
                e["HOMEPATH"] = profil.Profil.Substring(racine.Length - (racine.EndsWith("\\") ? 1 : 0));
                if (!string.IsNullOrEmpty(profil.LocalAppData)) e["LOCALAPPDATA"] = profil.LocalAppData;
                if (!string.IsNullOrEmpty(profil.AppData)) e["APPDATA"] = profil.AppData;
                if (!string.IsNullOrEmpty(profil.Temp)) e["GT_USERTEMP"] = profil.Temp;
            }
            return e;
        }

        /// <summary>ProfileImagePath brut (REG_EXPAND_SZ non développé) : seules %SystemDrive% et %SystemRoot% sont développées, depuis les API.</summary>
        public static string DevelopperCheminProfil(string brut, string systemDrive, string systemRoot)
        {
            if (string.IsNullOrWhiteSpace(brut)) return null;
            var r = brut.Trim();
            r = ReplaceIgnoreCase(r, "%SystemDrive%", systemDrive);
            r = ReplaceIgnoreCase(r, "%SystemRoot%", systemRoot);
            if (r.IndexOf('%') >= 0 || !Path.IsPathRooted(r) || r.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            try { return Path.GetFullPath(r).TrimEnd('\\'); } catch (Exception) { return null; }
        }

        private static string ReplaceIgnoreCase(string s, string quoi, string par)
        {
            int i;
            while ((i = s.IndexOf(quoi, StringComparison.OrdinalIgnoreCase)) >= 0) s = s.Substring(0, i) + par + s.Substring(i + quoi.Length);
            return s;
        }

        /// <summary>Dossiers standard d'un profil (AppData\Local, AppData\Roaming, AppData\Local\Temp).</summary>
        public static ProfilUtilisateur ProfilDepuisDossier(string profil)
        {
            if (string.IsNullOrEmpty(profil)) return null;
            var local = Path.Combine(profil, "AppData", "Local");
            return new ProfilUtilisateur { Profil = profil, LocalAppData = local, AppData = Path.Combine(profil, "AppData", "Roaming"), Temp = Path.Combine(local, "Temp") };
        }

        /// <summary>Profil de l'utilisateur du bureau (sinon du compte courant) d'après HKLM\...\ProfileList\&lt;SID&gt;.</summary>
        private static ProfilUtilisateur LireProfilBureau()
        {
            string sid = null;
            try { sid = ElevationCheck.SidDuBureau(); } catch (Exception) { }
            if (string.IsNullOrEmpty(sid))
                using (var id = WindowsIdentity.GetCurrent()) sid = id.User == null ? null : id.User.Value;
            if (string.IsNullOrEmpty(sid)) return null;
            var sys = DossiersSysteme();
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid))
            {
                if (k == null) return null;
                var brut = k.GetValue("ProfileImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                return ProfilDepuisDossier(DevelopperCheminProfil(brut, sys["SystemDrive"], sys["Windows"]));
            }
        }

        /// <summary>
        /// Arrête le processus et ses enfants (taskkill /T, chemin absolu), puis attend la fermeture des flux redirigés (au plus 5 s) :
        /// les lignes déjà émises restent reçues.
        /// </summary>
        public static void Tuer(Process p)
        {
            try
            {
                var psi = new ProcessStartInfo(SystemExe.Chemin("taskkill.exe"), "/T /F /PID " + p.Id)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var k = Process.Start(psi)) { k.WaitForExit(10000); }
            }
            catch (Exception exc) { TraceAppli.Erreur("arrêt du script (taskkill)", exc); }
            try { if (!p.HasExited) p.Kill(); } catch (Exception) { }
            try { p.WaitForExit(5000); } catch (Exception) { }
            AttendreFlux(p);
        }

        /// <summary>WaitForExit sans paramètre (vide les flux asynchrones), borné à 5 s.</summary>
        public static void AttendreFlux(Process p)
        {
            try { Task.Run(() => { try { p.WaitForExit(); } catch (Exception) { } }).Wait(5000); } catch (Exception) { }
        }
    }
}
