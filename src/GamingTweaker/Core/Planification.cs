using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GamingTweaker.Core
{
    public class RoutineConfig
    {
        public DayOfWeek Jour { get; set; }
        public TimeSpan Heure { get; set; }
        /// <summary>Chemin du fichier de profil à appliquer ; Install en copie le contenu dans ProgramData.</summary>
        public string Profil { get; set; }
    }

    /// <summary>État lu dans la tâche planifiée (lecture seule).</summary>
    public class RoutineEtat
    {
        public bool Existe { get; set; }
        public DayOfWeek? Jour { get; set; }
        public TimeSpan? Heure { get; set; }
        public string ProfilChemin { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Routine hebdomadaire : tâche \GamingTweaker\Routine (droits élevés, compte interactif) qui lance la copie de l'exe
    /// placée dans ProgramData avec « --profile &lt;copie du profil&gt; --silent ». Seule la construction du XML est pure.
    /// </summary>
    public static class Planification
    {
        public const string NomTache = @"\GamingTweaker\Routine";
        private const string Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        public static string DossierCopie { get { return DataPaths.DefaultProgramDataRoot; } }
        /// <summary>Sous-dossier de l'exe lancé par la tâche : rien d'autre n'y est écrit.</summary>
        public static string DossierApp { get { return Path.Combine(DossierCopie, "App"); } }
        public static string ExeCopie { get { return Path.Combine(DossierApp, "GamingTweaker.exe"); } }
        /// <summary>Emplacement de la copie des versions précédentes (racine de ProgramData), retirée par Install et Remove.</summary>
        private static string ExeCopieAncienne { get { return Path.Combine(DossierCopie, "GamingTweaker.exe"); } }
        public static string DossierProfil { get { return Path.Combine(DossierCopie, "Routine"); } }

        public const string MessageAutreCompte = "La routine n'a pas été planifiée : GamingTweaker tourne sous un autre compte que celui du bureau, la tâche serait enregistrée pour ce compte. Planifiez depuis la session de l'utilisateur, avec un compte administrateur.";
        public const string MessagePourcent = "La routine n'a pas été planifiée : le nom du profil contient « % », que le Planificateur de tâches remplacerait par une variable d'environnement. Renommez le profil.";

        /// <summary>Le Planificateur développe %VAR% dans les arguments : un nom de profil avec « % » est refusé.</summary>
        public static bool NomProfilAccepte(string nomOuChemin)
        {
            return !string.IsNullOrEmpty(nomOuChemin) && nomOuChemin.IndexOf('%') < 0;
        }

        /// <summary>Résultat de « schtasks /Delete » : succès, ou tâche introuvable (déjà absente). Un refus (droits…) n'est pas un succès.</summary>
        public static bool SuppressionReussie(int code, string sortie)
        {
            if (code == 0) return true;
            var t = (sortie ?? "").ToLowerInvariant();
            return t.Contains("introuvable") || t.Contains("cannot find") || t.Contains("n'existe pas") || t.Contains("does not exist");
        }

        private static string E(string s) { return SecurityElement.Escape(s ?? ""); }

        public static string BuildTaskXml(RoutineConfig c, string exe, string profil, string user)
        {
            var debut = DateTime.Today.Add(c.Heure).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
            sb.AppendLine("<Task version=\"1.2\" xmlns=\"" + Ns + "\">");
            sb.AppendLine("  <RegistrationInfo>");
            sb.AppendLine("    <Author>GamingTweaker</Author>");
            sb.AppendLine("    <Description>Applique chaque semaine le profil GamingTweaker choisi.</Description>");
            sb.AppendLine("  </RegistrationInfo>");
            sb.AppendLine("  <Triggers>");
            sb.AppendLine("    <CalendarTrigger>");
            sb.AppendLine("      <StartBoundary>" + debut + "</StartBoundary>");
            sb.AppendLine("      <Enabled>true</Enabled>");
            sb.AppendLine("      <ScheduleByWeek>");
            sb.AppendLine("        <DaysOfWeek><" + c.Jour + " /></DaysOfWeek>");
            sb.AppendLine("        <WeeksInterval>1</WeeksInterval>");
            sb.AppendLine("      </ScheduleByWeek>");
            sb.AppendLine("    </CalendarTrigger>");
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
            sb.AppendLine("    <StartWhenAvailable>true</StartWhenAvailable>");
            sb.AppendLine("    <AllowStartOnDemand>true</AllowStartOnDemand>");
            sb.AppendLine("    <Enabled>true</Enabled>");
            sb.AppendLine("    <Hidden>false</Hidden>");
            sb.AppendLine("    <ExecutionTimeLimit>PT2H</ExecutionTimeLimit>");
            sb.AppendLine("  </Settings>");
            sb.AppendLine("  <Actions Context=\"Author\">");
            sb.AppendLine("    <Exec>");
            sb.AppendLine("      <Command>" + E("\"" + exe + "\"") + "</Command>");
            sb.AppendLine("      <Arguments>" + E("--profile \"" + profil + "\" --silent") + "</Arguments>");
            sb.AppendLine("    </Exec>");
            sb.AppendLine("  </Actions>");
            sb.Append("</Task>");
            return sb.ToString();
        }

        /// <summary>Lit jour, heure et profil dans le XML d'une tâche ; ne lève jamais.</summary>
        public static RoutineEtat ParseTaskXml(string xml)
        {
            try
            {
                var doc = XDocument.Parse((xml ?? "").Trim());
                XNamespace ns = Ns;
                var e = new RoutineEtat { Existe = true };
                var jour = doc.Descendants(ns + "DaysOfWeek").Elements().Select(x => x.Name.LocalName).FirstOrDefault();
                DayOfWeek j;
                if (jour != null && Enum.TryParse(jour, out j)) e.Jour = j;
                var sb = doc.Descendants(ns + "StartBoundary").Select(x => x.Value).FirstOrDefault();
                DateTime d;
                if (sb != null && DateTime.TryParse(sb, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) e.Heure = d.TimeOfDay;
                var args = doc.Descendants(ns + "Arguments").Select(x => x.Value).FirstOrDefault() ?? "";
                var m = Regex.Match(args, "--profile\\s+\"([^\"]+)\"");
                if (m.Success) e.ProfilChemin = m.Groups[1].Value;
                return e;
            }
            catch (Exception ex) { return new RoutineEtat { Existe = false, Message = "Définition de la tâche illisible : " + ex.Message }; }
        }

        // ---------- Effets système (jamais appelés par les tests) ----------

        /// <summary>Copie l'exe et le profil dans ProgramData puis crée la tâche (schtasks /Create /XML ... /F, qui remplace l'existante).</summary>
        public static void Install(RoutineConfig c)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.Profil) || !File.Exists(c.Profil)) throw new InvalidOperationException("Profil introuvable : " + (c == null ? "" : c.Profil));
            if (!NomProfilAccepte(Path.GetFileName(c.Profil))) throw new InvalidOperationException(MessagePourcent);
            var load = Profils.Load(c.Profil);
            if (load.Erreur != null) throw new InvalidOperationException(load.Erreur);
            // La tâche est enregistrée pour le compte courant : élevé par un autre administrateur, ce ne serait pas l'utilisateur du bureau.
            if (!ElevationCheck.SameUserAsDesktop()) throw new InvalidOperationException(MessageAutreCompte);

            // Dossier sécurisé (propriétaire Administrateurs, DACL protégée, contenu non fiable supprimé) avant d'y copier l'exe lancé élevé.
            var avert = DossierProtege.Securiser(DossierCopie);
            DossierProtege.Journaliser(DossierCopie, avert);

            // App\ est vidé et recréé : un .config (appDomainManager), une DLL de détournement ou un ancien exe pré-déposés ne doivent jamais
            // survivre à côté de l'exe lancé élevé par la tâche. Un échec interrompt l'installation.
            try
            {
                if (Directory.Exists(DossierApp)) Directory.Delete(DossierApp, true);
                Directory.CreateDirectory(DossierApp);
            }
            catch (Exception ex) { throw new IOException("Le dossier " + DossierApp + " n'a pas pu être vidé avant la copie de l'exe : " + ex.Message, ex); }

            var source = Process.GetCurrentProcess().MainModule.FileName;
            File.Copy(source, ExeCopie, true);
            try { if (File.Exists(ExeCopieAncienne)) File.Delete(ExeCopieAncienne); } catch (Exception) { }

            if (Directory.Exists(DossierProfil)) Directory.Delete(DossierProfil, true);
            Directory.CreateDirectory(DossierProfil);
            var copieProfil = Path.Combine(DossierProfil, Path.GetFileName(c.Profil));
            File.Copy(c.Profil, copieProfil, true);
            // L'exe et le profil fraîchement copiés reçoivent la DACL protégée explicite (plutôt que l'héritage seul).
            DossierProtege.Journaliser(DossierCopie, DossierProtege.Securiser(DossierCopie));

            string user;
            using (var id = WindowsIdentity.GetCurrent()) user = id.User != null ? id.User.Value : id.Name;
            var xml = BuildTaskXml(c, ExeCopie, copieProfil, user);

            var tmp = Path.Combine(Path.GetTempPath(), "gt-routine-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                File.WriteAllText(tmp, xml, Encoding.Unicode);
                string sortie;
                int code = Schtasks("/Create /TN \"" + NomTache + "\" /XML \"" + tmp + "\" /F", out sortie);
                if (code != 0) throw new InvalidOperationException("schtasks a refusé la tâche (code " + code + ") : " + sortie.Trim());
            }
            finally { try { File.Delete(tmp); } catch (Exception) { } }
        }

        /// <summary>Supprime la tâche, la copie de l'exe et celle du profil. Le dossier Machines (sessions, journaux, mesures) n'est jamais touché.</summary>
        public static void Remove()
        {
            // Toujours tenté (une lecture en échec ne doit pas laisser la tâche en place) ; « introuvable » vaut succès.
            string sortie;
            int code = Schtasks("/Delete /TN \"" + NomTache + "\" /F", out sortie);
            if (!SuppressionReussie(code, sortie)) throw new InvalidOperationException("schtasks n'a pas supprimé la tâche (code " + code + ") : " + sortie.Trim());
            var erreurs = new List<string>();
            try { if (File.Exists(ExeCopie)) File.Delete(ExeCopie); } catch (Exception ex) { erreurs.Add(ExeCopie + " : " + ex.Message); }
            try { if (File.Exists(ExeCopieAncienne)) File.Delete(ExeCopieAncienne); } catch (Exception ex) { erreurs.Add(ExeCopieAncienne + " : " + ex.Message); }
            try { if (Directory.Exists(DossierProfil)) Directory.Delete(DossierProfil, true); } catch (Exception ex) { erreurs.Add(DossierProfil + " : " + ex.Message); }
            if (erreurs.Count > 0) throw new IOException("La tâche est supprimée, mais des copies n'ont pas pu l'être : " + string.Join(" ; ", erreurs));
        }

        /// <summary>État de la tâche (schtasks /Query /XML, lecture seule).</summary>
        public static RoutineEtat Query()
        {
            try
            {
                string sortie;
                int code = Schtasks("/Query /TN \"" + NomTache + "\" /XML", out sortie);
                if (code != 0) return new RoutineEtat { Existe = false };
                var i = sortie.IndexOf('<');
                return ParseTaskXml(i < 0 ? sortie : sortie.Substring(i));
            }
            catch (Exception ex) { return new RoutineEtat { Existe = false, Message = "Lecture de la tâche impossible : " + ex.Message }; }
        }

        private static int Schtasks(string args, out string sortie)
        {
            var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = oem, StandardErrorEncoding = oem,
            };
            using (var p = Process.Start(psi))
            {
                var err = p.StandardError.ReadToEndAsync();
                var o = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(30000)) { try { p.Kill(); } catch (Exception) { } throw new System.TimeoutException("schtasks n'a pas répondu."); }
                sortie = p.ExitCode == 0 ? o : (err.Result + o);
                return p.ExitCode;
            }
        }
    }
}
