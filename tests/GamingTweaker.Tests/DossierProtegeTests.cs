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

        private static EntreeProtection E(bool racine = false, bool dossier = true, bool lien = false, int liens = 1, string proprio = Admins, bool daclSure = true, bool volumeInattendu = false)
            => new EntreeProtection { EstRacine = racine, EstDossier = dossier, EstLien = lien, NombreLiens = liens, ProprietaireSid = proprio, DaclSure = daclSure, EstVolumeInattendu = volumeInattendu };

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
        public void Racine_de_confiance_est_reappliquee()
        {
            var d = DossierProtege.Decider(E(racine: true), true, @"C:\x");
            Assert.Equal(ActionProtection.Appliquer, d.Action);
            Assert.Null(d.Avertissement);
        }

        [Fact]
        public void Proprietaire_non_fiable_est_supprime_en_mode_eleve()
        {
            // Élevé : le propriétaire peut être fixé, donc un élément non fiable (y compris la racine) est supprimé, pas repris.
            foreach (var racine in new[] { true, false })
            {
                var d = DossierProtege.Decider(E(racine: racine, proprio: Utilisateur), true, @"C:\x\Machines");
                Assert.Equal(ActionProtection.Supprimer, d.Action);
                Assert.Contains(Utilisateur, d.Avertissement);
                Assert.Contains(@"C:\x\Machines", d.Avertissement);
            }
        }

        [Fact]
        public void Hors_elevation_le_proprietaire_n_est_pas_applique()
        {
            // Sans pouvoir prendre possession, on ne supprime pas sur le seul critère du propriétaire : on applique la DACL.
            Assert.Equal(ActionProtection.Appliquer, DossierProtege.Decider(E(proprio: Utilisateur, daclSure: false), false, "d").Action);
            Assert.Equal(ActionProtection.Aucune, DossierProtege.Decider(E(proprio: Utilisateur, daclSure: true), false, "d").Action);
            // Un lien reste supprimé, élevé ou non.
            Assert.Equal(ActionProtection.Supprimer, DossierProtege.Decider(E(lien: true, proprio: Utilisateur), false, "j").Action);
        }

        [Fact]
        public void Lien_physique_volume_etranger_sont_supprimes_sans_etre_suivis()
        {
            Assert.Equal(ActionProtection.Supprimer, DossierProtege.Decider(E(lien: true), true, "j").Action);
            Assert.Equal(ActionProtection.Supprimer, DossierProtege.Decider(E(racine: true, lien: true), true, "j").Action);
            Assert.Equal(ActionProtection.Supprimer, DossierProtege.Decider(E(dossier: false, liens: 2), true, "f").Action);
            Assert.NotNull(DossierProtege.Decider(E(dossier: false, liens: 2), true, "f").Avertissement);
            var v = DossierProtege.Decider(E(volumeInattendu: true), true, "m");
            Assert.Equal(ActionProtection.Supprimer, v.Action);
            Assert.Contains("volume", v.Avertissement);
        }

        [Fact]
        public void Descendant_sain_est_laisse_tel_quel_sinon_normalise()
        {
            Assert.Equal(ActionProtection.Aucune, DossierProtege.Decider(E(), true, "d").Action);
            Assert.Equal(ActionProtection.Aucune, DossierProtege.Decider(E(dossier: false, proprio: Systeme), true, "f").Action);
            Assert.Equal(ActionProtection.Appliquer, DossierProtege.Decider(E(daclSure: false), true, "d").Action);
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
                // Non élevé : le propriétaire ne peut pas être fixé à BUILTIN\Administrators (privilège requis), et la règle de propriétaire
                // n'est pas appliquée (sinon tout le contenu créé par l'utilisateur de test serait supprimé). Seules la DACL et le retrait des liens sont vérifiés.
                sortie.WriteLine("Vérification du propriétaire et de la suppression par propriétaire ignorée : le lanceur de tests n'est pas élevé (SeTakeOwnership/SeRestore indisponibles).");
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

        [Fact]
        public void Securiser_supprime_un_element_plante_au_proprietaire_non_fiable()
        {
            var racine = Path.Combine(tmp, "Plante");
            Directory.CreateDirectory(Path.Combine(racine, "App"));
            // Fichier simple planté (pas un lien) à côté de l'exe, par exemple un .config appDomainManager ou une DLL de détournement.
            var plante = Path.Combine(racine, "App", "GamingTweaker.exe.config");
            File.WriteAllText(plante, "<configuration/>");

            if (!EstEleve())
            {
                sortie.WriteLine("Test de suppression réelle par propriétaire ignoré : non élevé, impossible de fixer le propriétaire à un compte non fiable (SeRestore requis). La décision est couverte par Proprietaire_non_fiable_est_supprime_en_mode_eleve.");
                return;
            }
            try
            {
                var sec = new FileInfo(plante).GetAccessControl();
                sec.SetOwner(new SecurityIdentifier(Users));
                new FileInfo(plante).SetAccessControl(sec);
            }
            catch (Exception ex) { sortie.WriteLine("Impossible de fixer le propriétaire non fiable, test ignoré : " + ex.Message); return; }

            var avert = DossierProtege.Securiser(racine, definirProprietaire: true);
            Assert.False(File.Exists(plante), "le fichier planté au propriétaire non fiable aurait dû être supprimé");
            Assert.Contains(avert, a => a.Contains("propriétaire non fiable") && a.Contains("GamingTweaker.exe.config"));
            VerifierDacl(new DirectoryInfo(Path.Combine(racine, "App")).GetAccessControl(), Path.Combine(racine, "App"));
        }

        [Fact]
        public void Securiser_refuse_si_un_element_est_ouvert_en_ecriture_par_un_autre()
        {
            var racine = Path.Combine(tmp, "Ouvert");
            Directory.CreateDirectory(racine);
            var f = Path.Combine(racine, "occupe.bin");
            File.WriteAllText(f, "x");
            // Poignée concurrente avec accès en écriture : l'ouverture en partage lecture seule de Securiser doit être refusée (pas de FILE_SHARE_WRITE).
            using (new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                var ex = Assert.ThrowsAny<Exception>(() => DossierProtege.Securiser(racine, definirProprietaire: EstEleve()));
                Assert.Contains("partage", ex.Message);
                var r = DossierProtege.SecuriserAuDemarrage(racine, () => true, definirProprietaire: EstEleve());
                Assert.NotNull(r.Erreur);
            }
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
