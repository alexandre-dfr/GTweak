using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using GamingTweaker.Core;
using GamingTweaker.Lists;
using GamingTweaker.Tests.Helpers;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>
    /// Menu Installation : catalogue des applications, liste proposée, ordre d'exécution et script d'installation. Rien n'est installé :
    /// winget, le téléchargement, la signature et l'installateur sont remplacés par des doubles qui journalisent leurs arguments.
    /// </summary>
    public class InstallationTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "GTIns-" + Guid.NewGuid().ToString("N"));

        public InstallationTests() { Directory.CreateDirectory(tmp); }
        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        private static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        private static IReadOnlyList<AppInstallable> Apps => AppsCatalogue.LoadEmbedded();

        // ---- Catalogue ------------------------------------------------------------------------------------

        [Fact] public void Le_catalogue_des_applications_est_valide() => Assert.Empty(AppsCatalogue.Validate(Apps));

        [Fact]
        public void Chaque_categorie_a_son_option_et_les_applications_demandees_y_figurent()
        {
            var cat = GamingTweaker.Core.Catalogue.LoadEmbedded().Where(i => i.Menu == "Installation").ToDictionary(i => i.Id);
            foreach (var c in AppsCatalogue.Categories)
            {
                var it = cat["ins." + c];
                Assert.Equal("applis-" + c, it.Liste);
                Assert.False(it.Reversible);
                Assert.False(it.Testable);
                Assert.Contains(Apps, a => a.Categorie == c);
            }
            foreach (var id in new[] { "steam", "epic", "ubisoft", "ea", "battlenet", "rockstar", "ghub", "icue", "synapse", "steelseries", "armoury", "msicenter",
                                       "nvidia-app", "amd-adrenalin", "discord", "operagx", "notepadpp", "deezer", "vcredist-x64", "directx", "riot" })
                Assert.Contains(Apps, a => a.Id == id);
            // Launchers seulement, aucun jeu : le Riot Client remplace League of Legends et VALORANT.
            Assert.DoesNotContain(Apps, a => a.Id == "lol" || a.Id == "valorant");
            Assert.True(cat["ins.composants"].Recommande);
            Assert.False(cat["ins.launchers"].Recommande);
        }

        [Fact]
        public void Validate_refuse_http_un_lien_sans_signataire_un_exe_interactif_et_des_arguments_dangereux()
        {
            var x1 = new List<string> { "X Inc." };
            var e = AppsCatalogue.Validate(new[]
            {
                new AppInstallable { Id = "a", Categorie = "applis", Nom = "A", Url = "http://x.com/a.exe", Type = "exe", Signataires = x1, Args = "/S" },
                new AppInstallable { Id = "b", Categorie = "applis", Nom = "B", Url = "https://x.com/b.exe", Type = "exe", Args = "/S" },
                new AppInstallable { Id = "c", Categorie = "applis", Nom = "C", Url = "https://x.com/c.exe", Type = "exe", Signataires = x1, Args = "/S; calc" },
                new AppInstallable { Id = "d", Categorie = "inconnue", Nom = "D", Winget = "D.D" },
                new AppInstallable { Id = "e", Categorie = "applis", Nom = "E" },
                new AppInstallable { Id = "f", Categorie = "applis", Nom = "F", Url = "https://x.com/f.exe", Type = "exe", Signataires = x1 },
                new AppInstallable { Id = "g", Categorie = "applis", Nom = "G", Page = "https://x.com/", Fichiers = new List<string> { @"C:\Windows\x.dll", @"%SystemRoot%\..\x.dll" } },
                new AppInstallable { Id = "h", Categorie = "applis", Nom = "H", Url = "https://x.com/h.msi", Type = "msi", Signataires = x1,
                                     Fichiers = new List<string> { @"%SystemRoot%\System32\d3dx9_43.dll" } },
            });
            Assert.Contains(e, x => x.StartsWith("a :") && x.Contains("https"));
            Assert.Contains(e, x => x.StartsWith("b :") && x.Contains("signataire"));
            Assert.Contains(e, x => x.StartsWith("f :") && x.Contains("silencieuse"));
            Assert.Equal(2, e.Count(x => x.StartsWith("g :") && x.Contains("fichier de détection")));
            Assert.DoesNotContain(e, x => x.StartsWith("h :"));   // un msi s'installe toujours en silence (msiexec /qn)
            Assert.Contains(e, x => x.StartsWith("c :") && x.Contains("arguments"));
            Assert.Contains(e, x => x.StartsWith("d :") && x.Contains("catégorie"));
            Assert.Contains(e, x => x.StartsWith("e :") && x.Contains("ni winget"));
        }

        [Fact] public void Le_json_compact_tient_sur_une_ligne()
        {
            var j = AppsCatalogue.JsonCompact();
            Assert.DoesNotContain("\n", j);
            Assert.StartsWith("[", j);
        }

        // ---- Liste proposée ---------------------------------------------------------------------------------

        private class Installes : IProgrammesInstalles
        {
            private readonly string[] noms;
            public Installes(params string[] noms) { this.noms = noms; }
            public IReadOnlyList<string> Lire() => noms;
        }

        private class Marques : ISourceMarques
        {
            private readonly MaterielMarques m;
            public Marques(MaterielMarques m) { this.m = m; }
            public MaterielMarques Lire() => m;
        }

        [Fact]
        public void Les_applications_deja_installees_ne_sont_pas_proposees()
        {
            var p = new AppsProvider("launchers", null, new Installes("Steam", "Epic Games Launcher"));
            var l = p.LoadAsync(CancellationToken.None).Result;
            Assert.DoesNotContain(l, e => e.Key == "steam");
            Assert.DoesNotContain(l, e => e.Key == "epic");
            Assert.Contains(l, e => e.Key == "rockstar");
            Assert.Contains("Steam", p.Message);
            Assert.Equal("applis-launchers", p.ListId);
        }

        [Fact]
        public void Les_logiciels_du_materiel_detecte_sont_proposes_d_office_et_en_tete()
        {
            var m = new MaterielMarques { CarteMere = "ASUSTeK COMPUTER INC.", Gpu = new List<string> { "NVIDIA" }, Cpu = "AMD" };
            m.AjouterPeripheriques(new[] { @"HID\VID_046D&PID_C08B&MI_00\7&1", @"USB\VID_1B1C&PID_1B2D\5&2", @"PCI\VEN_10DE&DEV_2684" });
            var p = new AppsProvider("marques", null, new Installes(), new Marques(m));
            var l = p.LoadAsync(CancellationToken.None).Result;
            var sugg = p.SelectionParDefaut;
            Assert.Equal(new[] { "ghub", "icue", "armoury", "nvidia-app" }.OrderBy(x => x), sugg.OrderBy(x => x));
            Assert.Equal(sugg.OrderBy(x => x), l.Take(4).Select(e => e.Key).OrderBy(x => x));
            Assert.DoesNotContain("amd-adrenalin", sugg);
            Assert.DoesNotContain("intel-dsa", sugg);
            Assert.Contains("Logitech G HUB", p.Message);
        }

        [Fact]
        public void Composants_coches_par_defaut_et_detail_indique_le_mode_d_installation()
        {
            var p = new AppsProvider("composants", null, new Installes("Microsoft Visual C++ 2015-2022 Redistributable (x86) - 14.40.33810"), null, _ => false);
            Assert.Contains("vcredist-x86", p.SelectionParDefaut);   // avant le chargement : valeur du catalogue
            var l = p.LoadAsync(CancellationToken.None).Result;
            Assert.Equal(new[] { "vcredist-x64", "directx" }, p.SelectionParDefaut);
            Assert.Contains("winget, sinon téléchargement direct", l.Single(e => e.Key == "vcredist-x64").Detail);
            var nv = AppsProvider.Entree(Apps.Single(a => a.Id == "nvidia-app"));
            Assert.Contains("page officielle", nv.Detail);
        }

        [Fact]
        public void DirectX_deja_present_n_est_pas_propose_meme_sans_entree_dans_les_programmes()
        {
            var vus = new List<string>();
            var p = new AppsProvider("composants", null, new Installes(), null, f => { vus.Add(f); return true; });
            var l = p.LoadAsync(CancellationToken.None).Result;
            Assert.DoesNotContain(l, e => e.Key == "directx");
            Assert.Contains("DirectX", p.Message);
            Assert.Contains(vus, f => f.EndsWith(@"\System32\d3dx9_43.dll", StringComparison.OrdinalIgnoreCase) && !f.Contains("%"));
            Assert.Contains(vus, f => f.EndsWith(@"\SysWOW64\d3dx9_43.dll", StringComparison.OrdinalIgnoreCase));

            // Un seul fichier manquant suffit pour proposer DirectX.
            var p2 = new AppsProvider("composants", null, new Installes(), null, f => !f.EndsWith("XAudio2_7.dll", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(p2.LoadAsync(CancellationToken.None).Result, e => e.Key == "directx");
        }

        [Fact]
        public void Battle_net_et_riot_client_n_ont_pas_de_lien_direct_et_chaque_exe_direct_s_installe_en_silence()
        {
            Assert.Null(Apps.Single(a => a.Id == "battlenet").Url);
            Assert.Equal("Blizzard.BattleNet", Apps.Single(a => a.Id == "battlenet").Winget);
            var riot = Apps.Single(a => a.Id == "riot");
            Assert.Equal("launchers", riot.Categorie);
            Assert.Null(riot.Url);
            Assert.False(string.IsNullOrEmpty(riot.Page));
            foreach (var a in Apps.Where(x => !string.IsNullOrEmpty(x.Url)))
            {
                Assert.True(a.Type == "msi" || !string.IsNullOrWhiteSpace(a.Args), a.Id + " : exe direct sans installation silencieuse");
                Assert.NotEmpty(a.Signataires);
            }
        }

        // ---- Ordre d'exécution et script ----------------------------------------------------------------------

        private static CatalogItem It(string id, string menu, string liste = null) =>
            new CatalogItem { Id = id, Menu = menu, Groupe = "G", Titre = "T", Risque = RiskLevel.Sur, Apply = "# " + id, Detect = "d", Liste = liste };

        [Fact]
        public void Le_script_suit_l_ordre_des_menus_et_le_nettoyage_passe_en_dernier()
        {
            var items = new[] { It("cln.a", "Nettoyage"), It("opt.a", "Optimisation"), It("tel.a", "Télémétrie"), It("ins.launchers", "Installation", "applis-launchers"),
                                It("blt.a", "Bloatware"), It("upd.a", "Mises à jour") };
            var sel = new Selection { OptionIds = new HashSet<string>(items.Select(i => i.Id)) };
            var script = ScriptBuilder.Build(items, sel, "j.jsonl");
            var pos = new[] { "upd.a", "blt.a", "ins.launchers", "tel.a", "opt.a", "cln.a" }.Select(id => script.IndexOf("Invoke-GTItem '" + id + "'", StringComparison.Ordinal)).ToList();
            Assert.True(pos.All(p => p > 0));
            Assert.Equal(pos.OrderBy(p => p), pos);
            Assert.Contains("$GT_AppCatalog = @(ConvertFrom-Json", script);
        }

        [Fact]
        public void Le_catalogue_des_applications_n_est_ecrit_que_si_le_menu_installation_est_choisi()
        {
            var items = new[] { It("opt.a", "Optimisation") };
            var script = ScriptBuilder.Build(items, new Selection { OptionIds = new HashSet<string> { "opt.a" } }, "j.jsonl");
            Assert.DoesNotContain("$GT_AppCatalog = @(", script);
        }

        [Fact]
        public void La_signature_doit_etre_valide_et_venir_exactement_d_un_signataire_attendu()
        {
            Func<string, string, string[], string> t = (statut, sujet, noms) => string.Join("", PsCommon.Eval(
                "$s = [pscustomobject]@{ Status = " + Q(statut) + "; SignerCertificate = [pscustomobject]@{ Subject = " + Q(sujet) + " } }\r\n"
                + "if (Test-GTAppSignature $s @(" + string.Join(", ", noms.Select(Q)) + ")) { 'OK' } else { 'KO' }"));
            Assert.Equal("OK", t("Valid", "CN=Valve Corp., O=Valve Corp., L=Bellevue, S=Washington, C=US", new[] { "Valve Corp." }));
            Assert.Equal("OK", t("Valid", "CN=\"Epic Games Inc.\", O=\"Epic Games Inc.\", C=US", new[] { "Epic Games Inc." }));
            Assert.Equal("OK", t("Valid", "CN=\"Electronic Arts, Inc.\", O=\"Electronic Arts, Inc.\", C=US", new[] { "Electronic Arts, Inc." }));
            Assert.Equal("OK", t("Valid", "CN=Logitech Inc, O=Logitech Inc", new[] { "Logitech Inc." }));                       // point final ignoré
            Assert.Equal("OK", t("Valid", "CN=Ubisoft Entertainment Sweden AB", new[] { "Ubisoft Entertainment", "Ubisoft Entertainment Sweden AB" }));
            Assert.Equal("KO", t("NotSigned", "CN=Valve Corp.", new[] { "Valve Corp." }));
            Assert.Equal("KO", t("Valid", "CN=Not Valve Corp., O=Pirate", new[] { "Valve Corp." }));                         // ne fait que contenir le nom
            Assert.Equal("KO", t("Valid", "CN=Valve Corp. Fans, O=Valve Corp. Fans", new[] { "Valve Corp." }));
            Assert.Equal("KO", t("Valid", "CN=Microsoft Fan Club Ltd, O=Microsoft Fan Club Ltd", new[] { "Microsoft Corporation" }));
            Assert.Equal("KO", t("Valid", "CN=Pirate, OU=Valve Corp.", new[] { "Valve Corp." }));                             // OU ignoré
            Assert.Equal("KO", t("Valid", "CN=Valve Corp.", new string[0]));
        }

        [Fact]
        public void Les_fichiers_de_detection_doivent_tous_exister()
        {
            var a = Path.Combine(tmp, "a.dll"); var b = Path.Combine(tmp, "b.dll");
            File.WriteAllText(a, "x");
            Func<string[], string> t = f => string.Join("", PsCommon.Eval("if (Test-GTAppFiles @(" + string.Join(", ", f.Select(Q)) + ")) { 'OK' } else { 'KO' }"));
            Assert.Equal("OK", t(new[] { a }));
            Assert.Equal("KO", t(new[] { a, b }));
            Assert.Equal("KO", t(new string[0]));
            Assert.Equal("KO", t(new[] { tmp }));   // un dossier n'est pas un fichier
        }

        [Fact]
        public void Un_exe_sans_installation_silencieuse_n_est_jamais_lance()
        {
            var r = string.Join("", PsCommon.Eval("try { Start-GTAppInstaller 'C:\\x.exe' 'exe' '' ; 'LANCE' } catch { 'REFUS ' + $_.Exception.Message }"));
            Assert.StartsWith("REFUS", r);
            Assert.Contains("silencieuse", r);
        }

        [Fact]
        public void Battle_net_sans_winget_renvoie_a_la_page_officielle_sans_rien_telecharger()
        {
            var r = Apply("launchers", new[] { "battlenet" }, Doubles(false));
            Assert.Contains("##GT|skip|ins.launchers|à installer depuis la page officielle", r);
            Assert.Contains("https://download.battle.net/", r);
            Assert.DoesNotContain("download", Log());
        }

        /// <summary>Doubles : winget journalise et renvoie le code demandé ; téléchargement, signature, dossier et installateur factices.</summary>
        private string Doubles(bool winget = true, int codeWinget = 0, string sujet = "CN=Valve Corp., O=Valve Corp.", params string[] installes)
        {
            var log = Path.Combine(tmp, "log.txt");
            var sb = new System.Text.StringBuilder();
            sb.Append("$GT_AppCatalog = @(ConvertFrom-Json -InputObject ").Append(Q(AppsCatalogue.JsonCompact())).Append(" | ForEach-Object { $_ })\r\n");
            sb.Append("$GT_InstalledApps = @(").Append(string.Join(", ", installes.Select(Q))).Append(")\r\n");
            if (winget)
            {
                var cmd = Path.Combine(tmp, "winget-factice.cmd");
                File.WriteAllText(cmd, "@echo off\r\necho winget %* >>\"" + log + "\"\r\nexit /b " + codeWinget + "\r\n");
                sb.Append("$GT_WingetPath = ").Append(Q(cmd)).Append("\r\n");
            }
            else sb.Append("$GT_WingetPath = $null\r\n");
            sb.Append("function New-GTSecureDownloadDir { $d = Join-Path ").Append(Q(tmp)).Append(" ([Guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Path $d | Out-Null; return $d }\r\n");
            sb.Append("function Save-GTAppDownload([string]$Url, [string]$Dest) { Add-Content -LiteralPath ").Append(Q(log)).Append(" -Value ('download ' + $Url); Set-Content -LiteralPath $Dest -Value 'x' }\r\n");
            sb.Append("function Get-GTAppSignature([string]$Path) { [pscustomobject]@{ Status = 'Valid'; SignerCertificate = [pscustomobject]@{ Subject = ").Append(Q(sujet)).Append(" } } }\r\n");
            sb.Append("function Start-GTAppInstaller([string]$Path, [string]$Type, [string]$Arguments) { Add-Content -LiteralPath ").Append(Q(log)).Append(" -Value ('run ' + [IO.Path]::GetFileName($Path) + ' ' + $Type + ' ' + $Arguments); return 0 }\r\n");
            return sb.ToString();
        }

        private string Log() { var f = Path.Combine(tmp, "log.txt"); return File.Exists(f) ? File.ReadAllText(f) : ""; }

        private string Apply(string categorie, string[] selection, string preambule)
        {
            var sel = Newtonsoft.Json.JsonConvert.SerializeObject(selection);
            var item = GamingTweaker.Core.Catalogue.LoadEmbedded().Single(i => i.Id == "ins." + categorie);
            return string.Join("\n", PsCommon.Eval("$Selection = [string[]]@(ConvertFrom-Json -InputObject " + Q(sel) + " | ForEach-Object { $_ })\r\n"
                + preambule + "\r\nInvoke-GTItem " + Q(item.Id) + " ([scriptblock]::Create(" + Q(item.Apply) + "))"));
        }

        [Fact]
        public void Avec_winget_chaque_application_choisie_est_installee_en_silence_et_les_inconnues_ignorees()
        {
            var r = Apply("launchers", new[] { "steam", "xbox", "pirate" }, Doubles());
            Assert.Contains("##GT|ok|ins.launchers|2 installée(s) : Steam, Xbox (PC Game Pass)", r);
            var log = Log();
            Assert.Contains("install --id Valve.Steam --exact --source winget --silent --accept-package-agreements --accept-source-agreements --disable-interactivity", log);
            Assert.Contains("install --id 9MV0B5HZVK9Z --exact --source msstore", log);
            Assert.DoesNotContain("pirate", log);
            Assert.DoesNotContain("download", log);
        }

        [Fact]
        public void Une_application_deja_installee_est_ignoree()
        {
            var r = Apply("launchers", new[] { "steam" }, Doubles(true, 0, "CN=Valve Corp.", "Steam"));
            Assert.Contains("##GT|skip|ins.launchers|déjà installée(s) : Steam", r);
            Assert.Equal("", Log());
        }

        [Fact]
        public void Sans_winget_le_lien_direct_est_telecharge_verifie_puis_installe_en_silence()
        {
            var r = Apply("launchers", new[] { "steam" }, Doubles(false));
            Assert.Contains("##GT|ok|ins.launchers|1 installée(s) : Steam", r);
            var log = Log();
            Assert.Contains("download https://cdn.akamai.steamstatic.com/client/installer/SteamSetup.exe", log);
            Assert.Contains("run steam-setup.exe exe /S", log);
        }

        [Fact]
        public void Une_signature_d_un_autre_editeur_bloque_l_installation()
        {
            var r = Apply("launchers", new[] { "steam" }, Doubles(false, 0, "CN=Pirate Ltd"));
            Assert.Contains("##GT|fail|ins.launchers", r);
            Assert.Contains("signature", r);
            Assert.DoesNotContain("run ", Log());
            Assert.Empty(Directory.GetDirectories(tmp));   // dossier de téléchargement supprimé
        }

        [Fact]
        public void Si_winget_echoue_le_lien_direct_prend_le_relais()
        {
            var r = Apply("launchers", new[] { "steam" }, Doubles(true, 1));
            Assert.Contains("##GT|ok|ins.launchers|1 installée(s) : Steam", r);
            var log = Log();
            Assert.Contains("--id Valve.Steam", log);
            Assert.Contains("run steam-setup.exe", log);
        }

        [Fact]
        public void Sans_winget_ni_lien_direct_la_page_officielle_est_indiquee()
        {
            var r = Apply("marques", new[] { "nvidia-app" }, Doubles(false));
            Assert.Contains("##GT|skip|ins.marques|à installer depuis la page officielle", r);
            Assert.Contains("https://www.nvidia.com/fr-fr/software/nvidia-app/", r);
        }

        [Fact]
        public void Un_msi_passe_par_msiexec_et_un_echec_winget_sans_lien_est_signale()
        {
            var r = Apply("launchers", new[] { "epic" }, Doubles(false, 0, "CN=Epic Games Inc., O=Epic Games Inc."));
            Assert.Contains("##GT|ok|ins.launchers", r);
            Assert.Contains("run epic-setup.msi msi", Log());

            var r2 = Apply("launchers", new[] { "gog" }, Doubles(true, 1));
            Assert.Contains("##GT|fail|ins.launchers", r2);
            Assert.Contains("GOG Galaxy (winget code 1", r2);
            Assert.Contains("https://www.gog.com/fr/galaxy", r2);
        }

        [Fact]
        public void Sans_application_choisie_l_option_est_ignoree()
        {
            Assert.Contains("##GT|skip|ins.applis|Aucune application choisie.", Apply("applis", new string[0], Doubles()));
        }
    }
}
