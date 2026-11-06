using System;
using System.Collections.Generic;
using System.Linq;
using GamingTweaker.Info;
using Xunit;

namespace GamingTweaker.Tests
{
    public class InfoTests
    {
        private static readonly Lazy<InfoReport> Rapport = new Lazy<InfoReport>(() => InfoCollector.Collect());

        [Fact]
        public void Toutes_les_sections_sont_presentes()
        {
            foreach (var s in new[] { "Windows", "CPU", "Carte mère", "RAM", "GPU", "Disques", "Réseau", "Écrans", "Pilotes" })
                Assert.Contains(Rapport.Value.Sections, x => x.Titre == s);
        }

        [Fact]
        public void Export_texte_contient_les_sections() => Assert.Contains("CPU", Rapport.Value.ToText());

        [Fact]
        public void Collecte_ne_charge_pas_librehardwaremonitor()
        {
            var _ = Rapport.Value;
            Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a => a.FullName.StartsWith("LibreHardwareMonitor"));
        }

        [Fact]
        public void Une_section_en_erreur_affiche_indisponible_sans_arreter_les_autres()
        {
            var r = InfoCollector.Build(new List<KeyValuePair<string, Func<List<KeyValuePair<string, string>>>>>
            {
                new KeyValuePair<string, Func<List<KeyValuePair<string, string>>>>("A", () => { throw new InvalidOperationException("boum"); }),
                new KeyValuePair<string, Func<List<KeyValuePair<string, string>>>>("B", () => new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("x", "1") }),
            });
            Assert.Equal(2, r.Sections.Count);
            Assert.Contains("indisponible", r.Sections[0].Lignes[0].Value);
            Assert.Contains("boum", r.Sections[0].Lignes[0].Value);
            Assert.Equal("1", r.Sections[1].Lignes[0].Value);
        }

        [Fact]
        public void ToText_formate_titres_et_lignes()
        {
            var r = new InfoReport();
            r.Sections.Add(new InfoSection { Titre = "CPU", Lignes = { new KeyValuePair<string, string>("Nom", "Ryzen") } });
            var t = r.ToText();
            Assert.Contains("CPU", t); Assert.Contains("Nom : Ryzen", t);
        }

        [Fact]
        public void PawnIo_IsInstalled_ne_leve_pas() { var _ = PawnIoInstaller.IsInstalled(); }

        [Fact]
        public void Format_capteur_gere_les_unites()
        {
            Assert.Equal("65,0 °C", new SensorReading { Type = "Temperature", Value = 65f }.Affichage);
            Assert.Equal("--", new SensorReading { Type = "Load", Value = null }.Affichage);
        }
    }
}
