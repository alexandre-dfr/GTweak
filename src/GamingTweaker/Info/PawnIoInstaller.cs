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
                // Avant : ReadToEnd() bloquait jusqu'à la fin de winget, la limite de 5 min n'agissait jamais. Désormais bornée :
                // au-delà, winget et ses enfants (l'installateur) sont arrêtés et l'utilisateur est prévenu.
                var r = Core.ProcessusBorne.Executer(psi, 300000, System.Threading.CancellationToken.None, "winget (PawnIO)");
                if (r.CodeSortie == 0 && IsInstalled()) return "PawnIO est installé.";
            }
            catch (TimeoutException)
            {
                try { Core.SystemExe.OuvrirUrl(PageOfficielle); } catch { }
                return "L'installation par winget n'a pas abouti en 5 minutes et a été arrêtée : la page officielle de PawnIO a été ouverte dans le navigateur.";
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (Exception exc) { Core.TraceAppli.Erreur("PawnIO : installation par winget", exc); }
            // Navigateur ouvert par l'explorateur du bureau : il ne tourne pas en administrateur.
            try { Core.SystemExe.OuvrirUrl(PageOfficielle); } catch { }
            return "L'installation par winget n'a pas abouti : la page officielle de PawnIO a été ouverte dans le navigateur.";
        }
    }
}
