using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using GamingTweaker.Lists;
using GamingTweaker.Tests.Helpers;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>opt.tasks-tierces : tâches planifiées des applications (fournisseur « taches », règles partagées avec Common.ps1).</summary>
    public class TachesTiercesTests
    {
        private sealed class Source : ITachesSource
        {
            public readonly List<TacheBrute> Donnees = new List<TacheBrute>();
            public Source Ajouter(string chemin, string etat = "Ready", string prog = null) { Donnees.Add(new TacheBrute { Chemin = chemin, Etat = etat, Programme = prog }); return this; }
            public Task<IReadOnlyList<TacheBrute>> LireAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<TacheBrute>>(Donnees);
        }

        private static IReadOnlyList<ListEntry> Charger(Source s) => new TachesProvider(s).LoadAsync(CancellationToken.None).GetAwaiter().GetResult();

        public static IEnumerable<object[]> Chemins() => new[]
        {
            new object[] { @"\Google\GoogleUpdateTaskMachineUA{A1B2}", "Google:GoogleUpdateTaskMachineUA(A1B2)" },
            new object[] { @"\OneDrive Reporting Task-S-1-5-21-1", "OneDrive Reporting Task-S-1-5-21-1" },
            new object[] { @"\NVIDIA\NvTmRep [x]=y", "NVIDIA:NvTmRep (x)_y" },
            new object[] { @"\Adobe Acrobat Update Task", "Adobe Acrobat Update Task" },
            new object[] { @"\_cachee", null },
            new object[] { "", null },
        };

        [Theory]
        [MemberData(nameof(Chemins))]
        public void La_cle_est_acceptee_par_les_profils(string chemin, string attendu)
        {
            var cle = TachesTierces.Cle(chemin);
            Assert.Equal(attendu, cle);
            if (cle != null) Assert.True(Profils.ElementValide(cle), cle);
        }

        [Fact]
        public void Les_cles_sont_identiques_en_CSharp_et_en_PowerShell()
        {
            var chemins = Chemins().Select(c => (string)c[0]).Where(c => c != "").ToList();
            var ps = PsCommon.Eval(string.Join("\r\n", chemins.Select(c => "$k = Get-GTTaskKey '" + c.Replace("'", "''") + "'; if ($k) { $k } else { '<null>' }")));
            Assert.Equal(chemins.Select(c => TachesTierces.Cle(c) ?? "<null>"), ps);
            var tierces = PsCommon.Eval(string.Join("\r\n", new[] { @"\Microsoft\Windows\Defrag\ScheduledDefrag", @"\GamingTweaker\Routine", @"\Avast Software\Overseer", @"\Google\GoogleUpdateTaskMachineUA" }
                .Select(c => "[string](Test-GTTaskTierce '" + c + "')")));
            Assert.Equal(new[] { "False", "False", "False", "True" }, tierces);
        }

        [Fact]
        public void Windows_GamingTweaker_et_les_antivirus_ne_sont_jamais_proposes()
        {
            var s = new Source().Ajouter(@"\Microsoft\Windows\Defrag\ScheduledDefrag").Ajouter(@"\GamingTweaker\Routine")
                .Ajouter(@"\Avast Software\Overseer").Ajouter(@"\McAfee Update").Ajouter(@"\Adobe Acrobat Update Task", prog: "AdobeARM.exe");
            var l = Charger(s);
            Assert.Equal(new[] { "Adobe Acrobat Update Task" }, l.Select(e => e.Key));
            Assert.Equal(RiskLevel.Avance, l[0].Risque);
            Assert.Contains("AdobeARM.exe", l[0].Detail);
        }

        [Fact]
        public void Les_mises_a_jour_des_navigateurs_sont_risquees_et_listees_en_dernier()
        {
            var s = new Source().Ajouter(@"\MicrosoftEdgeUpdateTaskMachineCore{1}").Ajouter(@"\Zeta Tache", "Disabled").Ajouter(@"\Alpha Tache");
            var l = Charger(s);
            Assert.Equal(new[] { "Alpha Tache", "Zeta Tache", "MicrosoftEdgeUpdateTaskMachineCore(1)" }, l.Select(e => e.Key));
            Assert.Equal(RiskLevel.Risque, l[2].Risque);
            Assert.Contains("correctifs de sécurité", l[2].Detail);
            Assert.EndsWith(": désactivée", l[1].Detail);
        }

        [Fact]
        public void La_sortie_json_de_powershell_est_analysee()
        {
            Assert.Empty(PowerShellTachesSource.Analyser(""));
            var un = PowerShellTachesSource.Analyser("{\"Chemin\":\"\\\\A\\\\B\",\"Etat\":\"Ready\",\"Programme\":\"b.exe\"}");
            Assert.Equal(@"\A\B", un.Single().Chemin);
            var deux = PowerShellTachesSource.Analyser("[{\"Chemin\":\"\\\\X\",\"Etat\":\"Disabled\",\"Programme\":null},{\"Chemin\":\"\\\\Y\"}]");
            Assert.Equal(new[] { @"\X", @"\Y" }, deux.Select(t => t.Chemin));
        }

        [Fact]
        public void L_option_refuse_tout_et_ne_passe_que_par_les_fonctions_journalisees()
        {
            var it = Catalogue.LoadEmbedded().Single(i => i.Id == "opt.tasks-tierces");
            Assert.Equal("taches", it.Liste);
            Assert.Equal(RiskLevel.Avance, it.Risque);
            Assert.False(it.Hkcu);
            Assert.Contains("-contains '*'", it.Apply);
            Assert.Contains("Set-TweakTaskIfNeeded", it.Apply);
            Assert.Contains("Get-GTThirdPartyTasks", it.Apply);
            Assert.Contains("Get-GTThirdPartyTasksState", it.Detect);
            Assert.False(Profils.CleSupporteTous("taches"));
            Assert.Empty(PsSyntax.CheckAll(new[] { it }));
        }

        [Fact]
        public void La_lecture_reelle_des_taches_ne_renvoie_que_des_cles_valides()
        {
            foreach (var e in new TachesProvider().LoadAsync(CancellationToken.None).GetAwaiter().GetResult())
            {
                Assert.True(Profils.ElementValide(e.Key), e.Key);
                Assert.False(e.Key.StartsWith("Microsoft:", System.StringComparison.OrdinalIgnoreCase), e.Key);
            }
        }
    }
}
