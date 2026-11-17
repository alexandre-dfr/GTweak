using System;
using System.IO;
using System.Linq;
using GamingTweaker.Tests.Helpers;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>
    /// Réglages du pilote NVIDIA : journalisation et annulation, contre un faux GTNative.NvDrs (même signature, valeurs en mémoire).
    /// Les runners de test n'ont pas de GPU NVIDIA : l'appel réel à nvapi64.dll n'est pas exercé ici.
    /// </summary>
    public class NvidiaReglagesTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-nv-" + Guid.NewGuid().ToString("N"));
        public NvidiaReglagesTests() { Directory.CreateDirectory(tmp); }
        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        private const string Faux = @"
Add-Type -TypeDefinition @'
using System.Collections.Generic;
namespace GTNative {
  public static class NvDrs {
    public static Dictionary<uint, uint> Profil = new Dictionary<uint, uint>();
    public static Dictionary<uint, uint> Defaut = new Dictionary<uint, uint>();
    public static int Ecritures;
    public static long[] Lire(uint id) {
      uint v;
      if (Profil.TryGetValue(id, out v)) return new long[] { 1, v };
      if (Defaut.TryGetValue(id, out v)) return new long[] { 0, v };
      return new long[] { 0, -1 };
    }
    public static void Ecrire(uint id, uint v) { Profil[id] = v; Ecritures++; }
    public static void Retirer(uint id) { Profil.Remove(id); }
  }
}
'@
";

        private string Eval(string snippet)
        {
            var journal = Path.Combine(tmp, "journal.jsonl");
            return string.Join("\n", PsCommon.Eval(Faux + "$GT_JournalPath = '" + journal + "'\n$script:GT_CurrentItem = 'opt.nv-test'\n" + snippet));
        }

        private const string Annuler =
            "$e = @(Read-GTJournal); [array]::Reverse($e); foreach ($x in $e) { Restore-GTEntry $x }\n";

        [Fact]
        public void Valeur_definie_avant_puis_annulation_la_remet()
        {
            var r = Eval("[GTNative.NvDrs]::Profil[0x1057EB71] = 0\n"
                       + "Set-TweakNvSetting -Id 0x1057EB71 -Value 1\n"
                       + "Write-Output ([GTNative.NvDrs]::Profil[0x1057EB71])\n"
                       + Annuler
                       + "Write-Output ([GTNative.NvDrs]::Profil[0x1057EB71])");
            Assert.Equal("1\n0", r);
        }

        [Fact]
        public void Valeur_absente_du_profil_avant_annulation_la_retire()
        {
            var r = Eval("[GTNative.NvDrs]::Defaut[0x00A879CF] = 0x60925292\n"
                       + "Set-TweakNvSetting -Id 0x00A879CF -Value 0x08416747\n"
                       + "Write-Output (Get-GTNvState @(,@(0x00A879CF, 0x08416747)))\n"
                       + Annuler
                       + "Write-Output ([GTNative.NvDrs]::Profil.ContainsKey(0x00A879CF))\n"
                       + "Write-Output (Get-GTNvState @(,@(0x00A879CF, 0x08416747)))");
            Assert.Equal("Applied\nFalse\nNotApplied", r);
        }

        [Fact]
        public void Valeur_deja_en_place_rien_n_est_ecrit_ni_journalise()
        {
            var r = Eval("[GTNative.NvDrs]::Profil[0x00CE2691] = 0x14\n"
                       + "Set-TweakNvSetting -Id 0x00CE2691 -Value 0x14\n"
                       + "Write-Output ([GTNative.NvDrs]::Ecritures)\n"
                       + "Write-Output (@(Read-GTJournal).Count)");
            Assert.Equal("0\n0", r);
        }

        [Fact]
        public void Valeur_sur_32_bits_complets_conservee()
        {
            var r = Eval("[GTNative.NvDrs]::Profil[0x00AC8497] = 4096\n"
                       + "Set-TweakNvSetting -Id 0x00AC8497 -Value 4294967295\n"
                       + "Write-Output ([GTNative.NvDrs]::Profil[0x00AC8497])\n"
                       + "Write-Output (Get-GTNvState @(,@(0x00AC8497, 4294967295)))\n"
                       + Annuler
                       + "Write-Output ([GTNative.NvDrs]::Profil[0x00AC8497])");
            Assert.Equal("4294967295\nApplied\n4096", r);
        }

        [Fact]
        public void Plusieurs_reglages_d_une_option_et_annulation_complete()
        {
            var apply = File.ReadAllText(Path.Combine(PsCommon.Racine(), "src", "GamingTweaker", "Assets", "scripts", "opt.nv-latency-ultra.apply.ps1"));
            var detect = File.ReadAllText(Path.Combine(PsCommon.Racine(), "src", "GamingTweaker", "Assets", "scripts", "opt.nv-latency-ultra.detect.ps1"));
            var r = Eval(apply + "\nWrite-Output (& { " + detect + " })\n" + Annuler
                       + "Write-Output ([GTNative.NvDrs]::Profil.Count)\nWrite-Output (& { " + detect + " })");
            Assert.Equal("Applied\n0\nNotApplied", r);
        }

        [Fact]
        public void Annulation_sans_journal_retire_les_reglages()
        {
            var revert = File.ReadAllText(Path.Combine(PsCommon.Racine(), "src", "GamingTweaker", "Assets", "scripts", "opt.nv-vsync-off.revert.ps1"));
            var r = Eval("[GTNative.NvDrs]::Profil[0x00A879CF] = 0x08416747\n" + revert + "\nWrite-Output ([GTNative.NvDrs]::Profil.Count)");
            Assert.Equal("0", r);
        }

        [Fact]
        public void Identifiant_hexadecimal_du_journal()
        {
            Assert.Equal("0x00A879CF", string.Join("", PsCommon.Eval("Format-GTNvId 0x00A879CF")));
            Assert.Equal("11041231", string.Join("", PsCommon.Eval("ConvertFrom-GTNvId '0x00A879CF'")));
        }

        [Fact]
        public void Detection_inconnue_sans_pilote_nvidia()
        {
            // Sans le faux type et sans nvapi64.dll sur le runner : la détection répond Unknown, sans erreur.
            if (File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvapi64.dll"))) return;
            var detect = File.ReadAllText(Path.Combine(PsCommon.Racine(), "src", "GamingTweaker", "Assets", "scripts", "opt.nv-power-max.detect.ps1"));
            Assert.Equal("Unknown", string.Join("", PsCommon.Eval(detect)));
        }
    }
}
