using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using GamingTweaker.Core;
using Xunit;
using Xunit.Abstractions;

namespace GamingTweaker.Tests
{
    /// <summary>
    /// Sécurisation du dossier de données élevé. Logique de décision pure, puis application réelle sur un dossier sous %TEMP%
    /// (jamais sur C:\ProgramData\GamingTweaker).
    /// </summary>
    public class DossierProtegeTests : IDisposable
    {
        private const string Systeme = "S-1-5-18", Admins = "S-1-5-32-544", Users = "S-1-5-32-545", Utilisateur = "S-1-5-21-1-2-3-1001";
        private readonly ITestOutputHelper sortie;
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-protege-" + Guid.NewGuid().ToString("N"));

        public DossierProtegeTests(ITestOutputHelper sortie) { this.sortie = sortie; }

        // ---------- Décision pure ----------

        private static EntreeProtection E(bool racine = false, bool dossier = true, bool lien = false, int liens = 1, string proprio = Admins, bool daclSure = true)
            => new EntreeProtection { EstRacine = racine, EstDossier = dossier, EstLien = lien, NombreLiens = liens, ProprietaireSid = proprio, DaclSure = daclSure };

        [Fact]
        public void Proprietaires_de_confiance_sont_system_et_administrateurs()
        {
            Assert.True(DossierProtege.ProprietaireDeConfiance(Systeme));
            Assert.True(DossierProtege.ProprietaireDeConfiance(Admins));
            Assert.False(DossierProtege.ProprietaireDeConfiance(Users));
            Assert.False(DossierProtege.ProprietaireDeConfiance(Utilisateur));
            Assert.False(DossierProtege.ProprietaireDeConfiance(null));
        }

        [Fact]
        public void Racine_est_toujours_reappliquee()
        {
            var d = DossierProtege.Decider(E(racine: true), @"C:\x");
            Assert.Equal(ActionProtection.Appliquer, d.Action);
            Assert.Null(d.Avertissement);
        }

        [Fact]
        public void Proprietaire_non_fiable_est_repris_avec_avertissement()
        {
            foreach (var racine in new[] { true, false })
            {
                var d = DossierProtege.Decider(E(racine: racine, proprio: Utilisateur), @"C:\x\Machines");
                Assert.Equal(ActionProtection.Appliquer, d.Action);
                Assert.Contains(Utilisateur, d.Avertissement);
                Assert.Contains(@"C:\x\Machines", d.Avertissement);
            }
        }

        [Fact]
        public void Lien_jonction_ou_lien_physique_est_supprime_sans_etre_suivi()
        {
            Assert.Equal(ActionProtection.Supprimer, DossierProtege.Decider(E(lien: true), "j").Action);
            Assert.Equal(ActionProtection.Supprimer, DossierProtege.Decider(E(racine: true, lien: true), "j").Action);
            Assert.Equal(ActionProtection.Supprimer, DossierProtege.Decider(E(dossier: false, liens: 2), "f").Action);
            Assert.NotNull(DossierProtege.Decider(E(dossier: false, liens: 2), "f").Avertissement);
        }

        [Fact]
        public void Descendant_sain_est_laisse_tel_quel_sinon_normalise()
        {
            Assert.Equal(ActionProtection.Aucune, DossierProtege.Decider(E(), "d").Action);
            Assert.Equal(ActionProtection.Aucune, DossierProtege.Decider(E(dossier: false, proprio: Systeme), "f").Action);
            Assert.Equal(ActionProtection.Appliquer, DossierProtege.Decider(E(daclSure: false), "d").Action);
        }

        private static AceInfo Ace(string sid, FileSystemRights r, bool autorise = true) => new AceInfo { Sid = sid, Masque = (int)r, Autorise = autorise };

        [Fact]
        public void Dacl_sure_n_accorde_l_ecriture_qu_a_system_et_aux_administrateurs()
        {
            Assert.True(DossierProtege.DaclSure(new[] { Ace(Systeme, FileSystemRights.FullControl), Ace(Admins, FileSystemRights.FullControl), Ace(Users, FileSystemRights.ReadAndExecute) }));
            Assert.True(DossierProtege.DaclSure(new[] { Ace(Admins, FileSystemRights.FullControl), Ace(Users, FileSystemRights.Write, autorise: false) }));
            Assert.False(DossierProtege.DaclSure(new[] { Ace(Users, FileSystemRights.CreateFiles) }));
            Assert.False(DossierProtege.DaclSure(new[] { Ace(Users, FileSystemRights.AppendData) }));
            Assert.False(DossierProtege.DaclSure(new[] { Ace(Utilisateur, FileSystemRights.Modify) }));
            Assert.False(DossierProtege.DaclSure(new[] { new AceInfo { Sid = "S-1-3-0", Masque = 0x10000000, Autorise = true } })); // CREATOR OWNER GENERIC_ALL
            Assert.False(DossierProtege.DaclSure(null)); // DACL nulle : tout le monde a tous les droits
        }

