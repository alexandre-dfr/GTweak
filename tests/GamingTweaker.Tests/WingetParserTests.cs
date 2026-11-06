using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Lists;
using GamingTweaker.Tests.Helpers;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>Analyse de « winget upgrade » : fixtures capturées sur le PC de développement (français), et variante dérivée avec noms tronqués.</summary>
    public class WingetParserTests
    {
        private static string Fr => Fixtures.Lire("winget-fr.txt");
        private static string Tronque => Fixtures.Lire("winget-fr-tronque.txt");
        private static string Aucune => Fixtures.Lire("winget-aucune.txt");

        [Fact] public void Winget_analyse_le_tableau_francais() => Assert.Contains(WingetProvider.Parse(Fr), e => e.Key == "Git.Git");

        [Fact] public void Winget_lit_toutes_les_lignes_sans_le_pied_de_page()
        {
            var l = WingetProvider.Parse(Fr);
            Assert.Equal(16, l.Count);
            Assert.Contains(l, e => e.Key == "XPFFTQ032PTPHF");   // source msstore
            Assert.Contains(l, e => e.Key == "Adobe.Acrobat.Reader.64-bit");
            Assert.DoesNotContain(l, e => e.Key.StartsWith("mises", StringComparison.OrdinalIgnoreCase));
            var git = l.Single(e => e.Key == "Git.Git");
            Assert.Equal("Git (Git.Git)", git.Titre);
            Assert.Contains("2.56.0.3", git.Detail);
            Assert.Contains("2.56.0.4", git.Detail);
        }

        [Fact] public void Winget_utilise_la_position_des_colonnes_avec_noms_tronques()
        {
            var l = WingetProvider.Parse(Tronque);
            Assert.Contains(l, e => e.Titre.Contains("…"));            // la fixture contient bien des noms tronqués
            Assert.DoesNotContain(l, e => e.Key.Contains(" "));
            Assert.Equal(WingetProvider.Parse(Fr).Select(e => e.Key), l.Select(e => e.Key));
        }

        [Fact] public void Winget_sans_mise_a_jour_renvoie_vide() => Assert.Empty(WingetProvider.Parse(Aucune));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("<html>pas winget</html>")]
        [InlineData("Nom   ID   Version\r\n")]
        public void Winget_sortie_inattendue_renvoie_vide(string sortie) => Assert.Empty(WingetProvider.Parse(sortie));

        [Fact] public void Winget_ignore_la_barre_de_progression_et_les_tableaux_suivants()
        {
            var s = "\r   - \r   \\ \r" + Fr.TrimStart('﻿').Replace("Nom ", "Nom ") + "\r\n\r\nLes packages suivants ont été épinglés :\r\nNom       ID          Version Disponible Source\r\n----------------------------------------------\r\nAutre App Autre.App   1.0     2.0        winget\r\n";
            var l = WingetProvider.Parse(s);
            Assert.DoesNotContain(l, e => e.Key == "Autre.App");
            Assert.Equal(16, l.Count);
        }

        [Fact] public void Winget_et_PowerShell_donnent_les_memes_identifiants()
        {
            foreach (var f in new[] { "winget-fr.txt", "winget-fr-tronque.txt", "winget-aucune.txt" })
            {
                var attendu = WingetProvider.Parse(Fixtures.Lire(f)).Select(e => e.Key).ToList();
                var ps = PsCommon.Eval("$l = [IO.File]::ReadAllLines('" + Fixtures.Chemin(f).Replace("'", "''") + "', [Text.Encoding]::UTF8)\r\n(ConvertFrom-GTWingetUpgrade $l) | ForEach-Object { $_.Id }");
                Assert.Equal(attendu, ps);
            }
        }

        [Fact] public async Task Le_fournisseur_liste_les_mises_a_jour()
        {
            var p = new WingetProvider(ct => Task.FromResult(Fr));
            Assert.Equal("winget", p.ListId);
            Assert.Equal(16, (await p.LoadAsync(CancellationToken.None)).Count);
            Assert.Equal("", p.Message);
        }

        [Fact] public async Task Winget_absent_donne_une_liste_vide_et_un_message()
        {
            var p = new WingetProvider(ct => { throw new Win32Exception(2); });
            Assert.Empty(await p.LoadAsync(CancellationToken.None));
            Assert.Contains("winget est introuvable", p.Message);
        }

        [Fact] public async Task Aucune_mise_a_jour_est_signalee()
        {
            var p = new WingetProvider(ct => Task.FromResult(Aucune));
            Assert.Empty(await p.LoadAsync(CancellationToken.None));
            Assert.Contains("Aucune mise à jour", p.Message);
        }

        // ---- Windows Update ------------------------------------------------------------------------

        [Fact] public void Windows_Update_analyse_les_lignes_KB()
        {
            var l = WindowsUpdateProvider.Parse("KB5034441|Mise à jour cumulative 2024-01|512\r\nbruit\r\nKB5034441|doublon|1\r\nKB12|trop court|1\r\nKB890830|Outil de suppression de logiciels malveillants|0\r\n");
            Assert.Equal(new[] { "KB5034441", "KB890830" }, l.Select(e => e.Key));
            Assert.Contains("512 Mo", l[0].Detail);
            Assert.DoesNotContain("Mo", l[1].Detail);
        }

        [Fact] public async Task Le_fournisseur_Windows_Update_utilise_la_requete_logicielle_sans_pilotes()
        {
            var p = new WindowsUpdateProvider(ct => Task.FromResult("KB5034441|Cumulative|10\r\n"));
            Assert.Equal("windowsUpdate", p.ListId);
            Assert.Single(await p.LoadAsync(CancellationToken.None));
            var vide = new WindowsUpdateProvider(ct => Task.FromResult(""));
            Assert.Empty(await vide.LoadAsync(CancellationToken.None));
            Assert.Contains("Aucune mise à jour", vide.Message);
        }
    }
}
