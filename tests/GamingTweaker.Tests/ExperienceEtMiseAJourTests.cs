using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GamingTweaker.Core;
using GamingTweaker.Ui;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>Résumé de fin d'exécution, avancement par menu, lecture espacée des températures, mise à jour intégrée et script d'installation.</summary>
    public class ExperienceEtMiseAJourTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-exp-" + Guid.NewGuid().ToString("N"));
        public ExperienceEtMiseAJourTests() { Directory.CreateDirectory(tmp); }
        public void Dispose() { try { Directory.Delete(tmp, true); } catch (Exception) { } }

        private static CatalogItem It(string id, string menu = "Optimisation", string titre = null) =>
            new CatalogItem { Id = id, Menu = menu, Groupe = "G", Titre = titre ?? id, Risque = RiskLevel.Sur, Reversible = true, Detect = "d", Apply = "a", Revert = "r" };

        // ---------- Résumé de fin ----------

        private static SessionReport Rapport(bool redemarrage, params ItemOutcome[] items)
        {
            var r = new SessionReport { SessionDir = "x", RedemarrageRequis = redemarrage };
            r.Items.AddRange(items);
            r.ExitCode = items.Any(i => i.Status == OutcomeStatus.Failed) ? 1 : 0;
            return r;
        }

        [Fact]
        public void Le_resume_tient_en_quelques_lignes_avec_les_raisons()
        {
            var cat = new List<CatalogItem> { It("a", titre: "Mode Jeu"), It("b", titre: "HAGS"), It("c", titre: "Widgets"), It("d", titre: "Plan") };
            var r = Rapport(true,
                new ItemOutcome { Id = "a", Status = OutcomeStatus.Ok },
                new ItemOutcome { Id = "b", Status = OutcomeStatus.AlreadyApplied },
                new ItemOutcome { Id = "c", Status = OutcomeStatus.Skipped, Message = "Windows 10" },
                new ItemOutcome { Id = "d", Status = OutcomeStatus.Failed, Message = "accès refusé" });
            var l = ResumeExecution.Construire(r, cat);
            Assert.Equal("2 options appliquées (dont 1 déjà en place)", l[0].Texte);
            Assert.Equal("ok", l[0].Genre);
            Assert.StartsWith("1 option ignorée", l[1].Texte);
            Assert.Contains("Widgets : Windows 10", l[1].Detail);
            Assert.Equal("erreur", l[2].Genre);
            Assert.Contains("Plan : accès refusé", l[2].Detail);
            Assert.StartsWith("Redémarrage nécessaire", l[3].Texte);
            Assert.Equal("Terminé avec des erreurs", ResumeExecution.Titre(r));
        }

        [Fact]
        public void Le_resume_sans_souci_annonce_qu_aucun_redemarrage_n_est_necessaire()
        {
            var r = Rapport(false, new ItemOutcome { Id = "a", Status = OutcomeStatus.Ok });
            var l = ResumeExecution.Construire(r, new List<CatalogItem> { It("a") });
            Assert.Equal(new[] { "1 option appliquée", "Aucun redémarrage nécessaire" }, l.Select(x => x.Texte));
            Assert.Equal("Terminé", ResumeExecution.Titre(r));
            Assert.Equal("Terminé : redémarrez le PC", ResumeExecution.Titre(Rapport(true, new ItemOutcome { Id = "a", Status = OutcomeStatus.Ok })));
            Assert.Equal("Rien n'a été exécuté", ResumeExecution.Titre(new SessionReport()));
        }

        [Fact]
        public void Les_raisons_au_dela_de_la_limite_renvoient_au_journal()
        {
            var items = Enumerable.Range(0, 7).Select(i => new ItemOutcome { Id = "x" + i, Status = OutcomeStatus.Skipped, Message = "m" }).ToArray();
            var l = ResumeExecution.Construire(Rapport(false, items), new List<CatalogItem>());
            var detail = l.Single(x => x.Genre == "attention").Detail.Split('\n');
            Assert.Equal(ResumeExecution.DetailsMax + 1, detail.Length);
            Assert.Contains("et 3 autres", detail.Last());
        }

        // ---------- Avancement par menu ----------

        [Fact]
        public async Task L_avancement_du_menu_compte_les_options_appliquees()
        {
            var items = new List<CatalogItem> { It("o.a"), It("o.b"), It("o.c"), It("t.a", "Télémétrie") };
            var paths = new DataPaths { ProfilsDir = Path.Combine(tmp, "P"), MachineDir = Path.Combine(tmp, "M"), SessionsDir = Path.Combine(tmp, "S"), QuarantineDir = Path.Combine(tmp, "Q") };
            var vm = new MainViewModel(items, new MachineFacts { WindowsMajor = "11", Edition = "Pro" }, paths,
                its => Task.FromResult(its.ToDictionary(i => i.Id, i => i.Id == "o.a" || i.Id == "o.c" ? DetectState.Applied : DetectState.NotApplied)),
                null, o => true);
            await vm.DetectAsync();
            vm.MenuCourant = "Optimisation";
            Assert.Equal("2 / 3 appliquées", vm.AvancementMenu);
            Assert.Equal(2.0 / 3, vm.ProportionMenu, 3);
            vm.MenuCourant = "Télémétrie";
            Assert.Equal("0 / 1 appliquée", vm.AvancementMenu);
            vm.AppliquerEtat("t.a", DetectState.Applied);
            Assert.Equal("1 / 1 appliquée", vm.AvancementMenu);
            vm.MenuCourant = MainViewModel.MenuAccueil;
            Assert.Equal("", vm.AvancementMenu);
        }

        // ---------- Compteur : températures espacées ----------

        [Fact]
        public void Les_temperatures_sont_lues_au_premier_releve_puis_toutes_les_quatre_secondes()
        {
            var lus = Enumerable.Range(0, 9).Where(CompteurService.DoitLireTemperatures).ToArray();
            Assert.Equal(new[] { 0, 4, 8 }, lus);
            Assert.Equal(4, CompteurService.TemperaturesToutesLesNSecondes);
        }

        // ---------- Mise à jour intégrée ----------

        private const string Page = "https://github.com/alexandre-dfr/GTweak/releases/tag/v9.0.0";
        private const string UrlExe = "https://github.com/alexandre-dfr/GTweak/releases/download/v9.0.0/GamingTweaker.exe";

        private static string Sha(string contenu)
        {
            using (var h = System.Security.Cryptography.SHA256.Create())
                return string.Concat(h.ComputeHash(Encoding.UTF8.GetBytes(contenu)).Select(b => b.ToString("x2")));
        }

        private static string Json(string urlExe = UrlExe, string digest = null, string body = "", string nom = "GamingTweaker.exe") =>
            "{\"tag_name\":\"v9.0.0\",\"draft\":false,\"prerelease\":false,\"html_url\":\"" + Page + "\",\"body\":" + Newtonsoft.Json.JsonConvert.ToString(body)
            + ",\"assets\":[{\"name\":\"" + nom + "\",\"browser_download_url\":\"" + urlExe + "\"" + (digest == null ? "" : ",\"digest\":\"" + digest + "\"") + "}]}";

        private static readonly System.Version Courante = new System.Version(0, 9, 1);

        [Fact]
        public void L_exe_et_l_empreinte_de_github_sont_lus()
        {
            var h = Sha("x");
            var v = MiseAJour.Analyser(Json(digest: "sha256:" + h.ToUpperInvariant()), Courante);
            Assert.Equal(UrlExe, v.UrlExe);
            Assert.Equal(h, v.Sha256);
            Assert.True(v.PeutInstaller);
        }

        [Fact]
        public void Sans_empreinte_github_celle_des_notes_de_la_ci_est_utilisee()
        {
            var h = Sha("y");
            var v = MiseAJour.Analyser(Json(body: "Exe non signé.\n\nSHA-256 de GamingTweaker.exe : " + h.ToUpperInvariant()), Courante);
            Assert.Equal(h, v.Sha256);
            Assert.True(v.PeutInstaller);
        }

        [Theory]
        [InlineData("https://evil.example/releases/download/v9.0.0/GamingTweaker.exe")]
        [InlineData("https://github.com/autre/depot/releases/download/v9.0.0/GamingTweaker.exe")]
        [InlineData("http://github.com/alexandre-dfr/GTweak/releases/download/v9.0.0/GamingTweaker.exe")]
        [InlineData("https://github.com/alexandre-dfr/GTweak/releases/download/v9.0.0/autre.exe")]
        public void Un_exe_hors_des_releases_du_depot_n_est_jamais_installe(string url)
        {
            var v = MiseAJour.Analyser(Json(urlExe: url, digest: "sha256:" + Sha("x")), Courante);
            Assert.NotNull(v);                 // la version reste annoncée (page de la release)
            Assert.Null(v.UrlExe);
            Assert.False(v.PeutInstaller);
        }

        [Fact]
        public void Sans_empreinte_l_installation_n_est_pas_proposee()
        {
            var v = MiseAJour.Analyser(Json(digest: "sha1:abc"), Courante);
            Assert.False(v.PeutInstaller);
            Assert.Null(MiseAJour.Analyser(Json(), Courante).Sha256);
        }

        private VersionDisponible Dispo(string contenu) =>
            new VersionDisponible { Version = new System.Version(9, 0, 0), Url = Page, UrlExe = UrlExe, Sha256 = Sha(contenu) };

        [Fact]
        public async Task La_nouvelle_version_remplace_l_exe_quand_l_empreinte_correspond()
        {
            var exe = Path.Combine(tmp, "GamingTweaker.exe");
            File.WriteAllText(exe, "ancienne");
            string demande = null;
            await MiseAJour.InstallerAsync(Dispo("nouvelle"), exe, (url, dest) => { demande = url; File.WriteAllText(dest, "nouvelle"); return Task.FromResult(0); });
            Assert.Equal(UrlExe, demande);
            Assert.Equal("nouvelle", File.ReadAllText(exe));
            Assert.Equal("ancienne", File.ReadAllText(exe + ".ancien"));
            Assert.False(File.Exists(exe + ".nouveau"));
            MiseAJour.NettoyerAncien(exe);
            Assert.False(File.Exists(exe + ".ancien"));
        }

        [Fact]
        public async Task Un_telechargement_modifie_laisse_l_exe_intact()
        {
            var exe = Path.Combine(tmp, "GamingTweaker.exe");
            File.WriteAllText(exe, "ancienne");
            var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
                MiseAJour.InstallerAsync(Dispo("attendue"), exe, (url, dest) => { File.WriteAllText(dest, "piégée"); return Task.FromResult(0); }));
            Assert.Contains("empreinte", ex.Message);
            Assert.Equal("ancienne", File.ReadAllText(exe));
            Assert.False(File.Exists(exe + ".nouveau"));
            Assert.False(File.Exists(exe + ".ancien"));
        }

        [Fact]
        public async Task Une_version_sans_exe_verifiable_n_est_pas_installee()
        {
            var exe = Path.Combine(tmp, "GamingTweaker.exe");
            File.WriteAllText(exe, "ancienne");
            await Assert.ThrowsAsync<InvalidOperationException>(() => MiseAJour.InstallerAsync(new VersionDisponible { Version = new System.Version(9, 0, 0), Url = Page }, exe));
            Assert.Equal("ancienne", File.ReadAllText(exe));
        }

        [Fact]
        public void Le_remplacement_qui_echoue_remet_l_exe_en_place()
        {
            var exe = Path.Combine(tmp, "GamingTweaker.exe");
            File.WriteAllText(exe, "ancienne");
            Assert.ThrowsAny<Exception>(() => MiseAJour.Remplacer(exe, Path.Combine(tmp, "absent.nouveau")));
            Assert.Equal("ancienne", File.ReadAllText(exe));
            Assert.False(File.Exists(exe + ".ancien"));
        }

        // ---------- Script d'installation et CI ----------

        private static string Racine() => Path.GetDirectoryName(Path.GetDirectoryName(Helpers.Couverture.Chemin()));

        [Fact]
        public void Le_script_d_installation_verifie_l_empreinte_publiee_par_la_ci()
        {
            var script = File.ReadAllText(Path.Combine(Racine(), "installer.ps1"), Encoding.UTF8);
            var ci = File.ReadAllText(Path.Combine(Racine(), ".github", "workflows", "build.yml"), Encoding.UTF8);
            Assert.Contains("SHA-256 de GamingTweaker.exe : $env:HASH", ci);                 // texte écrit par la CI dans les notes
            Assert.Contains(@"SHA-256 de GamingTweaker\.exe", script);                       // texte lu par le script
            Assert.Contains("Get-FileHash", script);
            Assert.Contains("https://github.com/$depot/releases/download/", script);
            Assert.DoesNotContain("Invoke-Expression", script);
            var readme = File.ReadAllText(Path.Combine(Racine(), "README.md"), Encoding.UTF8);
            Assert.Contains("irm https://raw.githubusercontent.com/alexandre-dfr/GTweak/main/installer.ps1 | iex", readme);
        }
    }
}
