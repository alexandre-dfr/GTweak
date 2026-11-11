using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using Xunit;

namespace GamingTweaker.Tests
{
    public class RunnerTests
    {
        private static CatalogItem Item(string id, string apply) => new CatalogItem
        {
            Id = id, Menu = "Optimisation", Groupe = "G", Titre = "T", Risque = RiskLevel.Sur,
            Reversible = false, Detect = "d", Apply = apply,
        };

        private static Selection Sel(params string[] ids) => new Selection { OptionIds = new HashSet<string>(ids) };

        private static async Task<(RunResult res, List<string> log)> Run(string script, int delai = 120, CancellationToken ct = default(CancellationToken))
        {
            var log = new List<string>();
            var res = await Runner.RunAsync(script, l => { lock (log) log.Add(l); }, ct, id => delai);
            return (res, log);
        }

        [Fact]
        public async Task Erreur_d_une_option_n_arrete_pas_la_suivante()
        {
            var script = ScriptBuilder.Build(new[] { Item("a", "throw 'boum'"), Item("b", "Write-Output 'bien'") }, Sel("a", "b"), Path.Combine(Path.GetTempPath(), "j.json"));
            var (res, _) = await Run(script);
            Assert.Equal(new[] { OutcomeStatus.Failed, OutcomeStatus.Ok }, res.Items.Select(i => i.Status).ToArray());
            Assert.Contains("boum", res.Items[0].Message);
        }

        [Fact]
        public async Task Selection_de_liste_transmise_au_script()
        {
            var s = Sel("a");
            s.Listes["a"] = new List<string> { "x y", "it's $env:TEMP" };
            var script = ScriptBuilder.Build(new[] { Item("a", "Write-Output ('SEL=' + ($Selection -join ';'))") }, s, Path.Combine(Path.GetTempPath(), "j.json"));
            var (res, log) = await Run(script);
            Assert.Equal(OutcomeStatus.Ok, res.Items[0].Status);
            Assert.Contains("SEL=x y;it's $env:TEMP", log);
        }

        [Fact]
        public async Task Delai_depasse_marque_echec_et_continue()
        {
            var script = ScriptBuilder.Build(new[] { Item("a", "Start-Sleep 30"), Item("b", "Write-Output 'suite'") }, Sel("a", "b"), Path.Combine(Path.GetTempPath(), "j.json"));
            var log = new List<string>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var res = await Runner.RunAsync(script, l => { lock (log) log.Add(l); }, CancellationToken.None, id => id == "a" ? 1 : 120);
            Assert.True(sw.Elapsed.TotalSeconds < 25);
            Assert.Equal(new[] { OutcomeStatus.Failed, OutcomeStatus.Ok }, res.Items.Select(i => i.Status).ToArray());
            Assert.Contains("délai dépassé (1 s)", res.Items[0].Message);
            Assert.Contains("suite", log);
        }

