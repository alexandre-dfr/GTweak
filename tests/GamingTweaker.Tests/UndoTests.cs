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
    /// <summary>Tests d'annulation : exécutent un vrai powershell.exe, uniquement sous HKCU\Software\GamingTweakerTest et le dossier temporaire.</summary>
    public partial class UndoTests : IDisposable
    {
        private readonly string _sub = @"Software\GamingTweakerTest\" + Guid.NewGuid().ToString("N");
        private readonly string _tmp = Path.Combine(Path.GetTempPath(), "GTUndo-" + Guid.NewGuid().ToString("N"));
        private string Jrn => Path.Combine(_tmp, "journal.jsonl");

        public UndoTests() { Directory.CreateDirectory(_tmp); }

        public void Dispose()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(_sub, false); } catch { }
            try
            {
                foreach (var f in Directory.GetFiles(_tmp, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(_tmp, true);
            }
            catch { }
        }

        private static CatalogItem Item(string id, string apply) => new CatalogItem
        {
            Id = id, Menu = "Optimisation", Groupe = "G", Titre = "T", Risque = RiskLevel.Sur,
            Reversible = true, Detect = "d", Apply = apply,
        };

        private static Selection Sel(params string[] ids) => new Selection { OptionIds = new HashSet<string>(ids) };

        private string RegPath => "'HKCU:\\" + _sub + "'";
        private string SetReg(string name, string type, string value) => $"Set-TweakRegistry -Path {RegPath} -Name '{name}' -Type {type} -Value {value}";

        private static async Task<RunResult> Run(string script)
            => await Runner.RunAsync(script, null, CancellationToken.None, id => 120);

        private async Task<RunResult> Apply(string id, string apply, string quarantine = null)
            => await Run(ScriptBuilder.Build(new[] { Item(id, apply) }, Sel(id), Jrn, quarantine));

        private async Task<RunResult> Undo(params string[] ids)
            => await Run(UndoService.BuildScript(Jrn, ids));

        private object Lire(string name)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(_sub)) return k?.GetValue(name);
        }
        private bool Existe(string name) => Lire(name) != null;
        private void Ecrire(string name, object v, RegistryValueKind kind)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(_sub)) k.SetValue(name, v, kind);
        }

        [Fact]
        public async Task Annulation_restaure_valeur_existante()
        {
            Ecrire("X", 5, RegistryValueKind.DWord);
            var r = await Apply("a", SetReg("X", "DWord", "1"));
            Assert.Equal(OutcomeStatus.Ok, r.Items[0].Status);
            Assert.Equal(1, Lire("X"));
            var u = await Undo("a");
            Assert.Equal(OutcomeStatus.Ok, u.Items[0].Status);
            Assert.Equal(5, Lire("X"));
        }

        [Fact]
        public async Task Annulation_supprime_valeur_creee()
        {
            await Apply("a", SetReg("X", "DWord", "1"));
            Assert.True(Existe("X"));
            var u = await Undo("a");
            Assert.Equal(OutcomeStatus.Ok, u.Items[0].Status);
            Assert.False(Existe("X"));
        }

        [Fact]
        public async Task Annulation_en_ordre_inverse()
        {
            Ecrire("X", 5, RegistryValueKind.DWord);
            await Apply("a", SetReg("X", "DWord", "1"));
            await Apply("a", SetReg("X", "DWord", "2"));
            Assert.Equal(2, Lire("X"));
            await Undo("a");
            Assert.Equal(5, Lire("X"));
        }

        [Fact]
        public async Task Annulation_conserve_le_type()
        {
            Ecrire("S", "abc", RegistryValueKind.ExpandString);
            await Apply("a", SetReg("S", "DWord", "7"));
            await Undo("a");
            using (var k = Registry.CurrentUser.OpenSubKey(_sub))
            {
                Assert.Equal(RegistryValueKind.ExpandString, k.GetValueKind("S"));
                Assert.Equal("abc", k.GetValue("S", null, RegistryValueOptions.DoNotExpandEnvironmentNames));
            }
        }

        [Fact]
        public async Task Annulation_par_option_ne_touche_pas_les_autres()
        {
            await Apply("a", SetReg("X", "DWord", "1"));
            await Apply("b", SetReg("Y", "DWord", "1"));
            await Undo("a");
            Assert.False(Existe("X"));
            Assert.Equal(1, Lire("Y"));
        }

        [Fact]
        public async Task Valeur_modifiee_entre_temps_signalee()
        {
            Ecrire("X", 5, RegistryValueKind.DWord);
            await Apply("a", SetReg("X", "DWord", "1"));
            Assert.Empty(UndoService.FindConflicts(Jrn, new[] { "a" }));
            Ecrire("X", 9, RegistryValueKind.DWord);
            var c = Assert.Single(UndoService.FindConflicts(Jrn, new[] { "a" }));
            Assert.Equal("a", c.ItemId);
            Assert.Equal("1", c.Attendu);
            Assert.Equal("9", c.Actuel);
        }

        [Fact]
        public async Task Journal_inaccessible_aucune_modification()
        {
            Ecrire("X", 5, RegistryValueKind.DWord);
            File.WriteAllText(Jrn, "");
            File.SetAttributes(Jrn, FileAttributes.ReadOnly);
            var r = await Apply("a", SetReg("X", "DWord", "1"));
            Assert.Equal(OutcomeStatus.Failed, r.Items[0].Status);
            Assert.Equal(5, Lire("X"));
        }

        [Fact]
        public async Task Ordre_des_options_inverse_de_la_premiere_apparition()
        {
            await Apply("a", SetReg("X", "DWord", "1"));
            await Apply("b", SetReg("Y", "DWord", "1"));
            var u = await Undo("a", "b");
            Assert.Equal(new[] { "b", "a" }, u.Items.Select(i => i.Id).ToArray());
        }

        [Fact]
        public async Task Hosts_ajout_et_annulation_sur_fichier_temporaire()
        {
            var hosts = Path.Combine(_tmp, "hosts");
            File.WriteAllText(hosts, "127.0.0.1 localhost\r\n");
            var apply = $"Add-TweakHosts -Domains 'a.example','b.example' -HostsPath '{hosts}'";
            Assert.Equal(OutcomeStatus.Ok, (await Apply("a", apply)).Items[0].Status);
            var txt = File.ReadAllText(hosts);
            Assert.Contains("a.example", txt);
            Assert.Contains("b.example", txt);
            Assert.Equal(OutcomeStatus.Ok, (await Undo("a")).Items[0].Status);
            Assert.Equal("127.0.0.1 localhost", File.ReadAllText(hosts).Trim());
        }

        [Fact]
        public async Task Quarantaine_deplace_puis_restaure()
        {
            var f = Path.Combine(_tmp, "vieux.txt");
            File.WriteAllText(f, "contenu");
            var q = Path.Combine(_tmp, "quarantaine");
            Assert.Equal(OutcomeStatus.Ok, (await Apply("a", $"Move-TweakQuarantine -File '{f}'", q)).Items[0].Status);
            Assert.False(File.Exists(f));
            Assert.Single(Directory.GetFiles(q));
            Assert.Equal(OutcomeStatus.Ok, (await Undo("a")).Items[0].Status);
            Assert.Equal("contenu", File.ReadAllText(f));
        }

        [Fact]
        public async Task Quarantaine_un_fichier_purge_n_empeche_pas_d_annuler_les_autres()
        {
            var f1 = Path.Combine(_tmp, "un.msi"); File.WriteAllText(f1, "1");
            var f2 = Path.Combine(_tmp, "deux.msi"); File.WriteAllText(f2, "2");
            var q = Path.Combine(_tmp, "quarantaine");
            Assert.Equal(OutcomeStatus.Ok, (await Apply("a", $"Move-TweakQuarantine -File '{f1}'; Move-TweakQuarantine -File '{f2}'", q)).Items[0].Status);
            // QuarantineService lit les journaux des dossiers de sessions : copie du journal dans une session, marquage, puis retour.
            var sessions = Path.Combine(_tmp, "Sessions"); var sess = Path.Combine(sessions, "20260101-100000"); Directory.CreateDirectory(sess);
            File.Copy(Jrn, Path.Combine(sess, "journal.jsonl"));
            var svc = new QuarantineService(q, sessions);
            svc.Purge(svc.List().Single(e => e.Nom == "un.msi").Id);
            File.Copy(Path.Combine(sess, "journal.jsonl"), Jrn, true);
            var u = await Undo("a");
            Assert.Equal(OutcomeStatus.Ok, u.Items[0].Status);
            Assert.Equal("2", File.ReadAllText(f2));
            Assert.False(File.Exists(f1));
        }

        [Fact]
        public async Task Journal_ligne_finale_tronquee_ignoree()
        {
            await Apply("a", SetReg("X", "DWord", "1"));
            File.AppendAllText(Jrn, "{\"item\":\"a\",\"kin");
            var e = Journal.Read(Jrn);
            Assert.Single(e);
            Assert.Equal("a", e[0].Item);
            Assert.Equal("registry", e[0].Kind);
            Assert.False(e[0].Existed);
            await Undo("a");
            Assert.False(Existe("X"));
        }

        [Fact]
        public void Journal_ligne_corrompue_au_milieu_signalee()
        {
            File.WriteAllText(Jrn, "{\"item\":\"a\",\"kind\":\"registry\",\"target\":\"t\",\"existed\":false}\r\nPAS DU JSON\r\n{\"item\":\"a\",\"kind\":\"registry\",\"target\":\"t\",\"existed\":false}\r\n");
            var ex = Assert.Throws<InvalidDataException>(() => Journal.Read(Jrn));
            Assert.Contains("ligne 2", ex.Message);
        }
    }
}