        [Fact]
        public void Dacl_attendue_system_admins_controle_total_users_lecture()
        {
            var acl = DossierProtege.DaclAttendue(true);
            var aces = acl.Cast<CommonAce>().ToList();
            Assert.Equal(3, aces.Count);
            Assert.All(aces, a => Assert.Equal(AceQualifier.AccessAllowed, a.AceQualifier));
            Assert.All(aces, a => Assert.Equal(AceFlags.ContainerInherit | AceFlags.ObjectInherit, a.AceFlags));
            Assert.Equal((int)FileSystemRights.FullControl, aces.Single(a => a.SecurityIdentifier.Value == Systeme).AccessMask);
            Assert.Equal((int)FileSystemRights.FullControl, aces.Single(a => a.SecurityIdentifier.Value == Admins).AccessMask);
            Assert.Equal((int)(FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize), aces.Single(a => a.SecurityIdentifier.Value == Users).AccessMask);
            Assert.All(DossierProtege.DaclAttendue(false).Cast<CommonAce>(), a => Assert.Equal(AceFlags.None, a.AceFlags));
        }

        // ---------- Application réelle sous %TEMP% ----------

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string lien, string cible, IntPtr sec);

        private static bool EstEleve()
        {
            using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static void Jonction(string lien, string cible)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + lien + "\" \"" + cible + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); Assert.Equal(0, p.ExitCode); }
        }

        private static void VerifierDacl(FileSystemSecurity sec, string chemin)
        {
            Assert.True(sec.AreAccessRulesProtected, "héritage non coupé : " + chemin);
            var regles = sec.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
            Assert.All(regles, r => Assert.False(r.IsInherited, "règle héritée sur " + chemin));
            Assert.All(regles, r => Assert.Equal(AccessControlType.Allow, r.AccessControlType));
            Assert.Equal(new[] { Systeme, Admins, Users }.OrderBy(x => x), regles.Select(r => r.IdentityReference.Value).OrderBy(x => x));
            Assert.Equal(FileSystemRights.FullControl, regles.Single(r => r.IdentityReference.Value == Systeme).FileSystemRights);
            Assert.Equal(FileSystemRights.FullControl, regles.Single(r => r.IdentityReference.Value == Admins).FileSystemRights);
            Assert.Equal(FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize, regles.Single(r => r.IdentityReference.Value == Users).FileSystemRights);
        }

        [Fact]
        public void Securiser_applique_la_dacl_protegee_recursivement_et_retire_liens_et_jonctions()
        {
            var racine = Path.Combine(tmp, "GamingTweaker");
            var dehors = Path.Combine(tmp, "Dehors");
            Directory.CreateDirectory(Path.Combine(racine, "Machines", "PC-1", "Sessions"));
            Directory.CreateDirectory(Path.Combine(racine, "App"));
            Directory.CreateDirectory(dehors);
            File.WriteAllText(Path.Combine(racine, "App", "GamingTweaker.exe"), "exe");
            File.WriteAllText(Path.Combine(racine, "Machines", "PC-1", "last-applied.json"), "[]");
            var cibleDehors = Path.Combine(dehors, "cible.txt"); File.WriteAllText(cibleDehors, "dehors");
            Jonction(Path.Combine(racine, "Machines", "piege"), dehors);
            Assert.True(CreateHardLink(Path.Combine(racine, "App", "version.dll"), cibleDehors, IntPtr.Zero));
            var avantDehors = new DirectoryInfo(dehors).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            var avantCible = new FileInfo(cibleDehors).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);

            bool eleve = EstEleve();
            var avert = DossierProtege.Securiser(racine, definirProprietaire: eleve);

            // Liens retirés, cibles intactes (ni suivies ni modifiées).
            Assert.False(Directory.Exists(Path.Combine(racine, "Machines", "piege")));
            Assert.False(File.Exists(Path.Combine(racine, "App", "version.dll")));
            Assert.True(File.Exists(cibleDehors));
            Assert.Equal(avantDehors, new DirectoryInfo(dehors).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));
            Assert.Equal(avantCible, new FileInfo(cibleDehors).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));
            Assert.Contains(avert, a => a.Contains("piege"));
            Assert.Contains(avert, a => a.Contains("version.dll"));

            var dossiers = new[] { racine, Path.Combine(racine, "App"), Path.Combine(racine, "Machines"), Path.Combine(racine, "Machines", "PC-1"), Path.Combine(racine, "Machines", "PC-1", "Sessions") };
            foreach (var d in dossiers) VerifierDacl(new DirectoryInfo(d).GetAccessControl(), d);
            foreach (var f in new[] { Path.Combine(racine, "App", "GamingTweaker.exe"), Path.Combine(racine, "Machines", "PC-1", "last-applied.json") })
                VerifierDacl(new FileInfo(f).GetAccessControl(), f);

            if (eleve)
            {
                foreach (var d in dossiers)
                    Assert.Equal(Admins, new DirectoryInfo(d).GetAccessControl().GetOwner(typeof(SecurityIdentifier)).Value);
                Assert.Equal(Admins, new FileInfo(Path.Combine(racine, "App", "GamingTweaker.exe")).GetAccessControl().GetOwner(typeof(SecurityIdentifier)).Value);
            }
            else
            {
                // Non élevé : le propriétaire ne peut pas être fixé à BUILTIN\Administrators (privilège requis).
                // La reprise du propriétaire est couverte par la décision pure ; ici seule la DACL est vérifiée.
                sortie.WriteLine("Vérification du propriétaire ignorée : le lanceur de tests n'est pas élevé (SeTakeOwnership/SeRestore indisponibles).");
                Assert.Contains(avert, a => a.Contains("Propriétaire"));
            }

            // Idempotent : une seconde passe ne signale plus de lien.
            var avert2 = DossierProtege.Securiser(racine, definirProprietaire: eleve);
            Assert.DoesNotContain(avert2, a => a.Contains("piege") || a.Contains("version.dll"));
        }

        [Fact]
        public void Securiser_cree_le_dossier_absent()
        {
            var racine = Path.Combine(tmp, "Neuf");
            DossierProtege.Securiser(racine, definirProprietaire: EstEleve());
            Assert.True(Directory.Exists(racine));
            VerifierDacl(new DirectoryInfo(racine).GetAccessControl(), racine);
        }

        [Fact]
        public void Racine_jonction_est_remplacee_par_un_vrai_dossier()
        {
            var cible = Path.Combine(tmp, "Cible"); Directory.CreateDirectory(cible);
            File.WriteAllText(Path.Combine(cible, "garder.txt"), "x");
            var racine = Path.Combine(tmp, "Racine");
            Jonction(racine, cible);
            var avert = DossierProtege.Securiser(racine, definirProprietaire: EstEleve());
            Assert.False(new DirectoryInfo(racine).Attributes.HasFlag(FileAttributes.ReparsePoint));
            Assert.True(File.Exists(Path.Combine(cible, "garder.txt")));
            Assert.False(new DirectoryInfo(cible).GetAccessControl().AreAccessRulesProtected);
            Assert.NotEmpty(avert);
        }

        [Fact]
        public void Au_demarrage_non_eleve_rien_n_est_fait()
        {
            var racine = Path.Combine(tmp, "NonEleve");
            var r = DossierProtege.SecuriserAuDemarrage(racine, () => false);
            Assert.Null(r.Erreur);
            Assert.False(Directory.Exists(racine));
        }

        [Fact]
        public void Au_demarrage_les_avertissements_sont_journalises_dans_le_dossier()
        {
            var racine = Path.Combine(tmp, "Journal");
            Directory.CreateDirectory(racine);
            var cible = Path.Combine(tmp, "C2"); Directory.CreateDirectory(cible);
            Jonction(Path.Combine(racine, "lien"), cible);
            var r = DossierProtege.SecuriserAuDemarrage(racine, () => true, definirProprietaire: EstEleve());
            Assert.Null(r.Erreur);
            Assert.Contains(r.Avertissements, a => a.Contains("lien"));
            if (EstEleve()) Assert.Contains("lien", File.ReadAllText(Path.Combine(racine, DossierProtege.NomJournal)));
            else sortie.WriteLine("Vérification de securite.log ignorée : non élevé, le dossier sécurisé n'est plus inscriptible par l'utilisateur de test (comportement voulu).");
        }

        /// <summary>Le propriétaire (utilisateur courant, ou Administrateurs si élevé) remet l'héritage par icacls pour pouvoir supprimer le dossier de test.</summary>
        public void Dispose()
        {
            if (!Directory.Exists(tmp)) return;
            try
            {
                var psi = new ProcessStartInfo("icacls.exe", "\"" + tmp + "\" /reset /T /C /Q") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.WaitForExit(30000); }
            }
            catch (Exception) { }
            try { Directory.Delete(tmp, true); } catch (Exception) { }
        }
    }
}
