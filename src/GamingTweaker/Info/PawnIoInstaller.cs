using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace GamingTweaker.Info
{
    /// <summary>Détection et installation (sur accord explicite) du pilote PawnIO utilisé par LibreHardwareMonitor.</summary>
    public static class PawnIoInstaller
    {
        public const string WingetId = "namazso.PawnIO";
        public const string PageOfficielle = "https://pawnio.eu/";

        public static bool IsInstalled()
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (var svc = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO")) { if (svc != null) return true; }
                    using (var sw = hklm.OpenSubKey(@"SOFTWARE\PawnIO")) { if (sw != null) return true; }
                }
            }
            catch { }
            return false;
        }

        /// <summary>À n'appeler qu'après l'accord de l'utilisateur. winget installe le paquet vérifié ; sans winget, ouvre la page officielle. Renvoie un message.</summary>
        public static string Install()
        {
            try
            {
                // winget vérifié (paquet DesktopAppInstaller sous Program Files\WindowsApps), jamais celui du PATH.
                var winget = Core.SystemExe.Winget();
                if (winget == null) throw new System.ComponentModel.Win32Exception(2, "winget introuvable");
                var psi = new ProcessStartInfo(winget, "install --id " + WingetId + " --exact --source winget --accept-package-agreements --accept-source-agreements --disable-interactivity")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    if (p.WaitForExit(300000) && p.ExitCode == 0 && IsInstalled()) return "PawnIO est installé.";
                }
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (Exception) { }
            // Navigateur ouvert par l'explorateur du bureau : il ne tourne pas en administrateur.
            try { Core.SystemExe.OuvrirUrl(PageOfficielle); } catch { }
            return "L'installation par winget n'a pas abouti : la page officielle de PawnIO a été ouverte dans le navigateur.";
        }
    }
}
