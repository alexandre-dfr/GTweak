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
        // Sans clé : la version est annoncée même sans exe signé (ces tests portent sur la lecture de la release).
        private static readonly string[] SansCle = new string[0];
        private const string Page = "https://github.com/alexandre-dfr/GTweak/releases/tag/";

        private static string Json(string tag, bool pre = false, bool draft = false, string url = null)
        {
            return "{ \"tag_name\": \"" + tag + "\", \"prerelease\": " + (pre ? "true" : "false") + ", \"draft\": " + (draft ? "true" : "false")
                 + ", \"html_url\": \"" + (url ?? Page + tag) + "\" }";
        }

        [Fact]
        public void Version_plus_recente_est_proposee()
        {
            var v = MiseAJour.Analyser(Json("v0.10.0"), Courante, SansCle);
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
        public void Meme_version_plus_ancienne_ou_tag_inconnu_ne_propose_rien(string tag) { Assert.Null(MiseAJour.Analyser(Json(tag), Courante, SansCle)); }

        [Fact] public void Pre_version_ignoree() { Assert.Null(MiseAJour.Analyser(Json("v9.0.0", pre: true), Courante, SansCle)); }
        [Fact] public void Brouillon_ignore() { Assert.Null(MiseAJour.Analyser(Json("v9.0.0", draft: true), Courante, SansCle)); }

        [Theory]
        [InlineData("https://evil.example/releases/v9.0.0")]
        [InlineData("http://github.com/alexandre-dfr/GTweak/releases/tag/v9.0.0")]
        [InlineData("https://github.com/autre/depot/releases/tag/v9.0.0")]
        public void Page_hors_des_releases_du_depot_refusee(string url) { Assert.Null(MiseAJour.Analyser(Json("v9.0.0", url: url), Courante, SansCle)); }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("pas du json")]
        [InlineData("{ \"message\": \"API rate limit exceeded\" }")]
        public void Reponse_inattendue_ne_propose_rien(string json) { Assert.Null(MiseAJour.Analyser(json, Courante, SansCle)); }

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
