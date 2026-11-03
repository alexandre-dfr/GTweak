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

        [Fact]
        public async Task Annulation_marque_le_reste_annule()
        {
            var script = ScriptBuilder.Build(new[] { Item("a", "Start-Sleep 30") }, Sel("a"), Path.Combine(Path.GetTempPath(), "j.json"));
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var (res, _) = await Run(script, 120, cts.Token);
            Assert.Equal(OutcomeStatus.Skipped, res.Items[0].Status);
            Assert.Equal("annulé", res.Items[0].Message);
        }
    }
}
