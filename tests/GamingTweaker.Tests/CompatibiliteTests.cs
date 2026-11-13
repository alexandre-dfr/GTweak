using System;
using System.Collections.Generic;
using GamingTweaker.Core;
using Xunit;

namespace GamingTweaker.Tests
{
    public class CompatibiliteTests
    {
        private static MachineFacts Facts(string win, string ed, string cpu, params string[] gpus) => new MachineFacts
        {
            WindowsMajor = win, Edition = ed, CpuVendor = cpu, GpuVendors = new List<string>(gpus)
        };

        private static readonly MachineFacts Pro11Intel = Facts("11", "Pro", "Intel", "Intel");
        private static readonly MachineFacts Pro10Intel = Facts("10", "Pro", "Intel", "Intel");
        private static MachineFacts FactsAvecGpu(params string[] gpus) => Facts("11", "Pro", "Intel", gpus);

        [Fact] public void Rule_vide_accepte_tout() => Assert.True(Compatibilite.IsCompatible(new CompatRule(), Pro11Intel));
        [Fact] public void Windows_incompatible_refuse() => Assert.False(Compatibilite.IsCompatible(new CompatRule { Windows = { "11" } }, Pro10Intel));
        [Fact] public void Gpu_accepte_si_un_des_gpu_correspond() => Assert.True(Compatibilite.IsCompatible(new CompatRule { Gpu = { "NVIDIA" } }, FactsAvecGpu("Intel", "NVIDIA")));

        [Fact] public void Gpu_refuse_si_aucun_ne_correspond() => Assert.False(Compatibilite.IsCompatible(new CompatRule { Gpu = { "NVIDIA" } }, FactsAvecGpu("Intel", "AMD")));
        [Fact] public void Gpu_refuse_si_machine_sans_gpu() => Assert.False(Compatibilite.IsCompatible(new CompatRule { Gpu = { "NVIDIA" } }, FactsAvecGpu()));
        [Fact] public void Comparaison_insensible_a_la_casse() => Assert.True(Compatibilite.IsCompatible(new CompatRule { Editions = { "pro" }, Cpu = { "INTEL" } }, Pro11Intel));
        [Fact] public void Edition_incompatible_refuse() => Assert.False(Compatibilite.IsCompatible(new CompatRule { Editions = { "Home" } }, Pro11Intel));
        [Fact] public void Cpu_incompatible_refuse() => Assert.False(Compatibilite.IsCompatible(new CompatRule { Cpu = { "AMD" } }, Pro11Intel));
        [Fact] public void Toutes_les_conditions_doivent_etre_remplies() =>
            Assert.False(Compatibilite.IsCompatible(new CompatRule { Windows = { "11" }, Cpu = { "AMD" } }, Pro11Intel));

        [Theory]
        [InlineData("19045", "10")]
        [InlineData("21999", "10")]
        [InlineData("22000", "11")]
        [InlineData("26200", "11")]
        [InlineData("n/a", "")]
        [InlineData(null, "")]
        public void WindowsMajor_depuis_build(string build, string attendu) => Assert.Equal(attendu, SystemInfo.WindowsMajorFromBuild(build));

        [Theory]
        [InlineData("Core", "Home")]
        [InlineData("CoreSingleLanguage", "Home")]
        [InlineData("Professional", "Pro")]
        [InlineData("ProfessionalWorkstation", "Pro")]
        [InlineData("Enterprise", "Enterprise")]
        [InlineData("EnterpriseS", "Enterprise")]
        [InlineData("Education", "Education")]
        [InlineData("ProfessionalEducation", "Education")]
        [InlineData("ServerStandard", "")]
        [InlineData(null, "")]
        public void Edition_normalisee(string id, string attendu) => Assert.Equal(attendu, SystemInfo.NormalizeEdition(id));

        [Theory]
        [InlineData("Intel(R) Core(TM) i7-12700K", "Intel")]
        [InlineData("AMD Ryzen 7 5800X 8-Core Processor", "AMD")]
        [InlineData("NVIDIA GeForce RTX 3070", "NVIDIA")]
        [InlineData("AMD Radeon RX 6800", "AMD")]
        [InlineData("Intel(R) UHD Graphics 770", "Intel")]
        [InlineData("Microsoft Basic Display Adapter", "")]
        [InlineData(null, "")]
        public void Vendeur_normalise(string nom, string attendu) => Assert.Equal(attendu, SystemInfo.NormalizeVendor(nom));

        [Theory]
        [InlineData(new int[] { 3 }, false, false)]      // Desktop
        [InlineData(new int[] { 3 }, true, true)]        // Desktop avec batterie (onduleur ou mini-PC) : prudence
        [InlineData(new int[] { 10 }, false, true)]      // Notebook
        [InlineData(new int[] { 31 }, false, true)]      // Convertible
        [InlineData(new int[0], false, false)]
        public void Portable_depuis_le_chassis_ou_la_batterie(int[] chassis, bool batterie, bool attendu)
        {
            Assert.Equal(attendu, SystemInfo.EstPortable(chassis, batterie));
        }

        [Fact]
        public void Le_verrou_d_instance_a_une_DACL_explicite()
        {
            Assert.NotNull(SingleInstance.Securite());
        }

        [Fact]
        public void Le_journal_applicatif_ecrit_les_lignes_gardees_en_memoire_une_fois_configure()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gt-trace-" + Guid.NewGuid().ToString("N"));
            try
            {
                TraceAppli.Ecrire("avant configuration");
                TraceAppli.Configurer(dir);
                TraceAppli.Erreur("contexte", new InvalidOperationException("ligne 1\r\nligne 2"));
                var texte = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, TraceAppli.NomFichier));
                Assert.Contains("démarrage GamingTweaker", texte);
                Assert.Contains("avant configuration", texte);
                Assert.Contains("contexte : InvalidOperationException : ligne 1 ligne 2", texte);
                Assert.DoesNotContain("ligne 1\r\nligne 2", texte);
            }
            finally { TraceAppli.Reinitialiser(); try { System.IO.Directory.Delete(dir, true); } catch (System.IO.IOException) { } }
        }
    }
}