        [Fact]
        public async Task Chemin_avec_accents_et_apostrophe()
        {
            var dir = Path.Combine(Path.GetTempPath(), "Jérôme D'Arc");
            var journal = Path.Combine(dir, "journal.txt");
            try
            {
                var apply = "New-Item -ItemType Directory -Force -Path (Split-Path $GT_JournalPath) | Out-Null\r\n" +
                            "Set-Content -LiteralPath $GT_JournalPath -Value 'été' -Encoding UTF8\r\n" +
                            "Write-Output ('lu=' + (Get-Content -LiteralPath $GT_JournalPath -Encoding UTF8))";
                var script = ScriptBuilder.Build(new[] { Item("a", apply) }, Sel("a"), journal);
                var (res, log) = await Run(script);
                Assert.Equal(OutcomeStatus.Ok, res.Items[0].Status);
                Assert.Contains("lu=été", log);
                Assert.True(File.Exists(journal));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public async Task Skip_GTItem_donne_Skipped_et_message_sur_une_ligne()
        {
            var script = ScriptBuilder.Build(new[] { Item("a", "Skip-GTItem 'pas utile'"), Item("b", "throw \"l1`nl2|x\"") }, Sel("a", "b"), Path.Combine(Path.GetTempPath(), "j.json"));
            var (res, _) = await Run(script);
            Assert.Equal(OutcomeStatus.Skipped, res.Items[0].Status);
            Assert.Equal("pas utile", res.Items[0].Message);
            Assert.Equal(OutcomeStatus.Failed, res.Items[1].Status);
            Assert.Equal("l1 l2/x", res.Items[1].Message);
        }

        [Fact]
        public async Task Seules_les_options_selectionnees_et_pas_de_selection_de_liste_vide()
        {
            var script = ScriptBuilder.Build(new[] { Item("a", "Write-Output 'A'"), Item("b", "Write-Output ('n=' + $Selection.Count)") }, Sel("b"), Path.Combine(Path.GetTempPath(), "j.json"));
            var (res, log) = await Run(script);
            Assert.Single(res.Items);
            Assert.Contains("n=0", log);
            Assert.DoesNotContain("A", log);
        }

        // ---- Annulation (A) ----
        private static async Task<RunResult> RunCancelOn(string script, string trigger)
        {
            var cts = new CancellationTokenSource();
            var res = await Runner.RunAsync(script, l => { if (l == trigger) cts.Cancel(); }, cts.Token, id => 120);
            return res;
        }

        [Fact]
        public async Task Annulation_en_cours_d_option_marque_tout_le_reste_annule()
        {
            var script = ScriptBuilder.Build(new[] { Item("a", "Start-Sleep 30"), Item("b", "Write-Output 'b'"), Item("c", "Write-Output 'c'") }, Sel("a", "b", "c"), Path.Combine(Path.GetTempPath(), "j.json"));
            var res = await RunCancelOn(script, "##GT|start|a");
            Assert.Equal(new[] { "a", "b", "c" }, res.Items.Select(i => i.Id).ToArray());
            Assert.All(res.Items, i => { Assert.Equal(OutcomeStatus.Skipped, i.Status); Assert.Equal("annulé", i.Message); });
        }

        [Fact]
        public async Task Annulation_entre_deux_options_marque_le_reste_annule()
        {
            var script = ScriptBuilder.Build(new[] { Item("a", "Write-Output 'a'"), Item("b", "Start-Sleep 30"), Item("c", "Write-Output 'c'") }, Sel("a", "b", "c"), Path.Combine(Path.GetTempPath(), "j.json"));
            var res = await RunCancelOn(script, "##GT|ok|a");
            Assert.Equal(new[] { "a", "b", "c" }, res.Items.Select(i => i.Id).ToArray());
            Assert.Equal(OutcomeStatus.Ok, res.Items[0].Status);
            Assert.Equal(OutcomeStatus.Skipped, res.Items[1].Status);
            Assert.Equal(OutcomeStatus.Skipped, res.Items[2].Status);
            Assert.Equal("annulé", res.Items[2].Message);
        }

        // ---- Mort du processus (B) ----
        [Fact]
        public async Task Mort_du_processus_chaque_option_planifiee_a_un_resultat()
        {
            // Choix : l'option en cours est Failed (code), puis le script est relancé et les suivantes s'exécutent.
            var script = ScriptBuilder.Build(new[] { Item("a", "Write-Output 'a'"), Item("b", "[Environment]::Exit(3)"), Item("c", "Write-Output 'c'") }, Sel("a", "b", "c"), Path.Combine(Path.GetTempPath(), "j.json"));
            var (res, _) = await Run(script);
            Assert.Equal(new[] { "a", "b", "c" }, res.Items.Select(i => i.Id).ToArray());
            Assert.Equal(new[] { OutcomeStatus.Ok, OutcomeStatus.Failed, OutcomeStatus.Ok }, res.Items.Select(i => i.Status).ToArray());
            Assert.Contains("code 3", res.Items[1].Message);
        }

        [Fact]
        public void Complete_marque_non_executees_les_options_jamais_demarrees()
        {
            var st = new RunState();
            st.BeginLaunch();
            st.HandleLine("##GT|plan|a"); st.HandleLine("##GT|plan|b"); st.HandleLine("##GT|plan|c");
            st.HandleLine("##GT|start|a"); st.HandleLine("##GT|ok|a");
            st.Complete(false, 3);
            Assert.Equal(OutcomeStatus.Ok, st.Items[0].Status);
            Assert.Equal(OutcomeStatus.Failed, st.Items[1].Status);
            Assert.Equal("non exécuté : PowerShell arrêté (code 3)", st.Items[1].Message);
            Assert.Equal("non exécuté : PowerShell arrêté (code 3)", st.Items[2].Message);
        }

        // ---- Injection par guillemets typographiques (C) ----
        [Theory]
        [InlineData("’")] [InlineData("‘")] [InlineData("‚")] [InlineData("‛")]
        public async Task Chemin_de_journal_avec_guillemet_typographique(string q)
        {
            var journal = Path.Combine(Path.GetTempPath(), "D" + q + "; Write-Output INJECTED2; " + q, "j.txt");
            var apply = "Write-Output ('jp=' + $GT_JournalPath)";
            var script = ScriptBuilder.Build(new[] { Item("a", apply) }, Sel("a"), journal);
            var (res, log) = await Run(script);
            Assert.Equal(OutcomeStatus.Ok, res.Items[0].Status);
            Assert.Contains("jp=" + journal, log);
            Assert.DoesNotContain("INJECTED2", log);
        }

        // ---- Course délai / fin normale (D) ----
        [Fact]
        public void Delai_ne_ecrase_pas_une_option_terminee_entre_temps()
        {
            var st = new RunState();
            st.BeginLaunch();
            st.HandleLine("##GT|plan|a"); st.HandleLine("##GT|plan|b");
            st.HandleLine("##GT|start|a");
            var cand = st.TimeoutCandidate(id => 0, out int sec);
            Assert.NotNull(cand);
            st.HandleLine("##GT|ok|a");
            Assert.False(st.ApplyTimeout(cand, sec));
            Assert.Equal(OutcomeStatus.Ok, st.Items[0].Status);
            Assert.Null(st.Items[0].Message);
        }

        [Fact]
        public void Option_demarree_apres_la_decision_de_delai_n_est_pas_comptee_comme_traitee()
        {
            var st = new RunState();
            st.BeginLaunch();
            st.HandleLine("##GT|plan|a"); st.HandleLine("##GT|plan|b");
            st.HandleLine("##GT|start|a");
            var cand = st.TimeoutCandidate(id => 0, out int sec);
            st.HandleLine("##GT|ok|a");
            st.HandleLine("##GT|start|b");
            Assert.False(st.ApplyTimeout(cand, sec));
            Assert.Equal(new[] { "a" }, st.FinishedIds.ToArray());
        }

        [Fact]
        public void Delai_reel_marque_echec()
        {
            var st = new RunState();
            st.BeginLaunch();
            st.HandleLine("##GT|plan|a"); st.HandleLine("##GT|start|a");
            var cand = st.TimeoutCandidate(id => 0, out int sec);
            Assert.True(st.ApplyTimeout(cand, sec));
            Assert.Equal(OutcomeStatus.Failed, st.Items[0].Status);
            Assert.Equal("délai dépassé (0 s)", st.Items[0].Message);
            Assert.Contains("a", st.FinishedIds);
        }
    }
}
