using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GamingTweaker.Core;
using GamingTweaker.Lists;
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
            { "upd.windows", RiskLevel.Sur }, { "upd.apps", RiskLevel.Sur }, { "upd.nvidia", RiskLevel.Avance },
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

        [Fact] public void Le_code_winget_redemarrage_requis_est_un_succes_avec_une_note_de_redemarrage()
        {
            // 0x8A15010D : la mise à jour est faite, un redémarrage est nécessaire.
            var r = Apply("upd.apps", new[] { "Git.Git" }, FauxWinget(codeInstall: -1978334963));
            Assert.Contains("##GT|ok|upd.apps", r);
            Assert.DoesNotContain("##GT|fail", r);
            Assert.Contains("redémarrage", r);
            Assert.Equal("OK", string.Join("", PsCommon.Eval("if (Test-GTWingetOk -1978334963) { 'OK' } else { 'KO' }")));
            Assert.Equal("KO", string.Join("", PsCommon.Eval("if (Test-GTWingetReboot 0) { 'OK' } else { 'KO' }")));
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

        [Fact] public void Windows_Update_et_son_fournisseur_utilisent_la_meme_requete_sans_facultatives_ni_Upgrades()
        {
            var a = Catalogue()["upd.windows"].Apply;
            Assert.Contains("IsInstalled=0 and Type='Software' and IsHidden=0 and BrowseOnly=0", WindowsUpdateProvider.Requete);
            Assert.Contains("\"" + WindowsUpdateProvider.Requete + "\"", a);
            Assert.Contains(WindowsUpdateProvider.FiltreUpgrades, a);
            Assert.Contains("3689bdc8-b205-4af4-8d4a-a63924c5e9d5", WindowsUpdateProvider.FiltreUpgrades);
            Assert.Contains(WindowsUpdateProvider.FiltreUpgrades, WindowsUpdateProvider.ScriptRecherche);
            Assert.Contains(WindowsUpdateProvider.Requete, WindowsUpdateProvider.ScriptRecherche);
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

        // ---- NVIDIA : dossier réservé aux administrateurs, verrou de la vérification au lancement ------------------

        /// <summary>Doubles : téléchargement (écrit un faux exe), création de dossier (journalisée, sans ACL réelle), signature et lancement ; chacun tente d'écrire dans l'installateur pour vérifier le verrou.</summary>
        private string FauxNvidia(string racine, string log, bool signatureValide)
        {
            var sub = "try { [IO.File]::OpenWrite($dest).Dispose(); 'ECRITURE-POSSIBLE' } catch { 'ECRITURE-REFUSEE' }";
            return "$GT_DownloadRoot = " + Q(racine) + "\r\n"
                + "function Initialize-GTAdminDir([string]$Path) { Add-Content -LiteralPath " + Q(log) + " -Value ('acl ' + $Path); New-Item -ItemType Directory -Force -Path $Path | Out-Null }\r\n"
                + "function Invoke-WebRequest { [CmdletBinding()] param([switch]$UseBasicParsing, [string]$Uri, [string]$OutFile, [int]$TimeoutSec) Add-Content -LiteralPath " + Q(log) + " -Value 'download'; Set-Content -LiteralPath $OutFile -Value 'MZ' }\r\n"
                + "function Get-AuthenticodeSignature { [CmdletBinding()] param([string]$LiteralPath) $dest = $LiteralPath; Add-Content -LiteralPath " + Q(log) + " -Value ('verif ' + $(" + sub + ")); "
                + "[pscustomobject]@{ Status = " + (signatureValide ? "'Valid'" : "'NotSigned'") + "; SignerCertificate = [pscustomobject]@{ Subject = 'CN=NVIDIA Corporation' } } }\r\n"
                + "function Start-Process { [CmdletBinding()] param([string]$FilePath, $ArgumentList, [switch]$Wait, [switch]$PassThru) $dest = $FilePath; Add-Content -LiteralPath " + Q(log) + " -Value ('lancement ' + $(" + sub + ") + ' ' + ($ArgumentList -join ' ')); [pscustomobject]@{ ExitCode = 0 } }\r\n";
        }

        [Fact] public void L_ACL_du_dossier_de_telechargement_est_reservee_aux_administrateurs_et_a_SYSTEM()
        {
            var r = PsCommon.Eval(@"$acl = New-GTAdminOnlyAcl
'protege=' + $acl.AreAccessRulesProtected
foreach ($rule in $acl.GetAccessRules($true, $false, [System.Security.Principal.SecurityIdentifier])) { 'regle=' + $rule.IdentityReference.Value + ':' + $rule.FileSystemRights + ':' + $rule.AccessControlType + ':' + $rule.InheritanceFlags }
'racine=' + (Get-GTDownloadRoot)");
            Assert.Contains("protege=True", r);
            var regles = r.Where(l => l.StartsWith("regle=")).ToList();
            Assert.Equal(2, regles.Count);
            Assert.Contains(regles, l => l.StartsWith("regle=S-1-5-32-544:FullControl:Allow"));
            Assert.Contains(regles, l => l.StartsWith("regle=S-1-5-18:FullControl:Allow"));
            Assert.Contains(r, l => l.StartsWith("racine=") && l.EndsWith(@"GamingTweaker\Telechargements"));
        }

        [Fact] public void Le_pilote_NVIDIA_est_telecharge_dans_le_dossier_admin_verrouille_verifie_puis_lance_sous_verrou()
        {
            var racine = Path.Combine(tmp, "Telechargements"); var log = Path.Combine(tmp, "nv.log");
            var r = PsCommon.Eval(FauxNvidia(racine, log, true) + "Install-TweakNvidiaDriver -Url 'https://us.download.nvidia.com/Windows/555.99/555.99-desktop-win10-win11-64bit-international-dch-whql.exe'\r\n'FIN'");
            Assert.Contains("FIN", r);
            var l = File.ReadAllLines(log);
            Assert.Equal(new[] { "acl " + racine }, l.Take(1));
            Assert.StartsWith("acl " + racine + Path.DirectorySeparatorChar, l[1]);
            Assert.Equal(new[] { "download", "verif ECRITURE-REFUSEE", "lancement ECRITURE-REFUSEE -s -noreboot" }, l.Skip(2).ToArray());
            Assert.Empty(Directory.GetDirectories(racine));   // dossier supprimé après l'installation
        }

        [Fact] public void Une_signature_invalide_arrete_avant_le_lancement_et_supprime_le_dossier()
        {
            var racine = Path.Combine(tmp, "Telechargements"); var log = Path.Combine(tmp, "nv.log");
            var r = PsCommon.Eval(FauxNvidia(racine, log, false) + "try { Install-TweakNvidiaDriver -Url 'https://us.download.nvidia.com/Windows/1/pilote.exe'; 'PAS-D-ERREUR' } catch { 'ERREUR:' + $_.Exception.Message }");
            Assert.Contains(r, x => x.StartsWith("ERREUR:") && x.Contains("signature"));
            var l = File.ReadAllText(log);
            Assert.Contains("verif ECRITURE-REFUSEE", l);
            Assert.DoesNotContain("lancement", l);
            Assert.Empty(Directory.GetDirectories(racine));
        }

        [Fact] public void Le_verrou_laisse_l_installateur_lisible_et_executable_par_un_vrai_processus()
        {
            // Copie d'un petit exécutable système, verrouillée comme dans Install-TweakNvidiaDriver, puis lancée : rien n'est installé.
            var exe = Path.Combine(tmp, "faux-installateur.exe");
            File.Copy(Path.Combine(Environment.GetEnvironmentVariable("SystemRoot"), "System32", "whoami.exe"), exe);
            var r = PsCommon.Eval("$l = Open-GTLockedFile " + Q(exe) + "\r\ntry {\r\n"
                + "try { [IO.File]::OpenWrite(" + Q(exe) + ").Dispose(); 'ECRITURE-POSSIBLE' } catch { 'ECRITURE-REFUSEE' }\r\n"
                + "try { Remove-Item -LiteralPath " + Q(exe) + " -Force -ErrorAction Stop; 'SUPPRESSION-POSSIBLE' } catch { 'SUPPRESSION-REFUSEE' }\r\n"
                + "$p = Start-Process -FilePath " + Q(exe) + " -ArgumentList '/?' -Wait -PassThru -WindowStyle Hidden; 'LANCE'\r\n"
                + "} finally { $l.Dispose() }\r\n"
                + "try { [IO.File]::OpenWrite(" + Q(exe) + ").Dispose(); 'LIBERE' } catch { 'ENCORE-VERROUILLE' }");
            Assert.Equal(new[] { "ECRITURE-REFUSEE", "SUPPRESSION-REFUSEE", "LANCE", "LIBERE" }, r.ToArray());
        }

        // ---- catalogue ---------------------------------------------------------------------------------------

        [Fact] public void Le_catalogue_mises_a_jour_contient_exactement_le_tableau_et_le_recapitulatif()
        {
            var cat = Catalogue();
            var recap = Couverture.Recapitulatif("Mises à jour");
            Assert.Equal(new[] { "upd.wu-drivers-off", "upd.pause-max" }, recap.Select(r => r.Id));
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
            Assert.False(cat["upd.windows"].Reversible);
            Assert.False(cat["upd.apps"].Reversible);
            Assert.False(cat["upd.nvidia"].Reversible);
            Assert.True(cat["upd.windows"].Redemarrage);
            Assert.False(cat["upd.apps"].Redemarrage);
        }

        [Fact] public void Les_mises_a_jour_automatiques_ne_sont_plus_bloquees_et_les_pilotes_restent_exclus()
        {
            var cat = Catalogue();
            Assert.DoesNotContain("upd.wu-auto-off", cat.Keys);
            foreach (var it in cat.Values) Assert.DoesNotContain("NoAutoUpdate", it.Apply);
            var drv = cat["upd.wu-drivers-off"];
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
