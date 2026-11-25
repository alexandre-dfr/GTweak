using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceProcess;
using GamingTweaker.Core;
using GamingTweaker.Ui;
using Xunit;

namespace GamingTweaker.Tests
{
    public class AntiTricheTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-ac-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        private static Func<string, ServiceControllerStatus?> Etats(Dictionary<string, ServiceControllerStatus> e)
        {
            return n => e.TryGetValue(n, out var s) ? s : (ServiceControllerStatus?)null;
        }

        [Fact]
        public void Aucun_service_d_anti_triche_donne_null()
        {
            Assert.Null(AntiTriche.Actif(Etats(new Dictionary<string, ServiceControllerStatus>())));
        }

        [Fact]
        public void Service_installe_mais_arrete_n_est_pas_actif()
        {
            Assert.Null(AntiTriche.Actif(Etats(new Dictionary<string, ServiceControllerStatus> { { "vgc", ServiceControllerStatus.Stopped } })));
        }

        [Theory]
        [InlineData("vgc", "Riot Vanguard")]
        [InlineData("FACEIT", "FACEIT")]
        [InlineData("EasyAntiCheat_EOS", "Easy Anti-Cheat")]
        [InlineData("BEService", "BattlEye")]
        [InlineData("EAAntiCheatService", "EA Javelin")]
        public void Service_en_cours_est_reconnu(string service, string nom)
        {
            Assert.Equal(nom, AntiTriche.Actif(Etats(new Dictionary<string, ServiceControllerStatus> { { service, ServiceControllerStatus.Running } })));
        }

        [Fact]
        public void Erreur_de_lecture_d_un_service_est_ignoree()
        {
            Assert.Null(AntiTriche.Actif(n => { throw new InvalidOperationException("refusé"); }));
        }

        [Fact]
        public void Compteur_affiche_AC_quand_la_temperature_processeur_est_suspendue()
        {
            var vm = new CompteurViewModel(tmp, new SourceVide(), new CompteurConfig { Actif = true });
            vm.TempCpu = true;
            vm.Rafraichir(new ReleveCompteur { Charge = new ChargeSysteme { Cpu = 20, TempCpu = null, AntiTricheCpu = "Riot Vanguard" } });
            Assert.Equal("CPU  20 %   AC °C", vm.TexteCpu);
        }

        private sealed class SourceVide : ISourceCompteur
        {
            public void Demarrer() { }
            public void Arreter() { }
            public void Choisir(bool cpu, bool gpu, bool ram) { }
            public void ChoisirTemperatures(bool cpu, bool gpu) { }
            public ReleveCompteur Lire() { return null; }
            public string Erreur { get { return null; } }
        }
    }
}
