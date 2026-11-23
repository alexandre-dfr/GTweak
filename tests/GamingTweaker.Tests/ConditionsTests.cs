using System;
using System.Collections.Generic;
using System.Linq;
using GamingTweaker.Core;
using Microsoft.Win32;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>Conditions de pertinence (build minimale, RAM, service ou logiciel présent) : une option inutile sur ce PC n'est pas affichée.</summary>
    public class ConditionsTests
    {
        private static MachineFacts Facts(int build = 26100, int ram = 16, Func<string, bool> service = null, Func<string, bool> cle = null) => new MachineFacts
        {
            WindowsMajor = "11", WindowsBuild = build, Edition = "Pro", CpuVendor = "AMD", GpuVendors = new List<string> { "NVIDIA" },
            RamGo = ram, ServiceExiste = service, CleExiste = cle,
        };

        [Theory]
        [InlineData(26100, true)]
        [InlineData(26200, true)]
        [InlineData(22631, false)]
        [InlineData(0, true)]   // build inconnue : l'option reste proposée
        public void Build_minimale(int build, bool attendu) =>
            Assert.Equal(attendu, Compatibilite.IsCompatible(new CompatRule { BuildMin = 26100 }, Facts(build: build)));

        [Theory]
        [InlineData(16, true)]
        [InlineData(8, true)]
        [InlineData(6, false)]
        [InlineData(0, true)]   // RAM inconnue : l'option reste proposée
        public void Ram_minimale(int ram, bool attendu) =>
            Assert.Equal(attendu, Compatibilite.IsCompatible(new CompatRule { RamMinGo = 8 }, Facts(ram: ram)));

        [Fact]
        public void Service_present_si_au_moins_un_existe()
        {
            var regle = new CompatRule { Services = { "DiagTrack", "dmwappushservice" } };
            Assert.True(Compatibilite.IsCompatible(regle, Facts(service: n => n == "dmwappushservice")));
            Assert.False(Compatibilite.IsCompatible(regle, Facts(service: n => false)));
            Assert.True(Compatibilite.IsCompatible(regle, Facts(service: null)));
        }

        [Fact]
        public void Logiciel_installe_si_au_moins_une_cle_existe()
        {
            var regle = new CompatRule { Cles = { @"HKLM\SOFTWARE\BraveSoftware", @"HKCU\Software\BraveSoftware" } };
            Assert.True(Compatibilite.IsCompatible(regle, Facts(cle: c => c.StartsWith("HKCU", StringComparison.Ordinal))));
            Assert.False(Compatibilite.IsCompatible(regle, Facts(cle: c => false)));
            Assert.True(Compatibilite.IsCompatible(regle, Facts(cle: c => { throw new UnauthorizedAccessException(); })));   // test en échec : proposée
        }

        [Fact]
        public void Le_cycle_reel_ignore_les_conditions_de_pertinence_mais_pas_la_version()
        {
            var f = Compatibilite.SansPertinence(Facts(build: 22631, ram: 4, service: n => false, cle: c => false));
            Assert.True(Compatibilite.IsCompatible(new CompatRule { RamMinGo = 8, Services = { "X" }, Cles = { @"HKLM\X" } }, f));
            Assert.False(Compatibilite.IsCompatible(new CompatRule { BuildMin = 26100 }, f));
            Assert.False(Compatibilite.IsCompatible(new CompatRule { Gpu = { "AMD" } }, f));
        }

        [Theory]
        [InlineData(@"HKLM\SOFTWARE\X", RegistryHive.LocalMachine, @"SOFTWARE\X")]
        [InlineData(@"HKEY_CURRENT_USER\Software\X", RegistryHive.CurrentUser, @"Software\X")]
        [InlineData(@"hkcu\Software", RegistryHive.CurrentUser, "Software")]
        public void Ruche_reconnue(string chemin, RegistryHive ruche, string sous)
        {
            string s;
            Assert.Equal(ruche, SystemInfo.Ruche(chemin, out s));
            Assert.Equal(sous, s);
        }

        [Theory]
        [InlineData(@"HKCR\X")]
        [InlineData("HKLM")]
        [InlineData(@"HKLM\")]
        [InlineData("")]
        [InlineData(null)]
        public void Ruche_refusee(string chemin)
        {
            string s;
            Assert.Null(SystemInfo.Ruche(chemin, out s));
            Assert.False(SystemInfo.CleExiste(chemin));
        }

        [Fact]
        public void Existence_reelle_d_une_cle_et_d_un_service()
        {
            Assert.True(SystemInfo.CleExiste(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion"));
            Assert.False(SystemInfo.CleExiste(@"HKCU\Software\GamingTweakerTest\Absente-" + Guid.NewGuid().ToString("N")));
            Assert.True(SystemInfo.ServiceExiste("EventLog"));
            Assert.False(SystemInfo.ServiceExiste("GtServiceInexistant" + Guid.NewGuid().ToString("N")));
            Assert.False(SystemInfo.ServiceExiste(@"EventLog\..\X"));
        }

        [Theory]
        [InlineData(17052127232L, 16)]   // 15,9 Go visibles pour 16 Go installés
        [InlineData(8589934592L, 8)]
        [InlineData(0L, 0)]
        [InlineData(-1L, 0)]
        public void Arrondi_de_la_memoire(long octets, int go) => Assert.Equal(go, SystemInfo.ArrondirGo(octets));

        [Fact]
        public void Les_conditions_du_catalogue_sont_lues_depuis_le_json()
        {
            var cat = Catalogue.LoadEmbedded().ToDictionary(i => i.Id);
            Assert.Equal(26100, cat["tel.recall-off"].Compatibilite.BuildMin);
            Assert.Equal(26100, cat["tel.clicktodo-off"].Compatibilite.BuildMin);
            Assert.Equal(8, cat["opt.sysmain"].Compatibilite.RamMinGo);
            Assert.Contains("SysMain", cat["opt.sysmain"].Compatibilite.Services);
            Assert.Contains("DiagTrack", cat["tel.diagtrack-off"].Compatibilite.Services);
            foreach (var id in new[] { "tel.office-off", "tel.brave-debloat", "tel.browsers-telemetry-off", "cln.launchers" })
            {
                Assert.NotEmpty(cat[id].Compatibilite.Cles);
                string s;
                Assert.All(cat[id].Compatibilite.Cles, c => Assert.NotNull(SystemInfo.Ruche(c, out s)));
                Assert.Contains("Affiché seulement si le logiciel concerné est installé", cat[id].Description);
            }
            // Une option sans condition de pertinence n'en a pas reçu par défaut.
            Assert.Null(cat["opt.hags"].Compatibilite.BuildMin);
            Assert.Empty(cat["opt.hags"].Compatibilite.Cles);
        }

        [Fact]
        public void Une_option_d_un_logiciel_absent_n_est_pas_affichee()
        {
            var brave = new CatalogItem { Id = "tel.brave-x", Menu = "Télémétrie", Groupe = "G", Titre = "T", Risque = RiskLevel.Sur, Apply = "a", Detect = "d",
                                          Compatibilite = new CompatRule { Cles = { @"HKLM\SOFTWARE\BraveSoftware" } } };
            var autre = new CatalogItem { Id = "tel.autre", Menu = "Télémétrie", Groupe = "G", Titre = "T", Risque = RiskLevel.Sur, Apply = "a", Detect = "d" };
            var paths = new DataPaths { ProfilsDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gt-cond-" + Guid.NewGuid().ToString("N")) };
            var vm = new GamingTweaker.Ui.MainViewModel(new List<CatalogItem> { brave, autre }, Facts(cle: c => false), paths,
                _ => System.Threading.Tasks.Task.FromResult(new Dictionary<string, DetectState>()), null, _ => true);
            Assert.Equal(new[] { "tel.autre" }, vm.Options.Select(o => o.Id));
        }
    }
}
