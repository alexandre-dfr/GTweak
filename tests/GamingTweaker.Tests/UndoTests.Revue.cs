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
    /// <summary>Corrections issues de la revue de la Task 4 (énergie, BCD, marqueurs d'annulation, journal tronqué).</summary>
    public partial class UndoTests
    {
        private async Task<List<string>> PsEval(string apply)
        {
            var log = new List<string>();
            var script = ScriptBuilder.Build(new[] { Item("t", apply) }, Sel("t"), Jrn);
            var r = await Runner.RunAsync(script, l => { lock (log) log.Add(l); }, CancellationToken.None, id => 120);
            Assert.Equal(OutcomeStatus.Ok, r.Items[0].Status);
            return log;
        }

        private const string PowerEn =
            "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)\r\n" +
            "  Subgroup GUID: 54533251-82be-4824-96c1-47b60b740d00  (Processor power management)\r\n" +
            "    Power Setting GUID: 893dee8e-2bef-41e0-89c6-b55d0929964c  (Minimum processor state)\r\n" +
            "      Minimum Possible Setting: 0x00000000\r\n      Maximum Possible Setting: 0x00000064\r\n" +
            "      Possible Settings increment: 0x00000001\r\n      Possible Settings units: %\r\n" +
            "    Current AC Power Setting Index: 0x00000064\r\n    Current DC Power Setting Index: 0x00000032\r\n";

        private const string PowerFr =
            "GUID du mode de gestion de l’alimentation : 381b4222-f694-41f0-9685-ff5bb260df2e  (Utilisation normale)\r\n" +
            "  GUID du sous-groupe : 54533251-82be-4824-96c1-47b60b740d00  (Gestion de l’alimentation du processeur)\r\n" +
            "    GUID du paramètre d’alimentation : 893dee8e-2bef-41e0-89c6-b55d0929964c  (État minimal du processeur)\r\n" +
            "      Valeur minimale possible : 0x00000000\r\n      Valeur maximale possible : 0x00000064\r\n" +
            "      Incrément possible : 0x00000001\r\n      Unités : %\r\n" +
            "    Index du paramètre d’alimentation secteur actuel : 0x00000064\r\n" +
            "    Index du paramètre d’alimentation sur batterie actuel : 0x00000032\r\n";

        [Theory]
        [InlineData(PowerEn)]
        [InlineData(PowerFr)]
        public async Task Power_valeurs_ac_dc_lues_quelle_que_soit_la_langue(string sample)
        {
            var apply = "$t = @'\r\n" + sample + "'@\r\n$r = ConvertFrom-GTPowerQuery ($t -split \"`r?`n\")\r\nWrite-Output ('R=' + $r.AC + ',' + $r.DC)";
            Assert.Contains("R=100,50", await PsEval(apply));
        }

        [Theory]
        [InlineData("Yes", "yes")]
        [InlineData("Oui", "yes")]
        [InlineData("Ja", "yes")]
        [InlineData("Sí", "yes")]
        [InlineData("Sì", "yes")]
        [InlineData("Sim", "yes")]
        [InlineData("No", "no")]
        [InlineData("Non", "no")]
        [InlineData("Nein", "no")]
        [InlineData("Não", "no")]
        [InlineData("Enhanced", "Enhanced")]
        [InlineData("0x1f", "0x1f")]
        public async Task Bcd_valeur_affichee_normalisee(string shown, string canon)
        {
            var log = await PsEval("Write-Output ('R=' + (ConvertTo-GTBcdCanonical '" + shown + "'))");
            Assert.Contains("R=" + canon, log);
        }

        [Fact]
        public async Task Bcd_valeur_non_canonisable_leve_une_erreur()
        {
            var log = await PsEval("try { ConvertTo-GTBcdCanonical 'Да'; Write-Output 'R=pas-d-erreur' } catch { Write-Output 'R=erreur' }");
            Assert.Contains("R=erreur", log);
        }

        [Fact]
        public async Task Bcd_lecture_de_la_sortie_enum()
        {
            var apply = "$l = @('identifier              {current}','useplatformclock        Oui','x2apicpolicy           Enabled')\r\n" +
                        "Write-Output ('R=' + (Get-GTBcdValue $l 'useplatformclock') + '/' + (Get-GTBcdValue $l 'x2apicpolicy') + '/' + [string]($null -eq (Get-GTBcdValue $l 'absent')))";
            Assert.Contains("R=Oui/Enabled/True", await PsEval(apply));
        }

        [Fact]
        public async Task Annulation_repetee_sans_effet()
        {
            Ecrire("X", 5, RegistryValueKind.DWord);
            await Apply("a", SetReg("X", "DWord", "1"));
            Assert.Equal(OutcomeStatus.Ok, (await Undo("a")).Items[0].Status);
            Assert.Equal(5, Lire("X"));
            Ecrire("X", 7, RegistryValueKind.DWord);
            var u2 = await Undo("a");
            Assert.Equal(OutcomeStatus.Skipped, u2.Items[0].Status);
            Assert.Contains("déjà annulé", u2.Items[0].Message);
            Assert.Equal(7, Lire("X"));
            Assert.Empty(UndoService.FindConflicts(Jrn, new[] { "a" }));
        }

        [Fact]
        public async Task Appliquer_annuler_reappliquer_annuler_restaure_avant_la_seconde_application()
        {
            Ecrire("X", 5, RegistryValueKind.DWord);
            await Apply("a", SetReg("X", "DWord", "1"));
            await Undo("a");
            Ecrire("X", 6, RegistryValueKind.DWord);
            await Apply("a", SetReg("X", "DWord", "2"));
            await Undo("a");
            Assert.Equal(6, Lire("X"));
        }

        [Fact]
        public async Task Quarantaine_annulation_rejouee_sans_echec()
        {
            var f = Path.Combine(_tmp, "vieux.txt");
            File.WriteAllText(f, "contenu");
            var q = Path.Combine(_tmp, "quarantaine");
            await Apply("a", $"Move-TweakQuarantine -File '{f}'", q);
            // Simule une restauration interrompue avant le marqueur : le fichier est déjà revenu.
            File.Move((string)Journal.Read(Jrn)[0].After, f);
            var u = await Undo("a");
            Assert.Equal(OutcomeStatus.Ok, u.Items[0].Status);
            Assert.Equal("contenu", File.ReadAllText(f));
        }

        [Fact]
        public async Task Journal_fragment_final_tronque_avant_l_ajout_suivant()
        {
            await Apply("a", SetReg("X", "DWord", "1"));
            File.AppendAllText(Jrn, "{\"item\":\"a\",\"kin");
            var r = await Apply("b", SetReg("Y", "DWord", "1"));
            Assert.Equal(OutcomeStatus.Ok, r.Items[0].Status);
            var e = Journal.Read(Jrn);
            Assert.Equal(new[] { "a", "b" }, e.Select(x => x.Item).ToArray());
            Assert.All(File.ReadAllLines(Jrn), l => Assert.StartsWith("{", l));
        }

        [Fact]
        public async Task Entree_sans_option_courante_refusee()
        {
            var common = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "GamingTweaker", "Assets", "runtime", "Common.ps1"));
            var apply = "$GT_JournalPath = '" + Jrn + "'\r\n" + common +
                        "\r\ntry { Set-TweakRegistry -Path " + RegPath + " -Name X -Type DWord -Value 1; Write-Output 'R=pas-d-erreur' } catch { Write-Output 'R=erreur' }";
            var log = new List<string>();
            await Runner.RunAsync(apply, l => log.Add(l), CancellationToken.None, id => 120);
            Assert.Contains("R=erreur", log);
            Assert.False(Existe("X"));
            Assert.False(File.Exists(Jrn));
        }
    }
}
