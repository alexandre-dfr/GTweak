using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Chemins absolus des exécutables système lancés par l'outil. Jamais de nom nu : Windows chercherait d'abord dans le dossier de l'exe
    /// (un powershell.exe déposé à côté de l'exe portable s'exécuterait en administrateur), puis dans le PATH. Les dossiers viennent des API
    /// Windows (GetSystemDirectory, GetWindowsDirectory, dossiers connus), jamais des variables d'environnement.
    /// </summary>
    public static class SystemExe
    {
        private static readonly HashSet<string> DansSystem32 = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "taskkill.exe", "schtasks.exe", "powercfg.exe", "cmd.exe",
        };

        /// <summary>Dossier Windows (C:\Windows), par l'API.</summary>
        public static string DossierWindows { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }

        /// <summary>System32 (processus 64 bits : le vrai System32).</summary>
        public static string DossierSystem32 { get { return Environment.SystemDirectory; } }

        public static string PowerShell { get { return Chemin("powershell.exe"); } }
        public static string Explorer { get { return Chemin("explorer.exe"); } }

        /// <summary>Chemin absolu d'un exécutable système connu (powershell, explorer, taskkill, schtasks, powercfg, cmd). Nom inconnu : exception.</summary>
        public static string Chemin(string nom)
        {
            if (string.IsNullOrEmpty(nom)) throw new ArgumentException("Nom d'exécutable manquant.");
            var n = nom.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? nom : nom + ".exe";
            if (string.Equals(n, "powershell.exe", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(DossierSystem32, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (string.Equals(n, "explorer.exe", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(DossierWindows, "explorer.exe");
            if (DansSystem32.Contains(n)) return Path.Combine(DossierSystem32, n);
            throw new ArgumentException("Exécutable système non prévu : " + nom);
        }

        /// <summary>
        /// winget.exe d'après l'emplacement d'installation du paquet Microsoft.DesktopAppInstaller. Accepté uniquement sous
        /// &lt;Program Files&gt;\WindowsApps (dossier réservé au système) ; sinon null (« winget introuvable »).
        /// </summary>
        public static string WingetDepuisEmplacement(string emplacement, string programFiles)
        {
            if (string.IsNullOrWhiteSpace(emplacement) || string.IsNullOrWhiteSpace(programFiles)) return null;
            try
            {
                var e = emplacement.Trim();
                if (e.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || e.Contains("..") || !Path.IsPathRooted(e) || e.StartsWith(@"\\", StringComparison.Ordinal)) return null;
                var racine = Path.GetFullPath(Path.Combine(programFiles, "WindowsApps")).TrimEnd('\\') + "\\";
                var plein = Path.GetFullPath(e).TrimEnd('\\') + "\\";
                if (!plein.StartsWith(racine, StringComparison.OrdinalIgnoreCase) || plein.Length <= racine.Length) return null;
                return Path.Combine(plein.TrimEnd('\\'), "winget.exe");
            }
            catch (Exception) { return null; }
        }

        /// <summary>Script de lecture de l'emplacement du paquet winget (version la plus récente).</summary>
        public const string ScriptEmplacementWinget =
            "$p = @(Get-AppxPackage -Name Microsoft.DesktopAppInstaller -ErrorAction SilentlyContinue | Sort-Object Version -Descending | Select-Object -First 1)\r\n" +
            "if ($p.Count -gt 0) { [Console]::Out.WriteLine([string]$p[0].InstallLocation) }";

        /// <summary>winget.exe vérifié (paquet DesktopAppInstaller sous Program Files\WindowsApps, fichier présent), ou null.</summary>
        public static string Winget()
        {
            try
            {
                var sortie = Lists.PowerShellQuery.RunAsync(ScriptEmplacementWinget, System.Threading.CancellationToken.None, 30000).GetAwaiter().GetResult();
                var ligne = (sortie ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).LastOrDefault();
                var exe = WingetDepuisEmplacement(ligne, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
                return exe != null && File.Exists(exe) ? exe : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Adresse acceptée pour l'ouverture dans le navigateur : https, sans guillemet ni caractère de contrôle.</summary>
        public static bool UrlAcceptee(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
            if (url.Any(c => c == '"' || char.IsControl(c) || char.IsWhiteSpace(c))) return false;
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps;
        }

        /// <summary>
        /// Ouvre une adresse https par &lt;Windows&gt;\explorer.exe "&lt;url&gt;" : l'explorateur du bureau la transmet au navigateur par défaut,
        /// qui tourne avec les droits de l'utilisateur et non ceux de l'outil.
        /// </summary>
        public static void OuvrirUrl(string url)
        {
            if (!UrlAcceptee(url)) throw new ArgumentException("Adresse refusée : " + url);
            using (Process.Start(new ProcessStartInfo(Explorer, "\"" + url + "\"") { UseShellExecute = false })) { }
        }

        /// <summary>Ouvre un dossier ou relance un programme par l'explorateur (droits de l'utilisateur du bureau).</summary>
        public static void OuvrirAvecExplorateur(string chemin)
        {
            if (string.IsNullOrEmpty(chemin) || chemin.IndexOf('"') >= 0) throw new ArgumentException("Chemin refusé.");
            using (Process.Start(new ProcessStartInfo(Explorer, "\"" + chemin + "\"") { UseShellExecute = false })) { }
        }
    }
}
