using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GamingTweaker.Core;
using GamingTweaker.Tests.Helpers;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>
    /// Menu Mises à jour. Rien n'est installé : winget est remplacé par un double qui rejoue la fixture et journalise ses arguments ;
    /// la recherche COM de Windows Update et l'installation du pilote ne sont pas exécutées (contrôles statiques).
    /// </summary>
    public class MajsTests : IDisposable
    {
        private const string Menu = "Mises à jour";
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "GTUpd-" + Guid.NewGuid().ToString("N"));

        public MajsTests() { Directory.CreateDirectory(tmp); }
        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        private static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        private static Dictionary<string, CatalogItem> Catalogue() =>
            GamingTweaker.Core.Catalogue.LoadEmbedded().Where(i => i.Menu == Menu).ToDictionary(i => i.Id);

        private static readonly Dictionary<string, RiskLevel> Minimum = new Dictionary<string, RiskLevel>
        {
            { "upd.windows", RiskLevel.Sur }, { "upd.apps", RiskLevel.Sur }, { "upd.nvidia", RiskLevel.Avance }, { "upd.wu-auto-off", RiskLevel.Risque },
        };

        /// <summary>winget factice : « upgrade » sans --id affiche la fixture ; avec --id (ou « install »), journalise et réussit (ou renvoie le code demandé).</summary>
        private string FauxWinget(string fixture = "winget-fr.txt", int codeInstall = 0)
        {
            var cmd = Path.Combine(tmp, "winget-factice.cmd");
            var log = Path.Combine(tmp, "winget.log");
            File.WriteAllText(cmd, "@echo off\r\necho %* | findstr /C:\"--id\" >nul\r\nif %errorlevel%==0 (\r\n  echo %* >>\"" + log + "\"\r\n  exit /b " + codeInstall + "\r\n)\r\ntype \"" + Fixtures.Chemin(fixture) + "\"\r\nexit /b 0\r\n");
            return "$GT_WingetPath = " + Q(cmd) + "\r\n";
        }

        private string LogWinget() { var f = Path.Combine(tmp, "winget.log"); return File.Exists(f) ? File.ReadAllText(f) : ""; }

        private string Apply(string id, string[] selection, string preambule)
        {
            var sel = Newtonsoft.Json.JsonConvert.SerializeObject(selection);
            return string.Join("\n", PsCommon.Eval("$GT_JournalPath = " + Q(Path.Combine(tmp, "j.jsonl")) + "\r\n$Selection = [string[]]@(ConvertFrom-Json -InputObject " + Q(sel) + " | ForEach-Object { $_ })\r\n"
                + preambule + "\r\nInvoke-GTItem " + Q(id) + " ([scriptblock]::Create(" + Q(Catalogue()[id].Apply) + "))"));
        }

        // ---- upd.apps -------------------------------------------------------------------------------------

        [Fact] public void Tout_met_a_jour_chaque_application_proposee_avec_les_options_silencieuses()
        {
            var r = Apply("upd.apps", new[] { "*" }, FauxWinget());
            Assert.Contains("##GT|ok|upd.apps|16 application(s) mise(s) à jour.", r);
            var lignes = LogWinget().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(16, lignes.Length);
            Assert.Contains(lignes, l => l.Trim() == "upgrade --id Git.Git --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity");
        }

        [Fact] public void Les_exclusions_sont_respectees_et_les_noms_inconnus_ne_sont_jamais_executes()
        {
            var r = Apply("upd.apps", new[] { "*", "!Git.Git", "!Python.Python.3.13" }, FauxWinget());
            Assert.Contains("14 application(s) mise(s) à jour", r);
            var log = LogWinget();
            Assert.DoesNotContain("--id Git.Git ", log);
            Assert.DoesNotContain("Python.Python.3.13", log);

            File.Delete(Path.Combine(tmp, "winget.log"));
            var r2 = Apply("upd.apps", new[] { "Pirate.Paquet", "Git.Git" }, FauxWinget());
            Assert.Contains("1 application(s) mise(s) à jour", r2);
            Assert.DoesNotContain("Pirate.Paquet", LogWinget());

            File.Delete(Path.Combine(tmp, "winget.log"));
            var r3 = Apply("upd.apps", new[] { "Pirate.Paquet" }, FauxWinget());
            Assert.Contains("##GT|skip|upd.apps|Aucune mise à jour à installer", r3);
            Assert.Equal("", LogWinget());
        }

        [Fact] public void Une_mise_a_jour_sans_objet_est_un_succes_et_un_echec_est_signale()
        {
            // 0x8A15002B = aucune mise à jour applicable
            Assert.Contains("##GT|ok|upd.apps", Apply("upd.apps", new[] { "Git.Git" }, FauxWinget(codeInstall: -1978335189)));
            var r = Apply("upd.apps", new[] { "Git.Git", "Tailscale.Tailscale" }, FauxWinget(codeInstall: 1));
            Assert.Contains("##GT|fail|upd.apps", r);
            Assert.Contains("Git.Git (code 1)", r);
            Assert.Contains("Tailscale.Tailscale (code 1)", r);
        }

        [Fact] public void Sans_winget_toute_l_option_est_ignoree_avec_un_message()
        {
            var r = Apply("upd.apps", new[] { "*" }, "function Get-GTWingetPath { $null }");
            Assert.Contains("##GT|skip|upd.apps|winget est introuvable", r);
        }

        [Fact] public void Sans_mise_a_jour_disponible_l_option_est_ignoree()
        {
            var r = Apply("upd.apps", new[] { "*" }, FauxWinget("winget-aucune.txt"));
            Assert.Contains("##GT|skip|upd.apps", r);
            Assert.Equal("", LogWinget());
        }

        // ---- upd.windows et upd.nvidia (contrôles statiques) ------------------------------------------------------

        [Fact] public void Windows_Update_utilise_l_API_COM_sans_pilotes_et_limite_la_selection_aux_mises_a_jour_trouvees()
        {
            var a = Catalogue()["upd.windows"].Apply;
            Assert.Contains("Microsoft.Update.Session", a);
            Assert.Contains("IsInstalled=0 and Type='Software' and IsHidden=0", a);
            Assert.Contains("Resolve-GTListSelection", a);
            Assert.Contains("CreateUpdateDownloader", a);
            Assert.Contains("CreateUpdateInstaller", a);
            Assert.Contains("RebootRequired", a);
            Assert.Contains("Set-GTItemNote", a);
            Assert.DoesNotContain("Driver", a);
            Assert.DoesNotContain("Restart-Computer", a);
            Assert.DoesNotContain("shutdown", a, StringComparison.OrdinalIgnoreCase);
        }

        [Fact] public void Le_pilote_NVIDIA_est_verifie_avant_installation_et_installe_sans_redemarrage()
        {
            var cat = Catalogue();
            var a = cat["upd.nvidia"].Apply;
            Assert.Contains("Resolve-GTListSelection", a);
            Assert.Contains("'GameReady', 'Studio'", a);
            Assert.Contains("Install-TweakNvidiaDriver", a);
            Assert.DoesNotContain("Start-Process", a);
            var common = PsCommon.CommonText();
            var f = common.Substring(common.IndexOf("function Install-TweakNvidiaDriver", StringComparison.Ordinal));
            f = f.Substring(0, f.IndexOf("\r\n}\r\n", StringComparison.Ordinal));
            Assert.Contains("Confirm-GTNvidiaInstaller", f);
            Assert.True(f.IndexOf("Confirm-GTNvidiaInstaller", StringComparison.Ordinal) < f.IndexOf("Start-Process", StringComparison.Ordinal), "la signature doit être vérifiée avant le lancement");
            Assert.Contains("'-s', '-noreboot'", f);
            Assert.Contains("Test-GTNvidiaUrl", f);
            Assert.Contains("Get-AuthenticodeSignature", common);
            Assert.Contains("NVIDIA Corporation", common);
            Assert.Contains("'Valid'", common);
        }

        // ---- catalogue ---------------------------------------------------------------------------------------

        [Fact] public void Le_catalogue_mises_a_jour_contient_exactement_le_tableau_et_le_recapitulatif()
        {
            var cat = Catalogue();
            var recap = Couverture.Recapitulatif("Mises à jour");
            Assert.Single(recap);
            var attendu = new HashSet<string>(Minimum.Keys.Concat(recap.Select(r => r.Id)));
            Assert.Empty(cat.Keys.Where(k => !attendu.Contains(k)));
            Assert.Empty(attendu.Where(k => !cat.ContainsKey(k)));
            foreach (var kv in Minimum) Assert.True(cat[kv.Key].Risque == kv.Value, kv.Key + " : risque " + cat[kv.Key].Risque);
            foreach (var r in recap)
            {
                var it = cat[r.Id];
                Assert.True(it.Risque == r.Risque, r.Id + " : risque");
                Assert.True(it.Reversible == r.EstReversible, r.Id + " : réversible");
                Assert.Equal(r.Groupe, it.Groupe);
            }
            foreach (var it in cat.Values)
            {
                Assert.False(string.IsNullOrWhiteSpace(it.Description), it.Id);
                Assert.False(string.IsNullOrWhiteSpace(it.Detect), it.Id);
                Assert.False(string.IsNullOrWhiteSpace(it.Apply), it.Id);
                Assert.Equal(it.Reversible, !string.IsNullOrWhiteSpace(it.Revert));
                Assert.Equal(new[] { "10", "11" }, it.Compatibilite.Windows);
                Assert.StartsWith("upd.", it.Id);
                Assert.Matches(@"^[a-z0-9][a-z0-9.\-]*$", it.Id);
                Assert.False(it.Hkcu, it.Id);
            }
        }

        [Fact] public void Les_options_a_liste_utilisent_les_cles_de_profil_typees()
        {
            var cat = Catalogue();
            Assert.Equal("windowsUpdate", cat["upd.windows"].Liste);
            Assert.Equal("winget", cat["upd.apps"].Liste);
            Assert.Equal("pilote", cat["upd.nvidia"].Liste);
            Assert.Null(cat["upd.wu-auto-off"].Liste);
            Assert.False(cat["upd.windows"].Reversible);
            Assert.False(cat["upd.apps"].Reversible);
            Assert.False(cat["upd.nvidia"].Reversible);
            Assert.True(cat["upd.windows"].Redemarrage);
            Assert.False(cat["upd.apps"].Redemarrage);
        }

        [Fact] public void Le_blocage_des_mises_a_jour_automatiques_ne_touche_pas_aux_services()
        {
            var it = Catalogue()["upd.wu-auto-off"];
            Assert.Equal(RiskLevel.Risque, it.Risque);
            Assert.Contains(@"HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", it.Apply);
            Assert.Contains("'NoAutoUpdate'", it.Apply);
            Assert.Contains("-Value 1", it.Apply);
            Assert.Contains("Set-TweakRegistryIfNeeded", it.Apply);
            foreach (var interdit in new[] { "Service", "wuauserv", "UsoSvc", "sc.exe", "Stop-", "Disable-" }) Assert.DoesNotContain(interdit, it.Apply);
            Assert.Contains("services restent actifs", it.Description);
            var drv = Catalogue()["upd.wu-drivers-off"];
            Assert.Equal(RiskLevel.Avance, drv.Risque);
            Assert.Contains("SearchOrderConfig", drv.Apply);
            Assert.Contains("ExcludeWUDriversInQualityUpdate", drv.Apply);
        }

        [Fact] public void Les_scripts_upd_sont_syntaxiquement_valides() => Assert.Empty(PsSyntax.CheckAll(Catalogue().Values));

        [Fact] public void Les_scripts_apply_upd_ne_modifient_le_systeme_que_par_les_fonctions_journalisees()
        {
            var interdits = new[] { "Set-ItemProperty", "New-ItemProperty", "Remove-ItemProperty", "Set-Service", "sc.exe", "reg add", "reg.exe", "reg delete",
                                    "Out-File", "Set-Content", "Add-Content", "WriteAllLines", "Remove-Item", "New-Item", "Stop-Process", "Invoke-Expression", "iex ",
                                    "Start-Process", "Invoke-WebRequest", "Restart-Computer", "Stop-Computer", "shutdown", "Start-Service", "Restart-Service", "Stop-Service",
                                    "Set-MpPreference", "powercfg", "bcdedit" };
            foreach (var it in Catalogue().Values)
                foreach (var m in interdits)
                    Assert.False(it.Apply.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0, it.Id + " : apply contient « " + m + " »");
        }

        [Fact] public void Jamais_de_redemarrage_automatique_dans_les_scripts_upd_ni_blt()
        {
            var tous = GamingTweaker.Core.Catalogue.LoadEmbedded().Where(i => i.Menu == Menu || i.Menu == "Bloatware");
            foreach (var it in tous)
                foreach (var s in new[] { it.Apply, it.Revert })
                    if (s != null) Assert.False(Regex.IsMatch(s, @"(?i)Restart-Computer|Stop-Computer|shutdown\.exe|\bshutdown\b|/Reboot"), it.Id);
        }
    }
}
