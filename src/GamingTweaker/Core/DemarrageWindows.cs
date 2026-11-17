using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace GamingTweaker.Core
{
    /// <summary>
    /// « Démarrer avec Windows » : tâche \GamingTweaker\Demarrage, déclenchée à l'ouverture de session de l'utilisateur, avec les droits les plus
    /// élevés (sans invite UAC). Elle lance « GamingTweaker.exe --tray » depuis une copie placée dans %ProgramFiles%\GamingTweaker\Demarrage,
    /// dossier protégé : jamais depuis l'emplacement de l'exe portable, où un utilisateur standard pourrait le remplacer.
    /// Dossier distinct de App\ (routine) : installer ou retirer l'une ne touche pas à l'autre.
    /// </summary>
    public static class DemarrageWindows
    {
        public const string NomTache = @"\GamingTweaker\Demarrage";
        public const string Argument = "--tray";

        public static string Dossier { get { return Path.Combine(DataPaths.RacineProtegeeParDefaut, "Demarrage"); } }
        public static string ExeCopie { get { return Path.Combine(Dossier, "GamingTweaker.exe"); } }

        private static string E(string s) { return SecurityElement.Escape(s ?? ""); }

        public static string BuildTaskXml(string exe, string user)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
            sb.AppendLine("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
            sb.AppendLine("  <RegistrationInfo>");
            sb.AppendLine("    <Author>GamingTweaker</Author>");
            sb.AppendLine("    <Description>Démarre GamingTweaker dans la zone de notification (Mode Jeu automatique).</Description>");
            sb.AppendLine("  </RegistrationInfo>");
            sb.AppendLine("  <Triggers>");
            sb.AppendLine("    <LogonTrigger>");
            sb.AppendLine("      <Enabled>true</Enabled>");
            sb.AppendLine("      <UserId>" + E(user) + "</UserId>");
            sb.AppendLine("      <Delay>PT15S</Delay>");
            sb.AppendLine("    </LogonTrigger>");
            sb.AppendLine("  </Triggers>");
            sb.AppendLine("  <Principals>");
            sb.AppendLine("    <Principal id=\"Author\">");
            sb.AppendLine("      <UserId>" + E(user) + "</UserId>");
            sb.AppendLine("      <LogonType>InteractiveToken</LogonType>");
            sb.AppendLine("      <RunLevel>HighestAvailable</RunLevel>");
            sb.AppendLine("    </Principal>");
            sb.AppendLine("  </Principals>");
            sb.AppendLine("  <Settings>");
            sb.AppendLine("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
            sb.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
            sb.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
            sb.AppendLine("    <AllowHardTerminate>true</AllowHardTerminate>");
            sb.AppendLine("    <AllowStartOnDemand>true</AllowStartOnDemand>");
            sb.AppendLine("    <Enabled>true</Enabled>");
            sb.AppendLine("    <Hidden>false</Hidden>");
            sb.AppendLine("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
            sb.AppendLine("    <Priority>7</Priority>");
            sb.AppendLine("  </Settings>");
            sb.AppendLine("  <Actions Context=\"Author\">");
            sb.AppendLine("    <Exec>");
            sb.AppendLine("      <Command>" + E("\"" + exe + "\"") + "</Command>");
            sb.AppendLine("      <Arguments>" + Argument + "</Arguments>");
            sb.AppendLine("    </Exec>");
            sb.AppendLine("  </Actions>");
            sb.Append("</Task>");
            return sb.ToString();
        }

        public static bool EstInstalle()
        {
            try { string sortie; return Planification.Schtasks("/Query /TN \"" + NomTache + "\"", out sortie) == 0; }
            catch (Exception) { return false; }
        }

        /// <summary>Copie l'exe en cours (vérifié, voir ExeEnCours) dans le dossier protégé puis enregistre la tâche. Sous le verrou d'instance.</summary>
        public static void Installer(string lockName = null)
        {
            using (var verrou = SingleInstance.TryAcquire(lockName))
            {
                if (verrou == null) throw new InvalidOperationException(Planification.MessageOccupe.Replace("la routine", "le démarrage avec Windows"));
                if (!ElevationCheck.SameUserAsDesktop())
                    throw new InvalidOperationException("Le démarrage avec Windows n'a pas été activé : GamingTweaker tourne sous un autre compte que celui du bureau. Activez-le depuis la session de l'utilisateur.");
                DossierProtege.Securiser(DataPaths.RacineProtegeeParDefaut);
                string source;
                using (var p = Process.GetCurrentProcess()) source = p.MainModule.FileName;
                if (!Planification.DepuisLaCopie(source, ExeCopie))
                {
                    if (Directory.Exists(Dossier)) Directory.Delete(Dossier, true);
                    DossierProtege.CreerDossier(Dossier);
                    ExeEnCours.CopierVers(ExeCopie);
                }
                string user;
                using (var id = WindowsIdentity.GetCurrent()) user = id.User != null ? id.User.Value : id.Name;
                Planification.EnregistrerTache(NomTache, BuildTaskXml(ExeCopie, user));
            }
        }

        /// <summary>Supprime la tâche puis la copie (sauf si c'est l'exe en cours : elle sera supprimée au prochain retrait).</summary>
        public static void Retirer(string lockName = null)
        {
            using (var verrou = SingleInstance.TryAcquire(lockName))
            {
                if (verrou == null) throw new InvalidOperationException(Planification.MessageOccupe.Replace("la routine", "le démarrage avec Windows"));
                Planification.SupprimerTacheEtDossier(NomTache);
                string source;
                using (var p = Process.GetCurrentProcess()) source = p.MainModule.FileName;
                if (!Planification.DepuisLaCopie(source, ExeCopie) && Directory.Exists(Dossier)) Directory.Delete(Dossier, true);
            }
        }
    }
}
