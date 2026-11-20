using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GamingTweaker.Core;
using GamingTweaker.Ui;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>Import / export de profil (fichier .json pour un autre PC) et vérification de nouvelle version.</summary>
    public class PartageProfilTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-partage-" + Guid.NewGuid().ToString("N"));
        private int confirmationsRisque;

        public PartageProfilTests() { Directory.CreateDirectory(tmp); }
        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        private static CatalogItem It(string id, RiskLevel r)
        {
            return new CatalogItem { Id = id, Menu = "Optimisation", Groupe = "G", Titre = "Titre " + id, Description = "D", Risque = r, Reversible = true };
        }

        /// <summary>Un PC = un dossier de profils à lui.</summary>
        private MainViewModel Pc(string nom)
        {
            var dir = Path.Combine(tmp, nom);
            var paths = new DataPaths { ProfilsDir = Path.Combine(dir, "Profils"), MachineDir = Path.Combine(dir, "M"), SessionsDir = Path.Combine(dir, "M", "S"), QuarantineDir = Path.Combine(dir, "Q") };
            return new MainViewModel(new List<CatalogItem> { It("opt.a", RiskLevel.Sur), It("opt.b", RiskLevel.Avance), It("opt.r", RiskLevel.Risque) },
                new MachineFacts { WindowsMajor = "10", Edition = "Pro" }, paths,
                its => Task.FromResult(its.ToDictionary(i => i.Id, i => DetectState.NotApplied)), null, o => { confirmationsRisque++; return true; });
        }

        private static void Cocher(MainViewModel vm, string id) { vm.Options.Single(o => o.Id == id).IsChecked = true; }
        private static string[] Coches(MainViewModel vm) { return vm.Options.Where(o => o.IsChecked).Select(o => o.Id).OrderBy(x => x).ToArray(); }

        [Fact]
        public void Exporter_puis_importer_sur_un_autre_pc_recoche_les_memes_options()
        {
            var pc1 = Pc("pc1");
            Cocher(pc1, "opt.a"); Cocher(pc1, "opt.b");
            var fichier = Path.Combine(tmp, "Mon jeu.json");
            Assert.Null(pc1.ExporterProfil(fichier));
            Assert.True(File.Exists(fichier));

            var pc2 = Pc("pc2");
            Assert.Null(pc2.ImporterProfil(fichier, n => true));
            Assert.Contains("Mon jeu", pc2.ProfilsDisponibles);
            Assert.Equal("Mon jeu", pc2.ProfilSelectionne);
            Assert.Equal(new[] { "opt.a", "opt.b" }, Coches(pc2));
            Assert.Contains("importé", pc2.Message);
        }

        [Fact]
        public void Exporter_sans_rien_cocher_est_refuse()
        {
            var pc = Pc("pc");
            var fichier = Path.Combine(tmp, "vide.json");
            Assert.NotNull(pc.ExporterProfil(fichier));
            Assert.False(File.Exists(fichier));
        }

        [Fact]
        public void Importer_un_fichier_invalide_renvoie_une_erreur_sans_rien_creer()
        {
            var fichier = Path.Combine(tmp, "faux.json");
            File.WriteAllText(fichier, "{ \"version\": 1, \"nom\": \"x\", \"options\": [\"$(calc)\"] }");
            var pc = Pc("pc");
            var err = pc.ImporterProfil(fichier, n => true);
            Assert.NotNull(err);
            Assert.False(Directory.Exists(Path.Combine(tmp, "pc", "Profils")) && Directory.GetFiles(Path.Combine(tmp, "pc", "Profils")).Length > 0);
        }

        [Fact]
        public void Importer_par_dessus_un_profil_existant_demande_confirmation()
        {
            var pc1 = Pc("pc1");
            Cocher(pc1, "opt.a");
            var fichier = Path.Combine(tmp, "P.json");
            Assert.Null(pc1.ExporterProfil(fichier));

            var pc2 = Pc("pc2");
            Cocher(pc2, "opt.b");
            Assert.Null(pc2.SauverProfil("P"));
            string demande = null;
            Assert.Null(pc2.ImporterProfil(fichier, n => { demande = n; return false; }));
            Assert.Equal("P", demande);
            // Refusé : le profil existant n'a pas changé.
            pc2.RechargerProfil();
            Assert.Equal(new[] { "opt.b" }, Coches(pc2));

            Assert.Null(pc2.ImporterProfil(fichier, n => true));
            Assert.Equal(new[] { "opt.a" }, Coches(pc2));
        }

        [Fact]
        public void Un_profil_importe_ne_peut_pas_prendre_le_nom_d_un_profil_integre()
        {
            // Nom affiché d'un profil intégré (« … (intégré) ») : c'est lui qui est réservé.
            var integre = ProfilsIntegres.Tous.First().NomAffiche;
            var fichier = Path.Combine(tmp, "x.json");
            Profils.Save(new Profil { Nom = integre, Options = new List<string> { "opt.a" } }, fichier);
            var pc = Pc("pc");
            Assert.Null(pc.ImporterProfil(fichier, n => true));
            Assert.Contains(integre + " (importé)", pc.ProfilsDisponibles);
        }

        [Fact]
        public void Une_option_risquee_importee_demande_la_meme_confirmation_qu_au_chargement()
        {
            var fichier = Path.Combine(tmp, "r.json");
            Profils.Save(new Profil { Nom = "R", Options = new List<string> { "opt.r" } }, fichier);
            var pc = Pc("pc");
            Assert.Null(pc.ImporterProfil(fichier, n => true));
            Assert.Equal(1, confirmationsRisque);
        }

        [Theory]
        [InlineData("  Mon profil.  ", "Mon profil")]
        [InlineData("a/b\\c:d", "abcd")]
        [InlineData("...", "")]
        public void Nom_de_profil_nettoye(string brut, string attendu) { Assert.Equal(attendu, MainViewModel.NettoyerNom(brut)); }
    }

    public class MiseAJourTests
    {
        private static readonly Version Courante = new Version(0, 9, 1);
        private const string Page = "https://github.com/alexandre-dfr/GTweak/releases/tag/";

        private static string Json(string tag, bool pre = false, bool draft = false, string url = null)
        {
            return "{ \"tag_name\": \"" + tag + "\", \"prerelease\": " + (pre ? "true" : "false") + ", \"draft\": " + (draft ? "true" : "false")
                 + ", \"html_url\": \"" + (url ?? Page + tag) + "\" }";
        }

        private const string Sha = "7119d07cc57891f550a7f5774cb4e4898107b9e2b0f5cee8af51a2633062246d";
        private static string AvecExe(string tag, string lien = null, string digest = "sha256:" + Sha, string nom = "GamingTweaker.exe")
        {
            var j = Json(tag);
            return j.Substring(0, j.Length - 1) + ", \"assets\": [ { \"name\": \"" + nom + "\", \"browser_download_url\": \""
                 + (lien ?? "https://github.com/alexandre-dfr/GTweak/releases/download/" + tag + "/GamingTweaker.exe") + "\""
                 + (digest == null ? "" : ", \"digest\": \"" + digest + "\"") + " } ] }";
        }

        [Fact]
        public void Exe_de_la_release_et_son_empreinte_permettent_la_mise_a_jour_en_un_clic()
        {
            var v = MiseAJour.Analyser(AvecExe("v0.10.0"), Courante);
            Assert.True(v.InstallableEnUnClic);
            Assert.Equal("https://github.com/alexandre-dfr/GTweak/releases/download/v0.10.0/GamingTweaker.exe", v.UrlExe);
            Assert.Equal(Sha, v.Sha256);
            Assert.False(MiseAJour.Analyser(Json("v0.10.0"), Courante).InstallableEnUnClic);   // pas d'exe joint : page seulement
        }

        [Theory]
        [InlineData("https://example.com/GamingTweaker.exe", "sha256:" + Sha, "GamingTweaker.exe")]
        [InlineData("http://github.com/alexandre-dfr/GTweak/releases/download/v0.10.0/GamingTweaker.exe", "sha256:" + Sha, "GamingTweaker.exe")]
        [InlineData(null, null, "GamingTweaker.exe")]
        [InlineData(null, "md5:abc", "GamingTweaker.exe")]
        [InlineData(null, "sha256:" + Sha, "Autre.exe")]
        public void Exe_douteux_jamais_installe_en_un_clic(string lien, string digest, string nom)
        {
            var v = MiseAJour.Analyser(AvecExe("v0.10.0", lien, digest, nom), Courante);
            Assert.NotNull(v);
            Assert.False(v.InstallableEnUnClic);
        }

        [Fact]
        public void Contenu_telecharge_verifie_par_empreinte()
        {
            var exe = new byte[4096]; exe[0] = (byte)'M'; exe[1] = (byte)'Z'; exe[100] = 42;
            var sha = MiseAJourInstallation.Sha256(exe);
            Assert.Null(MiseAJourInstallation.VerifierContenu(exe, sha));
            Assert.Null(MiseAJourInstallation.VerifierContenu(exe, sha.ToUpperInvariant()));
            Assert.Contains("SHA-256", MiseAJourInstallation.VerifierContenu(exe, Sha));
            var pasExe = (byte[])exe.Clone(); pasExe[0] = 0;
            Assert.Contains("programme Windows", MiseAJourInstallation.VerifierContenu(pasExe, MiseAJourInstallation.Sha256(pasExe)));
            Assert.NotNull(MiseAJourInstallation.VerifierContenu(new byte[10], null));
        }

        [Theory]
        [InlineData(null, null, true)]
        [InlineData(null, "CN=Moi", true)]
        [InlineData("CN=Moi", "CN=Moi", true)]
        [InlineData("CN=Moi", "cn=moi", true)]
        [InlineData("CN=Moi", null, false)]
        [InlineData("CN=Moi", "CN=Autre", false)]
        public void Exe_signe_remplace_seulement_par_le_meme_editeur(string actuel, string nouveau, bool accepte)
        {
            Assert.Equal(accepte, MiseAJourInstallation.DecisionSignature(actuel, nouveau) == null);
        }

        [Fact]
        public void Remplacement_garde_l_ancien_exe_a_cote()
        {
            var dir = Path.Combine(Path.GetTempPath(), "gt-maj-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var exe = Path.Combine(dir, "GamingTweaker.exe");
                File.WriteAllText(exe, "ancien");
                File.WriteAllText(exe + MiseAJourInstallation.SuffixeAncien, "plus ancien");
                MiseAJourInstallation.Remplacer(exe, System.Text.Encoding.ASCII.GetBytes("nouveau"));
                Assert.Equal("nouveau", File.ReadAllText(exe));
                Assert.Equal("ancien", File.ReadAllText(exe + MiseAJourInstallation.SuffixeAncien));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public async Task Mise_a_jour_refusee_si_l_empreinte_ne_correspond_pas()
        {
            var v = new VersionDisponible { Version = new Version(9, 0, 0), Url = Page + "v9.0.0", UrlExe = "https://github.com/alexandre-dfr/GTweak/releases/download/v9.0.0/GamingTweaker.exe", Sha256 = Sha };
            var exe = new byte[4096]; exe[0] = (byte)'M'; exe[1] = (byte)'Z';
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => MiseAJourInstallation.InstallerAsync(v, u => Task.FromResult(exe)));
            Assert.Contains("SHA-256", ex.Message);
            await Assert.ThrowsAsync<InvalidOperationException>(() => MiseAJourInstallation.InstallerAsync(new VersionDisponible { Version = v.Version, Url = v.Url }));
        }

        [Fact]
        public void Version_plus_recente_est_proposee()
        {
            var v = MiseAJour.Analyser(Json("v0.10.0"), Courante);
            Assert.NotNull(v);
            Assert.Equal(new Version(0, 10, 0), v.Version);
            Assert.Equal(Page + "v0.10.0", v.Url);
        }

        [Theory]
        [InlineData("v0.9.1")]
        [InlineData("v0.9.0")]
        [InlineData("test-build")]
        [InlineData("v1.0")]
        [InlineData("")]
        public void Meme_version_plus_ancienne_ou_tag_inconnu_ne_propose_rien(string tag) { Assert.Null(MiseAJour.Analyser(Json(tag), Courante)); }

        [Fact] public void Pre_version_ignoree() { Assert.Null(MiseAJour.Analyser(Json("v9.0.0", pre: true), Courante)); }
        [Fact] public void Brouillon_ignore() { Assert.Null(MiseAJour.Analyser(Json("v9.0.0", draft: true), Courante)); }

        [Theory]
        [InlineData("https://evil.example/releases/v9.0.0")]
        [InlineData("http://github.com/alexandre-dfr/GTweak/releases/tag/v9.0.0")]
        [InlineData("https://github.com/autre/depot/releases/tag/v9.0.0")]
        public void Page_hors_des_releases_du_depot_refusee(string url) { Assert.Null(MiseAJour.Analyser(Json("v9.0.0", url: url), Courante)); }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("pas du json")]
        [InlineData("{ \"message\": \"API rate limit exceeded\" }")]
        public void Reponse_inattendue_ne_propose_rien(string json) { Assert.Null(MiseAJour.Analyser(json, Courante)); }

        [Fact]
        public async Task Erreur_reseau_silencieuse()
        {
            Assert.Null(await MiseAJour.VerifierAsync(u => { throw new System.Net.Http.HttpRequestException("hors ligne"); }));
        }

        [Fact]
        public async Task Interroge_l_api_des_releases_du_depot()
        {
            string demande = null;
            await MiseAJour.VerifierAsync(u => { demande = u; return Task.FromResult<string>(null); });
            Assert.Equal("https://api.github.com/repos/alexandre-dfr/GTweak/releases/latest", demande);
        }

        [Fact]
        public void Bandeau_de_la_vue_modele()
        {
            var vm = new MainViewModel(new List<CatalogItem>(), new MachineFacts(), new DataPaths(), its => Task.FromResult(new Dictionary<string, DetectState>()), null, o => true);
            Assert.False(vm.NouvelleVersionVisible);
            vm.NouvelleVersion = new VersionDisponible { Version = new Version(9, 0, 0), Url = Page + "v9.0.0" };
            Assert.True(vm.NouvelleVersionVisible);
            Assert.Contains("9.0.0", vm.TexteNouvelleVersion);
            vm.NouvelleVersionMasquee = true;
            Assert.False(vm.NouvelleVersionVisible);
        }
    }
}
