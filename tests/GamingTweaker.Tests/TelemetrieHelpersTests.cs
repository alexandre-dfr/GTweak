using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using Microsoft.Win32;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>
    /// Aides de Common.ps1 utilisées par les scripts du menu Télémétrie et service « Tout restaurer » :
    /// vrai powershell.exe, uniquement sous HKCU\Software\GamingTweakerTest et le dossier temporaire.
    /// </summary>
    public class TelemetrieHelpersTests : IDisposable
    {
        private readonly string sub = @"Software\GamingTweakerTest\" + Guid.NewGuid().ToString("N");
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "GTTel-" + Guid.NewGuid().ToString("N"));
        private string Session => Path.Combine(tmp, "Sessions", "20260101-100000");
        private string Journal => Path.Combine(Session, "journal.jsonl");
        private string K => "'HKCU:\\" + sub + "'";

        public TelemetrieHelpersTests() { Directory.CreateDirectory(Session); }

        public void Dispose()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(sub, false); } catch { }
            try { Directory.Delete(tmp, true); } catch { }
        }

        private static CatalogItem Item(string id, string apply, string detect = "d", string revert = null) => new CatalogItem
        {
            Id = id, Menu = "Télémétrie", Groupe = "G", Titre = "T", Risque = RiskLevel.Sur, Reversible = true, Detect = detect, Apply = apply, Revert = revert,
        };

        private async Task<RunResult> Appliquer(string id, string apply)
            => await Runner.RunAsync(ScriptBuilder.Build(new[] { Item(id, apply) }, new Selection { OptionIds = new HashSet<string> { id } }, Journal), null, CancellationToken.None, _ => 120);

        private object Lire(string name) { using (var k = Registry.CurrentUser.OpenSubKey(sub)) return k == null ? null : k.GetValue(name); }
        private int LignesJournal() => File.Exists(Journal) ? File.ReadAllLines(Journal).Count(l => l.Trim().Length > 0) : 0;

        private string Apply => string.Join("\n",
            "$k = " + K,
            "Set-TweakRegistryIfNeeded -Path $k -Name 'A' -Type DWord -Value 0",
            "Set-TweakRegistryIfNeeded -Path $k -Name 'S' -Type String -Value 'x'",
            "$t = Set-TweakTaskIfNeeded -Path '\\GamingTweakerTest\\TacheAbsente' -Enabled $false",
            "$s = Set-TweakServiceIfNeeded -Name 'GTServiceAbsent' -StartType 'Disabled'",
            "if ($t -or $s) { throw 'un élément absent a été signalé présent' }");

        [Fact]
        public async Task Apply_est_idempotent_et_ignore_sans_erreur_les_elements_absents()
        {
            var r1 = await Appliquer("tel.test", Apply);
            Assert.Equal(OutcomeStatus.Ok, r1.Items.Single().Status);
            Assert.Equal(0, Lire("A")); Assert.Equal("x", Lire("S"));
            Assert.Equal(2, LignesJournal());

            var r2 = await Appliquer("tel.test", Apply);
            Assert.Equal(OutcomeStatus.Ok, r2.Items.Single().Status);
            Assert.Equal(2, LignesJournal());   // valeurs déjà à l'état voulu : rien de nouveau au journal
        }

        [Fact]
        public async Task Une_valeur_d_un_autre_type_est_reecrite_et_journalisee()
        {
            Registry.CurrentUser.CreateSubKey(sub).SetValue("A", "0", RegistryValueKind.String);
            await Appliquer("tel.test", "Set-TweakRegistryIfNeeded -Path " + K + " -Name 'A' -Type DWord -Value 0");
            Assert.Equal(0, Lire("A"));
            Assert.Equal(1, LignesJournal());
        }

        private async Task<DetectState> Detecter(string detect)
        {
            var it = Item("tel.test", "x", detect);
            return (await Detection.RunAsync(new[] { it }))["tel.test"];
        }

        [Fact]
        public async Task Detect_compare_toutes_les_valeurs_et_ignore_les_elements_absents()
        {
            string detect = "try { Get-GTTweakState -Registry @(@(" + K + ", 'A', 'DWord', 0), @(" + K + ", 'S', 'String', 'x')) `\n -Service @(,@('GTServiceAbsent', 'Disabled')) `\n -Task @('\\GamingTweakerTest\\TacheAbsente') } catch { 'Unknown' }";
            Assert.Equal(DetectState.NotApplied, await Detecter(detect));
            Registry.CurrentUser.CreateSubKey(sub).SetValue("A", 0, RegistryValueKind.DWord);
            Assert.Equal(DetectState.NotApplied, await Detecter(detect));   // 'S' manque encore
            Registry.CurrentUser.CreateSubKey(sub).SetValue("S", "X", RegistryValueKind.String);
            Assert.Equal(DetectState.Applied, await Detecter(detect));      // comparaison de chaîne insensible à la casse
            Registry.CurrentUser.CreateSubKey(sub).SetValue("A", 1, RegistryValueKind.DWord);
            Assert.Equal(DetectState.NotApplied, await Detecter(detect));
        }

        [Fact]
        public async Task Detect_sans_aucun_element_present_n_est_pas_applique()
        {
            var detect = "Get-GTTweakState -Service @(,@('GTServiceAbsent', 'Disabled')) -Task @('\\GamingTweakerTest\\TacheAbsente')";
            Assert.Equal(DetectState.NotApplied, await Detecter(detect));
        }

        [Fact]
        public async Task Detect_hosts_non_bloques_n_est_pas_applique()
        {
            Assert.Equal(DetectState.NotApplied, await Detecter("Get-GTTweakState -HostName @('gt-test-absent.invalid')"));
        }

        [Fact]
        public void Le_journal_actif_le_plus_recent_est_choisi()
        {
            var svc = new RestaurationService(new DataPaths { SessionsDir = Path.Combine(tmp, "Sessions") });
            Assert.Null(svc.TrouverJournalActif("tel.test"));

            File.WriteAllText(Journal, "{\"item\":\"tel.test\",\"kind\":\"registry\",\"target\":\"HKCU:\\\\x|A\",\"existed\":false,\"before\":null,\"after\":0,\"valueType\":\"DWord\"}\n");
            var recent = Path.Combine(tmp, "Sessions", "20260102-100000");
            Directory.CreateDirectory(recent);
            File.WriteAllText(Path.Combine(recent, "journal.jsonl"), "{\"item\":\"tel.test\",\"kind\":\"registry\",\"target\":\"HKCU:\\\\x|B\",\"existed\":false,\"before\":null,\"after\":0,\"valueType\":\"DWord\"}\n");
            Assert.Equal(Path.Combine(recent, "journal.jsonl"), svc.TrouverJournalActif("tel.test"));

            // Une option déjà annulée dans le journal le plus récent n'est plus « active » : on retombe sur le précédent.
            File.AppendAllText(Path.Combine(recent, "journal.jsonl"), "{\"item\":\"tel.test\",\"kind\":\"undone\"}\n");
            Assert.Equal(Journal, svc.TrouverJournalActif("tel.test"));
            Assert.Null(svc.TrouverJournalActif("tel.autre"));
        }

        [Fact]
        public async Task Tout_restaurer_annule_par_le_journal_puis_par_le_revert()
        {
            await Appliquer("tel.test", Apply);
            Assert.Equal(0, Lire("A"));
            var svc = new RestaurationService(new DataPaths { SessionsDir = Path.Combine(tmp, "Sessions") });

            var journal = svc.TrouverJournalActif("tel.test");
            Assert.Equal(Journal, journal);
            var o = await svc.AnnulerParJournalAsync(journal, "tel.test");
            Assert.Equal(OutcomeStatus.Ok, o.Status);
            Assert.Null(Lire("A")); Assert.Null(Lire("S"));
            Assert.Null(svc.TrouverJournalActif("tel.test"));   // marqueur « undone » écrit

            // Sans journal : le script revert remet les valeurs par défaut (suppression ou valeur imposée).
            Registry.CurrentUser.CreateSubKey(sub).SetValue("A", 0, RegistryValueKind.DWord);
            Registry.CurrentUser.CreateSubKey(sub).SetValue("S", "x", RegistryValueKind.String);
            var revert = "Reset-GTTweaks -Registry @(@(" + K + ", 'A', 'DWord', $null), @(" + K + ", 'S', 'String', 'defaut')) `\n -Service @(,@('GTServiceAbsent', 'Manual')) -Task @('\\GamingTweakerTest\\TacheAbsente')";
            var o2 = await svc.ExecuterRevertAsync(Item("tel.test2", "x", revert: revert));
            Assert.Equal(OutcomeStatus.Ok, o2.Status);
            Assert.Null(Lire("A")); Assert.Equal("defaut", Lire("S"));
        }
    }
}
