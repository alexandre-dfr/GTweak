using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GamingTweaker.Tests
{
    public class RunPipelineTests : IDisposable
    {
        private const string Cle = @"Software\GamingTweakerTest\Pipeline";
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-pipe-" + Guid.NewGuid().ToString("N"));

        public RunPipelineTests() { Directory.CreateDirectory(tmp); }

        public void Dispose()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(Cle, false); } catch { }
            try { Directory.Delete(tmp, true); } catch { }
        }

        private class FauxPoint : IRestorePoint
        {
            public int Appels; public bool Reussi = true;
            public RestorePointResult TryCreate(string description)
            {
                Appels++;
                return new RestorePointResult { Created = Reussi, Message = Reussi ? "" : "Protection du système désactivée." };
            }
        }

        private static CatalogItem Item(string id, string valeur, bool reversible = true, bool hkcu = false, bool redem = false)
        {
            var k = "HKCU:\\" + Cle;
            return new CatalogItem
            {
                Id = id, Menu = "Optimisation", Groupe = "G", Titre = "T", Risque = RiskLevel.Sur,
                Reversible = reversible, Redemarrage = redem, Hkcu = hkcu, DelaiSec = 60,
                Detect = "try { if ((Get-ItemProperty -Path '" + k + "' -Name '" + id + "' -ErrorAction Stop).'" + id + "' -eq " + valeur + ") { 'Applied' } else { 'NotApplied' } } catch { 'NotApplied' }",
                Apply = "Set-TweakRegistry -Path '" + k + "' -Name '" + id + "' -Value " + valeur + " -Type DWord",
                Revert = reversible ? "Write-Output 'revert'" : null,
            };
        }

        private DataPaths Paths() => DataPaths.Resolve(Path.Combine(tmp, "exe"), new MachineFacts { MachineName = "PC", MachineGuid = "12345678-aaaa-bbbb-cccc-ddddeeeeffff" }, Path.Combine(tmp, "pd"));

        private readonly string verrou = @"Local\GamingTweakerTest." + Guid.NewGuid().ToString("N");

        private RunPipeline Pipe(IRestorePoint rp, bool memeUtilisateur = true) => new RunPipeline(rp, () => memeUtilisateur, verrou);

        private RunRequest Req(IEnumerable<CatalogItem> items, bool silent, Func<string, bool> confirm, params string[] ids) => new RunRequest
        {
            Selection = new Selection { OptionIds = new HashSet<string>(ids) },
            Silent = silent, ConfirmSansPointDeRestauration = confirm, CatalogItems = items.ToList(),
            Facts = new MachineFacts { MachineName = "PC", MachineGuid = "12345678-aaaa-bbbb-cccc-ddddeeeeffff" },
            Paths = Paths(),
        };

        private static OutcomeStatus Statut(SessionReport r, string id) => r.Items.Single(i => i.Id == id).Status;

        [Fact]
        public async Task Option_deja_appliquee_est_sautee()
        {
            var a = Item("a", "1");
            Registry.CurrentUser.CreateSubKey(Cle).SetValue("a", 1, RegistryValueKind.DWord);
            var r = await Pipe(new FauxPoint()).ExecuteAsync(Req(new[] { a }, true, null, "a"), null, CancellationToken.None);
            Assert.Equal(OutcomeStatus.AlreadyApplied, Statut(r, "a"));
            Assert.Equal(0, r.ExitCode);
        }

        [Fact]
        public async Task Option_appliquee_puis_rapports_ecrits()
        {
            var a = Item("a", "1", redem: true);
            var r = await Pipe(new FauxPoint()).ExecuteAsync(Req(new[] { a }, true, null, "a"), null, CancellationToken.None);
            Assert.Equal(OutcomeStatus.Ok, Statut(r, "a"));
            Assert.True(r.RedemarrageRequis);
            Assert.Equal(1, (int)Registry.CurrentUser.OpenSubKey(Cle).GetValue("a"));
            foreach (var f in new[] { "report.json", "report.txt", "state.json", "journal.jsonl", "log.txt" })
                Assert.True(File.Exists(Path.Combine(r.SessionDir, f)), f);
            Assert.Equal("Done", (string)JObject.Parse(File.ReadAllText(Path.Combine(r.SessionDir, "state.json")))["etat"]);
            var last = JArray.Parse(File.ReadAllText(Path.Combine(Paths().MachineDir, "last-applied.json")));
            Assert.Contains("a", last.Select(t => (string)t));
        }

        [Fact]
        public async Task Option_retablie_par_windows_est_signalee()
        {
            var a = Item("a", "1");
            var p = Paths();
            Directory.CreateDirectory(p.MachineDir);
            File.WriteAllText(Path.Combine(p.MachineDir, "last-applied.json"), "[\"a\"]");
            var r = await Pipe(new FauxPoint()).ExecuteAsync(Req(new[] { a }, true, null, "a"), null, CancellationToken.None);
            Assert.Contains("a", r.Retablies);
            Assert.Equal(OutcomeStatus.Ok, Statut(r, "a"));
        }

        [Fact]
        public async Task Option_non_reversible_s_execute_toujours()
        {
            var c = Item("c", "1", reversible: false);
            Registry.CurrentUser.CreateSubKey(Cle).SetValue("c", 1, RegistryValueKind.DWord);
            var r = await Pipe(new FauxPoint()).ExecuteAsync(Req(new[] { c }, true, null, "c"), null, CancellationToken.None);
            Assert.Equal(OutcomeStatus.Ok, Statut(r, "c"));
        }

        [Fact]
        public async Task Aucun_point_de_restauration_si_rien_de_reversible()
        {
            var fp = new FauxPoint();
            var c = Item("c", "1", reversible: false);
            await Pipe(fp).ExecuteAsync(Req(new[] { c }, true, null, "c"), null, CancellationToken.None);
            Assert.Equal(0, fp.Appels);
        }

        [Fact]
        public async Task Point_de_restauration_impossible_en_silencieux_continue()
        {
            var a = Item("a", "1");
            var r = await Pipe(new FauxPoint { Reussi = false }).ExecuteAsync(Req(new[] { a }, true, null, "a"), null, CancellationToken.None);
            Assert.Contains(r.Avertissements, w => w.Contains("point de restauration"));
            Assert.Equal(OutcomeStatus.Ok, Statut(r, "a"));
        }

        [Fact]
        public async Task Point_de_restauration_impossible_avec_refus_n_execute_rien()
        {
            var a = Item("a", "1");
            string vu = null;
            var r = await Pipe(new FauxPoint { Reussi = false }).ExecuteAsync(Req(new[] { a }, false, m => { vu = m; return false; }, "a"), null, CancellationToken.None);
            Assert.Empty(r.Items);
            Assert.NotNull(vu);
            Assert.Equal(2, r.ExitCode);
            Assert.Null(Registry.CurrentUser.OpenSubKey(Cle));
        }

        [Fact]
        public async Task Point_de_restauration_impossible_avec_accord_continue()
        {
            var a = Item("a", "1");
            var r = await Pipe(new FauxPoint { Reussi = false }).ExecuteAsync(Req(new[] { a }, false, m => true, "a"), null, CancellationToken.None);
            Assert.Equal(OutcomeStatus.Ok, Statut(r, "a"));
        }

        [Fact]
        public async Task Autre_compte_admin_ignore_les_options_hkcu()
        {
            var h = Item("hkcu", "1", hkcu: true); var b = Item("b", "1");
            var r = await Pipe(new FauxPoint(), memeUtilisateur: false).ExecuteAsync(Req(new[] { h, b }, true, null, "hkcu", "b"), null, CancellationToken.None);
            Assert.Equal(OutcomeStatus.Skipped, Statut(r, "hkcu"));
            Assert.Contains("compte", r.Items.Single(i => i.Id == "hkcu").Message);
            Assert.Equal(OutcomeStatus.Ok, Statut(r, "b"));
            Assert.NotEmpty(r.Avertissements);
            Assert.Equal(0, r.ExitCode);
            Assert.Null(Registry.CurrentUser.OpenSubKey(Cle)?.GetValue("hkcu"));
        }

        [Fact]
        public async Task Echec_d_une_option_donne_le_code_1()
        {
            var a = Item("a", "1"); a.Apply = "throw 'boum'";
            var r = await Pipe(new FauxPoint()).ExecuteAsync(Req(new[] { a }, true, null, "a"), null, CancellationToken.None);
            Assert.Equal(OutcomeStatus.Failed, Statut(r, "a"));
            Assert.Equal(1, r.ExitCode);
        }

        [Fact]
        public void Session_running_est_retrouvee_comme_interrompue()
        {
            var p = Paths();
            Assert.Null(RunPipeline.FindInterrupted(p.MachineDir));
            var d1 = Path.Combine(p.SessionsDir, "20260101-100000"); var d2 = Path.Combine(p.SessionsDir, "20260102-100000");
            Directory.CreateDirectory(d1); Directory.CreateDirectory(d2);
            File.WriteAllText(Path.Combine(d1, "state.json"), "{\"etat\":\"Done\",\"debut\":\"2026-01-01T10:00:00\",\"fin\":\"2026-01-01T10:01:00\"}");
            File.WriteAllText(Path.Combine(d2, "state.json"), "{\"etat\":\"Running\",\"debut\":\"2026-01-02T10:00:00\"}");
            var s = RunPipeline.FindInterrupted(p.MachineDir);
            Assert.NotNull(s);
            Assert.Equal(d2, s.SessionDir);
            Assert.Equal(new DateTime(2026, 1, 2, 10, 0, 0), s.Debut);
        }

        [Fact]
        public async Task Session_terminee_n_est_pas_interrompue()
        {
            var a = Item("a", "1");
            await Pipe(new FauxPoint()).ExecuteAsync(Req(new[] { a }, true, null, "a"), null, CancellationToken.None);
            Assert.Null(RunPipeline.FindInterrupted(Paths().MachineDir));
        }

        [Fact]
        public void Dossier_exe_en_lecture_seule_bascule_sur_programdata()
        {
            var p = Paths(); // exe\ n'existe pas : non inscriptible
            Assert.StartsWith(Path.Combine(tmp, "pd"), p.ProfilsDir);
            Assert.StartsWith(Path.Combine(tmp, "pd"), p.MachineDir);
            Assert.EndsWith(@"Machines\PC-12345678", p.MachineDir);
            Assert.Equal(Path.Combine(tmp, "pd", "Quarantaine"), p.QuarantineDir);
        }

        [Fact]
        public void Dossier_exe_inscriptible_est_utilise_et_sonde_supprimee()
        {
            var exe = Path.Combine(tmp, "exe2"); Directory.CreateDirectory(exe);
            var p = DataPaths.Resolve(exe, new MachineFacts { MachineName = "PC", MachineGuid = "abcdef01-2345" }, Path.Combine(tmp, "pd"));
            // Profils à côté de l'exe (inscriptible) ; Machines et Quarantaine toujours sous la racine protégée (racine unique).
            Assert.Equal(Path.Combine(exe, "Profils"), p.ProfilsDir);
            Assert.Equal(Path.Combine(tmp, "pd", "Machines", "PC-abcdef01", "Sessions"), p.SessionsDir);
            Assert.Equal(Path.Combine(tmp, "pd", "Quarantaine"), p.QuarantineDir);
            Assert.Empty(Directory.GetFiles(exe));
        }

        [Fact]
        public void Seconde_instance_refusee()
        {
            var nom = @"Local\GamingTweakerTest." + Guid.NewGuid().ToString("N");
            using (var premiere = SingleInstance.TryAcquire(nom))
            {
                Assert.NotNull(premiere);
                Assert.Null(SingleInstance.TryAcquire(nom));
            }
            using (var apres = SingleInstance.TryAcquire(nom)) Assert.NotNull(apres);
        }

        [Fact]
        public async Task Verrou_deja_pris_refuse_l_execution_avec_le_code_2()
        {
            var a = Item("a", "1");
            var fp = new FauxPoint();
            using (SingleInstance.TryAcquire(verrou))
            {
                var r = await Pipe(fp).ExecuteAsync(Req(new[] { a }, true, null, "a"), null, CancellationToken.None);
                Assert.Equal(2, r.ExitCode);
                Assert.Empty(r.Items);
                Assert.Null(r.SessionDir);
                Assert.Contains(r.Avertissements, w => w.Contains("Une autre exécution de GamingTweaker est en cours."));
                Assert.Equal(0, fp.Appels);
                Assert.Null(Registry.CurrentUser.OpenSubKey(Cle));
            }
            // Le verrou est libéré après une exécution normale.
            var r2 = await Pipe(fp).ExecuteAsync(Req(new[] { a }, true, null, "a"), null, CancellationToken.None);
            Assert.Equal(0, r2.ExitCode);
            using (var libre = SingleInstance.TryAcquire(verrou)) Assert.NotNull(libre);
        }

        [Fact]
        public async Task Exception_pendant_l_execution_donne_Failed_code_1_et_rapport()
        {
            var a = Item("a", "1");
            var pipe = new RunPipeline(new FauxPoint(), () => { throw new InvalidOperationException("boum interne"); }, verrou);
            var r = await pipe.ExecuteAsync(Req(new[] { a }, true, null, "a"), null, CancellationToken.None);
            Assert.Equal(1, r.ExitCode);
            Assert.Contains(r.Avertissements, w => w.Contains("boum interne"));
            Assert.Equal("Failed", (string)JObject.Parse(File.ReadAllText(Path.Combine(r.SessionDir, "state.json")))["etat"]);
            Assert.True(File.Exists(Path.Combine(r.SessionDir, "report.json")));
            Assert.NotNull(RunPipeline.FindInterrupted(Paths().MachineDir));
        }

        [Fact]
        public void Decision_d_elevation_ne_regarde_que_la_session_courante()
        {
            var owners = new[] { Tuple.Create(1, "S-AUTRE"), Tuple.Create(2, "S-MOI") };
            Assert.True(ElevationCheck.Decide("S-MOI", 2, owners));
            Assert.False(ElevationCheck.Decide("S-MOI", 1, owners));
            Assert.False(ElevationCheck.Decide("S-MOI", 3, owners));       // aucun explorer dans la session : non confirmé
            Assert.False(ElevationCheck.Decide("S-MOI", 2, new Tuple<int, string>[0]));
            Assert.True(ElevationCheck.Decide("s-moi", 2, owners));
        }

        [Fact]
        public async Task Detection_en_un_processus_et_inconnu_si_absente()
        {
            Registry.CurrentUser.CreateSubKey(Cle).SetValue("a", 1, RegistryValueKind.DWord);
            var sans = Item("s", "1"); sans.Detect = null;
            var casse = Item("k", "1"); casse.Detect = "throw 'x'";
            var r = await Detection.RunAsync(new[] { Item("a", "1"), Item("b", "1"), sans, casse });
            Assert.Equal(DetectState.Applied, r["a"]);
            Assert.Equal(DetectState.NotApplied, r["b"]);
            Assert.Equal(DetectState.Unknown, r["s"]);
            Assert.Equal(DetectState.Unknown, r["k"]);
        }

        [Fact]
        public async Task Detection_delai_global_donne_inconnu()
        {
            var lent = Item("l", "1"); lent.Detect = "Start-Sleep 30; 'Applied'";
            var r = await Detection.RunAsync(new[] { Item("b", "1"), lent }, TimeSpan.FromSeconds(4));
            Assert.Equal(DetectState.NotApplied, r["b"]);
            Assert.Equal(DetectState.Unknown, r["l"]);
        }
    }
}
