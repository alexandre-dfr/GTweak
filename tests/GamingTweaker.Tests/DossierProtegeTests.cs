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
    /// Racine protégée des données élevées. Logique de décision pure, puis création et vérification réelles sous %TEMP%
    /// (jamais sous %ProgramFiles%\GamingTweaker). Un élément douteux fait échouer (refus), rien n'est repris ni supprimé.
    /// </summary>
    public class DossierProtegeTests : IDisposable
    {
        private const string Systeme = "S-1-5-18", Admins = "S-1-5-32-544", Users = "S-1-5-32-545", Utilisateur = "S-1-5-21-1-2-3-1001";
        private const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
        private readonly ITestOutputHelper sortie;
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-protege-" + Guid.NewGuid().ToString("N"));

        public DossierProtegeTests(ITestOutputHelper sortie) { this.sortie = sortie; }

        // ---------- Décision pure ----------

        private static EntreeProtection E(bool racine = false, bool dossier = true, bool lien = false, int liens = 1, string proprio = Admins, bool daclSure = true, bool volumeInattendu = false)
            => new EntreeProtection { EstRacine = racine, EstDossier = dossier, EstLien = lien, NombreLiens = liens, ProprietaireSid = proprio, DaclSure = daclSure, EstVolumeInattendu = volumeInattendu };

        [Fact]
        public void Proprietaires_de_confiance_sont_system_administrateurs_et_trustedinstaller()
        {
            Assert.True(DossierProtege.ProprietaireDeConfiance(Systeme));
            Assert.True(DossierProtege.ProprietaireDeConfiance(Admins));
            Assert.True(DossierProtege.ProprietaireDeConfiance(TrustedInstaller));
            Assert.False(DossierProtege.ProprietaireDeConfiance(Users));
            Assert.False(DossierProtege.ProprietaireDeConfiance(Utilisateur));
            Assert.False(DossierProtege.ProprietaireDeConfiance(null));
        }

        [Fact]
        public void Racine_de_confiance_est_acceptee_et_sa_dacl_reappliquee()
        {
            foreach (var p in new[] { Systeme, Admins, TrustedInstaller })
            {
                var d = DossierProtege.Decider(E(racine: true, proprio: p), true, @"C:\x");
                Assert.Equal(ActionProtection.Appliquer, d.Action);
                Assert.Null(d.Raison);
            }
        }

        [Fact]
        public void Racine_au_proprietaire_non_fiable_ou_lien_est_refusee_jamais_reprise()
        {
            var d = DossierProtege.Decider(E(racine: true, proprio: Utilisateur), true, @"C:\x");
            Assert.Equal(ActionProtection.Refuser, d.Action);
            Assert.Contains("propriétaire non fiable", d.Raison);
            Assert.Contains(Utilisateur, d.Raison);
            Assert.Equal(ActionProtection.Refuser, DossierProtege.Decider(E(racine: true, lien: true), true, @"C:\x").Action);
            Assert.Equal(ActionProtection.Refuser, DossierProtege.Decider(E(racine: true, lien: true), false, @"C:\x").Action);
            Assert.Equal(ActionProtection.Refuser, DossierProtege.Decider(E(racine: true, dossier: false), true, @"C:\x").Action);
        }

        [Fact]
        public void Contenu_douteux_est_refuse()
        {
            Assert.Equal(ActionProtection.Refuser, DossierProtege.Decider(E(lien: true), true, "j").Action);
            Assert.Equal(ActionProtection.Refuser, DossierProtege.Decider(E(dossier: false, liens: 2), true, "f").Action);
            Assert.Equal(ActionProtection.Refuser, DossierProtege.Decider(E(volumeInattendu: true), true, "m").Action);
            Assert.Equal(ActionProtection.Refuser, DossierProtege.Decider(E(dossier: false, proprio: Utilisateur), true, "App\\x.dll").Action);
        }

        [Fact]
        public void Hors_elevation_le_proprietaire_n_est_pas_verifie()
        {
            Assert.Equal(ActionProtection.Appliquer, DossierProtege.Decider(E(racine: true, proprio: Utilisateur), false, "r").Action);
            Assert.Equal(ActionProtection.Aucune, DossierProtege.Decider(E(proprio: Utilisateur), false, "d").Action);
        }

        [Fact]
        public void Descendant_sain_est_laisse_tel_quel_sinon_sa_dacl_est_reecrite()
        {
            Assert.Equal(ActionProtection.Aucune, DossierProtege.Decider(E(), true, "d").Action);
            Assert.Equal(ActionProtection.Aucune, DossierProtege.Decider(E(dossier: false, proprio: Systeme), true, "f").Action);
            Assert.Equal(ActionProtection.Appliquer, DossierProtege.Decider(E(daclSure: false), true, "d").Action);
        }

        private static AceInfo Ace(string sid, FileSystemRights r, bool autorise = true) => new AceInfo { Sid = sid, Masque = (int)r, Autorise = autorise };

        [Fact]
        public void Dacl_sure_n_accorde_l_ecriture_qu_aux_comptes_de_confiance()
        {
            Assert.True(DossierProtege.DaclSure(new[] { Ace(Systeme, FileSystemRights.FullControl), Ace(Admins, FileSystemRights.FullControl), Ace(Users, FileSystemRights.ReadAndExecute) }));
            Assert.True(DossierProtege.DaclSure(new[] { Ace(Admins, FileSystemRights.FullControl), Ace(Users, FileSystemRights.Write, autorise: false) }));
            Assert.False(DossierProtege.DaclSure(new[] { Ace(Users, FileSystemRights.CreateFiles) }));
            Assert.False(DossierProtege.DaclSure(new[] { Ace(Users, FileSystemRights.AppendData) }));
            Assert.False(DossierProtege.DaclSure(new[] { Ace(Utilisateur, FileSystemRights.Modify) }));
            Assert.False(DossierProtege.DaclSure(new[] { new AceInfo { Sid = "S-1-3-0", Masque = 0x10000000, Autorise = true } })); // CREATOR OWNER GENERIC_ALL
            Assert.False(DossierProtege.DaclSure(null));
        }

        [Fact]
        public void Dacl_attendue_system_admins_controle_total_users_lecture()
        {
            var aces = DossierProtege.DaclAttendue(true).Cast<CommonAce>().ToList();
            Assert.Equal(3, aces.Count);
            Assert.All(aces, a => Assert.Equal(AceFlags.ContainerInherit | AceFlags.ObjectInherit, a.AceFlags));
            Assert.Equal((int)FileSystemRights.FullControl, aces.Single(a => a.SecurityIdentifier.Value == Systeme).AccessMask);
            Assert.Equal((int)FileSystemRights.FullControl, aces.Single(a => a.SecurityIdentifier.Value == Admins).AccessMask);
            Assert.Equal((int)(FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize), aces.Single(a => a.SecurityIdentifier.Value == Users).AccessMask);
            Assert.All(DossierProtege.DaclAttendue(false).Cast<CommonAce>(), a => Assert.Equal(AceFlags.None, a.AceFlags));
        }

        [Fact]
        public void La_racine_protegee_est_sous_program_files_et_plus_sous_programdata()
        {
            var r = DataPaths.RacineProtegeeParDefaut;
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GamingTweaker"), r);
            Assert.DoesNotContain("ProgramData", r, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(r, Planification.ExeCopie);
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

        private string Sddl(string chemin, bool dossier)
            => (dossier ? (FileSystemSecurity)new DirectoryInfo(chemin).GetAccessControl() : new FileInfo(chemin).GetAccessControl()).GetSecurityDescriptorSddlForm(AccessControlSections.Access);

        [Fact]
        public void Dossier_absent_est_cree_de_facon_atomique_avec_la_dacl_protegee()
        {
            bool eleve = EstEleve();
            var racine = Path.Combine(tmp, "Neuf");
            Directory.CreateDirectory(tmp);
            DossierProtege.Securiser(racine, definirProprietaire: eleve);
            VerifierDacl(new DirectoryInfo(racine).GetAccessControl(), racine);
            // Création atomique d'un dossier (comme App\ à l'installation). Hors élévation, la racine protégée n'est plus inscriptible
            // par l'utilisateur de test (voulu) : le dossier est créé à côté.
            var app = eleve ? Path.Combine(racine, "App") : Path.Combine(tmp, "App");
            DossierProtege.CreerDossier(app, eleve);
            VerifierDacl(new DirectoryInfo(app).GetAccessControl(), app);
            if (eleve) Assert.Equal(Admins, new DirectoryInfo(racine).GetAccessControl().GetOwner(typeof(SecurityIdentifier)).Value);
            else sortie.WriteLine("Vérification du propriétaire ignorée : le lanceur de tests n'est pas élevé (le propriétaire Administrateurs ne peut être fixé qu'en élévation).");
        }

        [Fact]
        public void Contenu_existant_recoit_la_dacl_protegee_sans_changer_de_proprietaire()
        {
            bool eleve = EstEleve();
            var racine = Path.Combine(tmp, "GamingTweaker");
            Directory.CreateDirectory(Path.Combine(racine, "Machines", "PC-1", "Sessions"));
            Directory.CreateDirectory(Path.Combine(racine, "App"));
            File.WriteAllText(Path.Combine(racine, "App", "GamingTweaker.exe"), "exe");
            File.WriteAllText(Path.Combine(racine, "Machines", "PC-1", "last-applied.json"), "[]");
            var proprioAvant = new DirectoryInfo(racine).GetAccessControl().GetOwner(typeof(SecurityIdentifier)).Value;

            DossierProtege.Securiser(racine, definirProprietaire: eleve);

            var dossiers = new[] { racine, Path.Combine(racine, "App"), Path.Combine(racine, "Machines"), Path.Combine(racine, "Machines", "PC-1"), Path.Combine(racine, "Machines", "PC-1", "Sessions") };
            foreach (var d in dossiers) VerifierDacl(new DirectoryInfo(d).GetAccessControl(), d);
            foreach (var f in new[] { Path.Combine(racine, "App", "GamingTweaker.exe"), Path.Combine(racine, "Machines", "PC-1", "last-applied.json") })
                VerifierDacl(new FileInfo(f).GetAccessControl(), f);
            Assert.Equal(proprioAvant, new DirectoryInfo(racine).GetAccessControl().GetOwner(typeof(SecurityIdentifier)).Value); // jamais repris
            DossierProtege.Securiser(racine, definirProprietaire: eleve); // idempotent
        }

        [Fact]
        public void Racine_jonction_est_refusee_et_sa_cible_n_est_pas_touchee()
        {
            var cible = Path.Combine(tmp, "Cible"); Directory.CreateDirectory(cible);
            File.WriteAllText(Path.Combine(cible, "garder.txt"), "x");
            var avant = Sddl(cible, true);
            var racine = Path.Combine(tmp, "Racine");
            Jonction(racine, cible);
            var ex = Assert.ThrowsAny<Exception>(() => DossierProtege.Securiser(racine, definirProprietaire: EstEleve()));
            Assert.Contains("lien ou jonction", ex.Message);
            Assert.True(new DirectoryInfo(racine).Attributes.HasFlag(FileAttributes.ReparsePoint)); // ni supprimée ni remplacée
            Assert.True(File.Exists(Path.Combine(cible, "garder.txt")));
            Assert.Equal(avant, Sddl(cible, true));
        }

        [Fact]
        public void Jonction_ou_lien_physique_dans_le_contenu_est_refuse_sans_toucher_la_cible()
        {
            var dehors = Path.Combine(tmp, "Dehors"); Directory.CreateDirectory(dehors);
            var cibleDehors = Path.Combine(dehors, "cible.txt"); File.WriteAllText(cibleDehors, "dehors");
            var avantDehors = Sddl(dehors, true); var avantCible = Sddl(cibleDehors, false);

            var r1 = Path.Combine(tmp, "R1");
            Directory.CreateDirectory(Path.Combine(r1, "Machines"));
            Jonction(Path.Combine(r1, "Machines", "piege"), dehors);
            var ex1 = Assert.ThrowsAny<Exception>(() => DossierProtege.Securiser(r1, definirProprietaire: EstEleve()));
            Assert.Contains("piege", ex1.Message);

            var r2 = Path.Combine(tmp, "R2");
            Directory.CreateDirectory(Path.Combine(r2, "App"));
            Assert.True(CreateHardLink(Path.Combine(r2, "App", "version.dll"), cibleDehors, IntPtr.Zero));
            var ex2 = Assert.ThrowsAny<Exception>(() => DossierProtege.Securiser(r2, definirProprietaire: EstEleve()));
            Assert.Contains("lien physique", ex2.Message);

            Assert.Equal(avantDehors, Sddl(dehors, true));
            Assert.Equal(avantCible, Sddl(cibleDehors, false));
            Assert.True(File.Exists(Path.Combine(r2, "App", "version.dll"))); // rien n'est supprimé : refus
        }

        [Fact]
        public void Element_ouvert_en_ecriture_par_un_autre_provoque_un_refus()
        {
            var racine = Path.Combine(tmp, "Ouvert");
            Directory.CreateDirectory(racine);
            var f = Path.Combine(racine, "occupe.bin");
            File.WriteAllText(f, "x");
            using (new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                var ex = Assert.ThrowsAny<Exception>(() => DossierProtege.Securiser(racine, definirProprietaire: EstEleve()));
                Assert.Contains("partage", ex.Message);
                var r = DossierProtege.SecuriserAuDemarrage(racine, () => true, definirProprietaire: EstEleve());
                Assert.NotNull(r.Erreur);
            }
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
        public void Au_demarrage_un_dossier_non_sur_arrete_l_application()
        {
            var cible = Path.Combine(tmp, "C2"); Directory.CreateDirectory(cible);
            var racine = Path.Combine(tmp, "Lien");
            Jonction(racine, cible);
            var r = DossierProtege.SecuriserAuDemarrage(racine, () => true, definirProprietaire: EstEleve());
            Assert.NotNull(r.Erreur);
            Assert.Contains("n'est pas sûr", r.Erreur);
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
            // Suppression parfois refusée un instant (analyse antivirus, indexation) : quelques nouvelles tentatives.
            for (int i = 0; i < 10 && Directory.Exists(tmp); i++)
            {
                try { Directory.Delete(tmp, true); }
                catch (Exception) { System.Threading.Thread.Sleep(300); }
            }
        }
    }
}
