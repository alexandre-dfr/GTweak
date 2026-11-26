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
    /// <summary>Résumé de fin d'exécution, avancement par menu, lecture espacée des températures, mise à jour intégrée signée et version portable.</summary>
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
        private const string UrlSig = "https://github.com/alexandre-dfr/GTweak/releases/download/v9.0.0/GamingTweaker.exe.sig";
        private static readonly System.Version V9 = new System.Version(9, 0, 0);
        private static readonly string[] SansCle = new string[0];

        // Clé de test générée une fois (3072 bits, comme celle du mainteneur) ; la partie privée ne sert qu'à signer ici.
        private static readonly Lazy<System.Security.Cryptography.RSACryptoServiceProvider> CleTest = new Lazy<System.Security.Cryptography.RSACryptoServiceProvider>(() =>
            new System.Security.Cryptography.RSACryptoServiceProvider(3072) { PersistKeyInCsp = false });
        private static string[] Cles => new[] { CleTest.Value.ToXmlString(false) };

        private static string Signer(System.Version v, string sha, System.Security.Cryptography.RSA cle = null) =>
            Convert.ToBase64String((cle ?? CleTest.Value).SignData(Encoding.UTF8.GetBytes(SignatureRelease.Message(v, sha)),
                System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1));

        private static string Sha(string contenu)
        {
            using (var h = System.Security.Cryptography.SHA256.Create())
                return string.Concat(h.ComputeHash(Encoding.UTF8.GetBytes(contenu)).Select(b => b.ToString("x2")));
        }

        private static string Json(string urlExe = UrlExe, string digest = null, string body = "", string nom = "GamingTweaker.exe", bool sig = true) =>
            "{\"tag_name\":\"v9.0.0\",\"draft\":false,\"prerelease\":false,\"html_url\":\"" + Page + "\",\"body\":" + Newtonsoft.Json.JsonConvert.ToString(body)
            + ",\"assets\":[{\"name\":\"" + nom + "\",\"browser_download_url\":\"" + urlExe + "\"" + (digest == null ? "" : ",\"digest\":\"" + digest + "\"") + "}"
            + (sig ? ",{\"name\":\"GamingTweaker.exe.sig\",\"browser_download_url\":\"" + UrlSig + "\"}" : "") + "]}";

        private static readonly System.Version Courante = new System.Version(0, 9, 1);

        [Fact]
        public void L_exe_la_signature_et_l_empreinte_de_github_sont_lus()
        {
            var h = Sha("x");
            var v = MiseAJour.Analyser(Json(digest: "sha256:" + h.ToUpperInvariant()), Courante, Cles);
            Assert.Equal(UrlExe, v.UrlExe);
            Assert.Equal(UrlSig, v.UrlSignature);
            Assert.Equal(h, v.Sha256);
            Assert.True(v.PeutInstaller);
        }

        [Fact]
        public void Sans_empreinte_github_celle_des_notes_de_la_ci_est_utilisee()
        {
            var h = Sha("y");
            var v = MiseAJour.Analyser(Json(body: "Exe non signé.\n\nSHA-256 de GamingTweaker.exe : " + h.ToUpperInvariant()), Courante, Cles);
            Assert.Equal(h, v.Sha256);
        }

        [Fact]
        public void Avec_une_cle_une_version_pas_encore_signee_n_est_pas_annoncee()
        {
            Assert.Null(MiseAJour.Analyser(Json(sig: false), Courante, Cles));
        }

        [Fact]
        public void Sans_cle_configuree_seule_la_page_est_proposee()
        {
            var v = MiseAJour.Analyser(Json(), Courante, SansCle);
            Assert.NotNull(v);
            Assert.False(v.PeutInstaller);
        }

        [Theory]
        [InlineData("https://evil.example/releases/download/v9.0.0/GamingTweaker.exe")]
        [InlineData("https://github.com/autre/depot/releases/download/v9.0.0/GamingTweaker.exe")]
        [InlineData("http://github.com/alexandre-dfr/GTweak/releases/download/v9.0.0/GamingTweaker.exe")]
        [InlineData("https://github.com/alexandre-dfr/GTweak/releases/download/v9.0.0/autre.exe")]
        public void Un_exe_hors_des_releases_du_depot_n_est_jamais_installe(string url)
        {
            var v = MiseAJour.Analyser(Json(urlExe: url, digest: "sha256:" + Sha("x")), Courante, SansCle);
            Assert.NotNull(v);                 // sans clé, la version reste annoncée (page de la release)
            Assert.Null(v.UrlExe);
            Assert.False(v.PeutInstaller);
            Assert.Null(MiseAJour.Analyser(Json(urlExe: url), Courante, Cles));   // avec clé : rien d'installable, rien d'annoncé
        }

        // ---------- Signature ----------

        [Fact]
        public void La_signature_lie_la_version_et_l_empreinte()
        {
            var h = Sha("contenu");
            var s = Signer(V9, h);
            Assert.True(SignatureRelease.Verifier(s, V9, h.ToUpperInvariant(), Cles));
            Assert.False(SignatureRelease.Verifier(s, new System.Version(9, 0, 1), h, Cles));   // ancien exe republié sous un autre tag
            Assert.False(SignatureRelease.Verifier(s, V9, Sha("autre"), Cles));
            Assert.False(SignatureRelease.Verifier("pas du base64 !", V9, h, Cles));
            Assert.False(SignatureRelease.Verifier(s, V9, h, SansCle));
            using (var autre = new System.Security.Cryptography.RSACryptoServiceProvider(3072) { PersistKeyInCsp = false })
                Assert.False(SignatureRelease.Verifier(s, V9, h, new[] { autre.ToXmlString(false) }));
        }

        [Fact]
        public void Message_signe()
        {
            Assert.Equal("GamingTweaker.exe|9.0.0|" + Sha("a"), SignatureRelease.Message(V9, Sha("a").ToUpperInvariant()));
        }

        [Fact]
        public void Cle_trop_courte_ou_privee_refusee()
        {
            using (var courte = new System.Security.Cryptography.RSACryptoServiceProvider(2048) { PersistKeyInCsp = false })
            {
                var h = Sha("x");
                var s = Convert.ToBase64String(courte.SignData(Encoding.UTF8.GetBytes(SignatureRelease.Message(V9, h)),
                    System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1));
                Assert.False(SignatureRelease.Verifier(s, V9, h, new[] { courte.ToXmlString(false) }));
            }
            var texte = "# commentaire\n\n" + CleTest.Value.ToXmlString(true) + "\n" + CleTest.Value.ToXmlString(false) + "\n";
            Assert.Equal(new[] { CleTest.Value.ToXmlString(false) }, SignatureRelease.LireCles(texte).ToArray());
        }

        [Fact]
        public void Les_cles_du_depot_sont_toutes_valides()
        {
            var texte = File.ReadAllText(Path.Combine(Racine(), "src", "GamingTweaker", "Assets", "cles-release.txt"), Encoding.UTF8);
            var lignes = texte.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
            Assert.Equal(lignes.Count, SignatureRelease.LireCles(texte).Count());   // aucune ligne ignorée en silence
            Assert.Equal(lignes.Count, SignatureRelease.Cles.Length);               // et toutes intégrées à l'exe
        }

        // ---------- Installation ----------

        private VersionDisponible Dispo(string sha, bool shaGithub = true) =>
            new VersionDisponible { Version = V9, Url = Page, UrlExe = UrlExe, UrlSignature = UrlSig, Sha256 = shaGithub ? sha : null, Cles = Cles };

        private static Func<string, Task<string>> Sig(string s) => url => { Assert.Equal(UrlSig, url); return Task.FromResult(s); };
        private static Func<string, string, Task> Ecrire(string contenu) => (url, dest) => { Assert.Equal(UrlExe, url); File.WriteAllText(dest, contenu); return Task.FromResult(0); };

        [Fact]
        public async Task La_nouvelle_version_signee_remplace_l_exe_et_reste_verrouillee_jusqu_au_lancement()
        {
            var exe = Path.Combine(tmp, "GamingTweaker.exe");
            File.WriteAllText(exe, "ancienne");
            var h = Sha("nouvelle");
            using (await MiseAJour.InstallerAsync(Dispo(h, shaGithub: false), exe, Ecrire("nouvelle"), Sig(Signer(V9, h))))
            {
                // Tant que la mise à jour n'est pas libérée : ni écriture, ni renommage, ni suppression du nouvel exe.
                Assert.ThrowsAny<IOException>(() => File.OpenWrite(exe).Dispose());
                Assert.ThrowsAny<IOException>(() => File.Move(exe, exe + ".vole"));
                Assert.ThrowsAny<IOException>(() => File.Delete(exe));
            }
            Assert.Equal("nouvelle", File.ReadAllText(exe));
            Assert.Equal("ancienne", File.ReadAllText(exe + ".ancien"));
            Assert.False(File.Exists(exe + ".nouveau"));
            MiseAJour.NettoyerAncien(exe);
            Assert.False(File.Exists(exe + ".ancien"));
        }

        [Fact]
        public async Task Le_nouvel_exe_verrouille_peut_etre_lance()
        {
            var systeme = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "whoami.exe");
            Assert.True(File.Exists(systeme));
            var exe = Path.Combine(tmp, "GamingTweaker.exe");
            File.WriteAllText(exe, "ancienne");
            var h = MiseAJour.Sha256Fichier(systeme);
            Func<string, string, Task> copier = (url, dest) => { File.Copy(systeme, dest); return Task.FromResult(0); };
            using (await MiseAJour.InstallerAsync(Dispo(h), exe, copier, Sig(Signer(V9, h))))
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                // Windows crée le processus malgré la poignée tenue (partage en lecture) : c'est ce que fait MiseAJour.Relancer.
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    Assert.True(p.WaitForExit(30000));
                }
            }
        }

        [Fact]
        public async Task Une_signature_invalide_laisse_l_exe_intact()
        {
            var exe = Path.Combine(tmp, "GamingTweaker.exe");
            File.WriteAllText(exe, "ancienne");
            var h = Sha("piégée");
            using (var autre = new System.Security.Cryptography.RSACryptoServiceProvider(3072) { PersistKeyInCsp = false })
            {
                // Release publiée par quelqu'un d'autre : empreinte GitHub cohérente, mais signée avec une autre clé.
                var ex = await Assert.ThrowsAsync<InvalidDataException>(() => MiseAJour.InstallerAsync(Dispo(h), exe, Ecrire("piégée"), Sig(Signer(V9, h, autre))));
                Assert.Equal(MiseAJour.MessageSignature, ex.Message);
            }
            Assert.Equal("ancienne", File.ReadAllText(exe));
            Assert.False(File.Exists(exe + ".nouveau"));
            Assert.False(File.Exists(exe + ".ancien"));
        }

        [Fact]
        public async Task Un_telechargement_modifie_laisse_l_exe_intact()
        {
            var exe = Path.Combine(tmp, "GamingTweaker.exe");
            File.WriteAllText(exe, "ancienne");
            var h = Sha("attendue");
            var ex = await Assert.ThrowsAsync<InvalidDataException>(() => MiseAJour.InstallerAsync(Dispo(h), exe, Ecrire("piégée"), Sig(Signer(V9, h))));
            Assert.Contains("empreinte", ex.Message);
            Assert.Equal("ancienne", File.ReadAllText(exe));
            Assert.False(File.Exists(exe + ".nouveau"));
            Assert.False(File.Exists(exe + ".ancien"));
        }

        [Fact]
        public async Task Une_version_sans_exe_signe_n_est_pas_installee()
        {
            var exe = Path.Combine(tmp, "GamingTweaker.exe");
            File.WriteAllText(exe, "ancienne");
            await Assert.ThrowsAsync<InvalidOperationException>(() => MiseAJour.InstallerAsync(new VersionDisponible { Version = V9, Url = Page, UrlExe = UrlExe, Cles = Cles }, exe));
            await Assert.ThrowsAsync<InvalidOperationException>(() => MiseAJour.InstallerAsync(new VersionDisponible { Version = V9, Url = Page, UrlExe = UrlExe, UrlSignature = UrlSig, Cles = SansCle }, exe));
            Assert.Equal("ancienne", File.ReadAllText(exe));
        }

        [Fact]
        public async Task Le_remplacement_qui_echoue_remet_l_exe_en_place()
        {
            var exe = Path.Combine(tmp, "GamingTweaker.exe");
            File.WriteAllText(exe, "ancienne");
            var h = Sha("nouvelle");
            // L'exe en cours ne peut pas être renommé (ouvert sans partage en suppression) : rien n'est remplacé.
            using (new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read))
                await Assert.ThrowsAnyAsync<IOException>(() => MiseAJour.InstallerAsync(Dispo(h), exe, Ecrire("nouvelle"), Sig(Signer(V9, h))));
            Assert.Equal("ancienne", File.ReadAllText(exe));
            Assert.False(File.Exists(exe + ".nouveau"));
        }

        [Theory]
        [InlineData(@"\\?\C:\Jeux\GamingTweaker.exe.nouveau", @"C:\Jeux\GamingTweaker.exe.nouveau", true)]
        [InlineData(@"\\?\c:\jeux\gamingtweaker.exe.nouveau", @"C:\Jeux\GamingTweaker.exe.nouveau", true)]
        [InlineData(@"\\?\UNC\serveur\partage\GamingTweaker.exe.nouveau", @"\\serveur\partage\GamingTweaker.exe.nouveau", true)]
        [InlineData(@"\\?\C:\Windows\System32\GamingTweaker.exe.nouveau", @"C:\Jeux\GamingTweaker.exe.nouveau", false)]
        [InlineData(null, @"C:\Jeux\GamingTweaker.exe.nouveau", false)]
        public void Chemin_reel_compare_au_chemin_attendu(string reel, string attendu, bool meme)
        {
            Assert.Equal(meme, MiseAJour.MemeChemin(reel, attendu));
        }

        [Theory]
        [InlineData(@"D:\Jeux\GamingTweaker.exe", @"\??\D:\Jeux\GamingTweaker.exe")]
        [InlineData(@"\\serveur\partage\GamingTweaker.exe", @"\??\UNC\serveur\partage\GamingTweaker.exe")]
        public void Chemin_nt_pour_le_renommage(string chemin, string attendu)
        {
            Assert.Equal(attendu, MiseAJour.CheminNt(chemin));
        }

        // ---------- Copies de la routine et du démarrage avec Windows ----------

        [Theory]
        [InlineData("0.9.2.10", "0.9.2.58", true)]
        [InlineData("0.9.2.58", "0.9.3.1", true)]
        [InlineData("0.9.2.58", "0.9.2.58", false)]
        [InlineData("0.9.3.1", "0.9.2.58", false)]    // jamais remplacée par une version plus ancienne
        [InlineData(null, "0.9.2.58", false)]
        public void Copie_protegee_remplacee_seulement_si_plus_ancienne(string copie, string courante, bool attendu)
        {
            Assert.Equal(attendu, CopiesProtegees.AMettreAJour(copie == null ? null : new System.Version(copie), new System.Version(courante)));
        }

        [Fact]
        public void Le_telechargement_de_la_mise_a_jour_est_borne_en_taille()
        {
            var src = new MemoryStream(new byte[1000]);
            var dst = new MemoryStream();
            Assert.Equal(1000, MiseAJour.CopierBorneAsync(src, dst, 1000).Result);
            Assert.Equal(1000, dst.Length);

            var trop = new MemoryStream(new byte[300000]);
            var ex = Assert.Throws<AggregateException>(() => MiseAJour.CopierBorneAsync(trop, new MemoryStream(), 100000).Wait());
            Assert.IsType<InvalidDataException>(ex.InnerException);
            Assert.Contains("rien n'a été remplacé", ex.InnerException.Message);
            Assert.True(MiseAJour.TailleMaxExe >= 50L * 1024 * 1024);
            Assert.InRange(MiseAJour.TailleMaxReponseApi, 64 * 1024, 4 * 1024 * 1024);   // réponse de l'API GitHub bornée
        }

        // ---------- Version portable et CI ----------

        private static string Racine() => Path.GetDirectoryName(Path.GetDirectoryName(Helpers.Couverture.Chemin()));

        [Fact]
        public void Version_portable_uniquement_sans_script_d_installation()
        {
            Assert.False(File.Exists(Path.Combine(Racine(), "installer.ps1")));
            var readme = File.ReadAllText(Path.Combine(Racine(), "README.md"), Encoding.UTF8);
            Assert.DoesNotContain("installer.ps1", readme);
            Assert.DoesNotContain("| iex", readme);
        }

        [Fact]
        public void La_ci_publie_l_empreinte_et_epingle_ses_actions()
        {
            var ci = File.ReadAllText(Path.Combine(Racine(), ".github", "workflows", "build.yml"), Encoding.UTF8);
            Assert.Contains("SHA-256 de GamingTweaker.exe : $env:HASH", ci);   // texte relu par MiseAJour (ShaNotes)
            var uses = System.Text.RegularExpressions.Regex.Matches(ci, @"uses:\s*(\S+)").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.NotEmpty(uses);
            Assert.All(uses, u => Assert.Matches(@"^[\w.-]+/[\w.-]+@[0-9a-f]{40}$", u));
        }
    }
}
