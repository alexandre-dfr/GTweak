using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using GamingTweaker.Ui;
using Xunit;

namespace GamingTweaker.Tests
{
    public class MesuresJeuTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-jeu-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        private static ReleveCompteur R(string p, double? fps, double? bas = null, double? cpu = null, double? gpu = null)
        {
            return new ReleveCompteur
            {
                Processus = p,
                Images = fps.HasValue ? new MesureImages { Fps = fps.Value, TempsImageMs = 1000 / fps.Value, UnPourcentBas = bas, Source = "DirectX" } : null,
                Charge = new ChargeSysteme { Cpu = cpu, Gpu = gpu },
            };
        }

        [Fact]
        public void Agreger_garde_le_jeu_le_plus_present_et_fait_les_moyennes()
        {
            var r = new List<ReleveCompteur>
            {
                R("jeu", 100, 80, 30, 90), R("jeu", 120, 60, 50, 95), R("GamingTweaker", 60, 60), R("jeu", 110, 70, 40, 92),
            };
            var m = MesuresJeu.Agreger(r, "2026-11-24T20:00:00", 4);
            Assert.Equal("jeu", m.Jeu);
            Assert.Equal(110, m.FpsMoyen);
            Assert.Equal(70, m.UnPourcentBas);
            Assert.Equal(60, m.PireBas);
            Assert.Equal(40, m.Cpu);
            Assert.Equal(92.3, m.Gpu);
            Assert.Equal("DirectX", m.Source);
            Assert.Equal(0.75, m.Couverture);
            Assert.Contains("75 %", m.Note);           // sous 80 % : signalée incomplète
            Assert.Contains("(incomplète)", m.Libelle);
        }

        [Fact]
        public void Agreger_sans_image_donne_une_mesure_vide_expliquee()
        {
            var m = MesuresJeu.Agreger(new List<ReleveCompteur> { R("explorer", null), null }, "2026-11-24T20:00:00", 2);
            Assert.Null(m.FpsMoyen);
            Assert.Equal("", m.Jeu);
            Assert.Contains("aucun jeu", m.Note);
            Assert.Contains("aucune image", m.Libelle);
        }

        [Fact]
        public void Comparer_donne_les_ecarts_et_le_verdict()
        {
            var a = new MesureJeu { Date = "a", Jeu = "jeu", FpsMoyen = 100, UnPourcentBas = 60, PireBas = 40, Couverture = 1 };
            var b = new MesureJeu { Date = "b", Jeu = "jeu", FpsMoyen = 101, UnPourcentBas = 72, PireBas = 30, Couverture = 1 };
            var t = MesuresJeu.Comparer(a, b);
            Assert.Contains("FPS moyens : 100 → 101 (+1 % : stable)", t);
            Assert.Contains("1 % bas (fluidité) : 60 → 72 (+20 % : mieux)", t);
            Assert.Contains("Pire 1 % bas (saccades) : 40 → 30 (-25 % : moins bien)", t);
            Assert.Contains("Bilan : 1 mesure(s) en progrès, 1 en recul", t);
            Assert.DoesNotContain("pas les mêmes jeux", t);
        }

        [Fact]
        public void Comparer_deux_jeux_differents_avertit()
        {
            var t = MesuresJeu.Comparer(new MesureJeu { Jeu = "a", FpsMoyen = 60, Couverture = 1 }, new MesureJeu { Jeu = "b", FpsMoyen = 90, Couverture = 1 });
            Assert.Contains("pas les mêmes jeux", t);
        }

        [Fact]
        public void Enregistrement_et_relecture()
        {
            MesuresJeu.Save(new MesureJeu { Date = "2026-11-24T20:00:00", Jeu = "jeu", FpsMoyen = 100 }, tmp);
            MesuresJeu.Save(new MesureJeu { Date = "2026-11-24T21:00:00", Jeu = "jeu", FpsMoyen = 110 }, tmp);
            File.WriteAllText(Path.Combine(tmp, MesuresJeu.Dossier, "casse.json"), "{pas du json");
            var l = MesuresJeu.List(tmp);
            Assert.Equal(new double?[] { 110, 100 }, l.Select(m => m.FpsMoyen));
        }

        private sealed class Source : ISourceCompteur
        {
            public int Demarrages, Arrets;
            public void Demarrer() { Demarrages++; }
            public void Arreter() { Arrets++; }
            public void Choisir(bool cpu, bool gpu, bool ram) { }
            public void ChoisirTemperatures(bool cpu, bool gpu) { }
            public ReleveCompteur Lire() { return R("jeu", 144, 100); }
            public string Erreur { get { return null; } }
        }

        [Fact]
        public async Task EnregistrerAsync_releve_puis_libere_la_source()
        {
            var s = new Source();
            var m = await MesuresJeu.EnregistrerAsync(s, TimeSpan.Zero, TimeSpan.FromSeconds(2), null, CancellationToken.None);
            Assert.Equal(1, s.Demarrages); Assert.Equal(1, s.Arrets);
            Assert.Equal("jeu", m.Jeu);
            Assert.Equal(144, m.FpsMoyen);
        }

        [Fact]
        public async Task EnregistrerAsync_annule_libere_aussi_la_source()
        {
            var s = new Source();
            var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MesuresJeu.EnregistrerAsync(s, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), null, cts.Token));
            Assert.Equal(s.Demarrages, s.Arrets);
        }

        [Fact]
        public async Task Historique_mesure_enregistre_et_compare_les_parties_du_meme_jeu()
        {
            var paths = new DataPaths { ProfilsDir = Path.Combine(tmp, "P"), MachineDir = Path.Combine(tmp, "M"), SessionsDir = Path.Combine(tmp, "M", "Sessions"), QuarantineDir = Path.Combine(tmp, "Q") };
            var vm = new HistoriqueViewModel(paths, null, new List<CatalogItem>(), new QuarantineService(paths.QuarantineDir, paths.SessionsDir));
            Assert.False(vm.MesurerPartieCommand.CanExecute(null));   // pas de source : indisponible
            vm.SourceJeu = new Source();
            var fps = new Queue<double>(new double[] { 100, 130 });
            int n = 0;
            vm.EnregistrerPartie = (src, a, d, p, ct) =>
            {
                var f = fps.Dequeue();
                return Task.FromResult(new MesureJeu { Date = "2026-11-24T2" + (n++) + ":00:00", Jeu = "jeu", FpsMoyen = f, UnPourcentBas = f / 2, Couverture = 1 });
            };
            vm.ComparerParties();
            Assert.Contains("Mesurez deux parties", vm.Comparaison);
            await vm.MesurerPartieAsync();
            Assert.Contains("Partie mesurée", vm.Message);
            await vm.MesurerPartieAsync();
            Assert.Equal(2, vm.Parties.Count);
            vm.ComparerParties();
            Assert.Contains("FPS moyens : 100 → 130 (+30 % : mieux)", vm.Comparaison);
        }
    }
}
