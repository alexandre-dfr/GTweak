using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using GamingTweaker.Tests.Helpers;
using GamingTweaker.Ui;
using Microsoft.Win32;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>
    /// Menu Nettoyage. Les suppressions se testent dans un dossier temporaire avec $AllowedRoots injecté ; le registre sous
    /// HKCU:\Software\GamingTweakerTest ; l'énumération de Windows Installer est factice. Aucun script réel ne vise un dossier système.
    /// </summary>
    public class NettoyageTests : IDisposable
    {
        private const string Menu = "Nettoyage";
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "GTCln-" + Guid.NewGuid().ToString("N"));
        private readonly string sub = @"Software\GamingTweakerTest\Cln-" + Guid.NewGuid().ToString("N");

        public NettoyageTests() { Directory.CreateDirectory(tmp); }

        public void Dispose()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(sub, false); } catch { }
            try { foreach (var f in Directory.GetFiles(tmp, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); } catch { }
            try { Directory.Delete(tmp, true); } catch { }
        }

        // ---- aides --------------------------------------------------------------------------------

        private string Dir(params string[] parts) { var p = Path.Combine(new[] { tmp }.Concat(parts).ToArray()); Directory.CreateDirectory(p); return p; }
        private string Fichier(string dossier, string nom, int octets = 10)
        {
            Directory.CreateDirectory(dossier);
            var p = Path.Combine(dossier, nom);
            File.WriteAllBytes(p, new byte[octets]);
            return p;
        }
        private static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        private static string Un(string snippet) => string.Join("\n", PsCommon.Eval(snippet));
        private static string Roots(params string[] roots) => "$AllowedRoots = @(" + string.Join(",", roots.Select(Q)) + ")\n";

        private static Dictionary<string, CatalogItem> Catalogue() =>
            GamingTweaker.Core.Catalogue.LoadEmbedded().Where(i => i.Menu == Menu).ToDictionary(i => i.Id);

        private static CatalogItem Factice(string id, string apply) =>
            new CatalogItem { Id = id, Menu = Menu, Groupe = "G", Titre = "T", Risque = RiskLevel.Sur, Detect = "d", Apply = apply };

        private static CatalogItem AvecPrefixe(CatalogItem it, string prefixe) =>
            new CatalogItem { Id = it.Id, Menu = it.Menu, Groupe = it.Groupe, Titre = it.Titre, Risque = it.Risque, Detect = it.Detect, Apply = prefixe + "\r\n" + it.Apply, Revert = it.Revert };

        private async Task<ItemOutcome> Lance(CatalogItem it, string journal = null, string quarantaine = null)
        {
            var script = ScriptBuilder.Build(new[] { it }, new Selection { OptionIds = new HashSet<string> { it.Id } },
                journal ?? Path.Combine(tmp, "Sessions", "s1", "journal.jsonl"), quarantaine ?? Path.Combine(tmp, "Quarantaine"));
            var r = await Runner.RunAsync(script, null, CancellationToken.None, _ => 120);
            return r.Items.Single();
        }

        private static void Jonction(string lien, string cible)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + lien + "\" \"" + cible + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(); Assert.Equal(0, p.ExitCode); }
        }

        // ---- Remove-TweakPath ---------------------------------------------------------------------

        [Fact]
        public void Chemin_hors_liste_refuse()
        {
            var ok = Dir("ok"); var hors = Dir("hors"); var cousin = Dir("ok-cousin");
            var fh = Fichier(hors, "a.txt"); var fc = Fichier(cousin, "b.txt"); var fo = Fichier(ok, "c.txt");
            var r = Un(Roots(ok) + "foreach ($p in @(" + Q(hors) + ", " + Q(cousin) + ", " + Q(Path.Combine(ok, @"..\hors")) + ")) {\n"
                     + "  try { Remove-TweakPath -Path $p | Out-Null; Write-Output 'SUPPRIME' } catch { Write-Output 'REFUSE' }\n}\n"
                     + "Remove-TweakPath -Path " + Q(ok) + " | Out-Null\n");
            Assert.Equal("REFUSE\nREFUSE\nREFUSE", r);
            Assert.True(File.Exists(fh));
            Assert.True(File.Exists(fc), "préfixe sans séparateur : « ok-cousin » ne fait pas partie de « ok »");
            Assert.False(File.Exists(fo));
        }

        [Fact]
        public void Un_chemin_refuse_n_empeche_pas_le_refus_global_avant_toute_suppression()
        {
            var ok = Dir("ok"); var hors = Dir("hors");
            var fo = Fichier(ok, "c.txt"); Fichier(hors, "a.txt");
            Un(Roots(ok) + "try { Remove-TweakPath -Path @(" + Q(ok) + ", " + Q(hors) + ") | Out-Null } catch { }");
            Assert.True(File.Exists(fo), "tous les chemins sont vérifiés avant de supprimer quoi que ce soit");
        }

        [Fact]
        public void Jonction_non_suivie()
        {
            var racine = Dir("racine"); var cible = Dir("cible");
            var cibleDeLaJonction = Fichier(cible, "precieux.txt");
            Fichier(racine, "temp.tmp");
            Jonction(Path.Combine(racine, "lien"), cible);
            var r = Un(Roots(racine) + "$s = Remove-TweakPath -Path " + Q(racine) + "\nWrite-Output ($s.Files)");
            Assert.True(File.Exists(cibleDeLaJonction), "le contenu de la cible de la jonction doit rester");
            Assert.False(File.Exists(Path.Combine(racine, "temp.tmp")));
            Assert.Equal("1", r);
        }

        [Fact]
        public void Une_racine_qui_est_une_jonction_est_refusee()
        {
            var cible = Dir("cible"); var f = Fichier(cible, "precieux.txt");
            var lien = Path.Combine(tmp, "lienracine"); Jonction(lien, cible);
            var r = Un(Roots(lien) + "try { Remove-TweakPath -Path " + Q(lien) + " | Out-Null; 'SUPPRIME' } catch { 'REFUSE' }");
            Assert.Equal("REFUSE", r);
            Assert.True(File.Exists(f));
        }

        [Fact]
        public void Le_dossier_racine_est_conserve_sauf_demande_explicite()
        {
            var racine = Dir("racine"); Fichier(racine, "a.tmp"); Fichier(Dir("racine", "sous", "profond"), "b.tmp");
            Un(Roots(racine) + "Remove-TweakPath -Path " + Q(racine) + " | Out-Null");
            Assert.True(Directory.Exists(racine));
            Assert.Empty(Directory.GetFileSystemEntries(racine));

            Fichier(racine, "c.tmp");
            Un(Roots(racine) + "Remove-TweakPath -Path " + Q(racine) + " -RemoveRoot | Out-Null");
            Assert.False(Directory.Exists(racine));
        }

        [Fact]
        public void Filtre_et_non_recursif_ne_touchent_que_les_fichiers_voulus()
        {
            var d = Dir("explorer");
            var t1 = Fichier(d, "thumbcache_96.db"); var t2 = Fichier(d, "thumbcache_idx.db"); var autre = Fichier(d, "iconcache_32.db");
            var profond = Fichier(Dir("explorer", "sous"), "thumbcache_256.db");
            Un(Roots(d) + "Remove-TweakPath -Path " + Q(d) + " -Filter 'thumbcache_*.db' -NoRecurse | Out-Null");
            Assert.False(File.Exists(t1)); Assert.False(File.Exists(t2));
            Assert.True(File.Exists(autre)); Assert.True(File.Exists(profond));
        }

        [Fact]
        public void Un_fichier_en_lecture_seule_est_supprime()
        {
            var d = Dir("ro"); var f = Fichier(d, "ro.tmp"); File.SetAttributes(f, FileAttributes.ReadOnly);
            Un(Roots(d) + "Remove-TweakPath -Path " + Q(d) + " | Out-Null");
            Assert.False(File.Exists(f));
        }

        [Fact]
        public void Les_donnees_personnelles_sont_protegees_meme_dans_un_dossier_autorise()
        {
            var racine = Dir("racine");
            var perso = new[] { Fichier(Dir("racine", "Downloads"), "a.zip"), Fichier(Dir("racine", "Documents"), "b.docx"), Fichier(Dir("racine", "Desktop"), "c.lnk"),
                                Fichier(Dir("racine", "Pictures"), "d.png"), Fichier(Dir("racine", "Default"), "Login Data"), Fichier(Dir("racine", "Default"), "History"),
                                Fichier(Dir("racine", "Default"), "Cookies"), Fichier(Dir("racine", "Default"), "Bookmarks"), Fichier(Dir("racine", "Default"), "Web Data"),
                                Fichier(Dir("racine", "Saved Games"), "save.sav") };
            var jetable = Fichier(Dir("racine", "Default", "Cache"), "f_000001");
            var r = Un(Roots(racine) + "$s = Remove-TweakPath -Path " + Q(racine) + "\nWrite-Output $s.Files");
            foreach (var p in perso) Assert.True(File.Exists(p), p + " doit rester");
            Assert.False(File.Exists(jetable));
            Assert.Equal("1", r);
        }

        [Fact]
        public async Task Fichier_verrouille_ne_fait_pas_echouer_l_option()
        {
            var racine = Dir("racine"); var libre = Fichier(racine, "libre.tmp"); var pris = Fichier(racine, "pris.tmp"); var pris2 = Fichier(racine, "pris2.tmp");
            var apply = Roots(racine)
                      + "$h1 = [IO.File]::Open(" + Q(pris) + ", 'Open', 'ReadWrite', 'None')\n$h2 = [IO.File]::Open(" + Q(pris2) + ", 'Open', 'ReadWrite', 'None')\n"
                      + "try { Remove-TweakPath -Path " + Q(racine) + " | Out-Null } finally { $h1.Dispose(); $h2.Dispose() }";
            var o = await Lance(Factice("cln.test", apply));
            Assert.Equal(OutcomeStatus.Ok, o.Status);
            Assert.Contains("2 fichiers verrouillés ignorés", o.Message);
            Assert.False(File.Exists(libre));
            Assert.True(File.Exists(pris));
        }

        [Fact]
        public async Task La_note_resume_la_taille_liberee()
        {
            var racine = Dir("racine"); Fichier(racine, "a.tmp", 2048); Fichier(Dir("racine", "x"), "b.tmp", 2048);
            var o = await Lance(Factice("cln.test", Roots(racine) + "Remove-TweakPath -Path " + Q(racine) + " | Out-Null"));
            Assert.Equal(OutcomeStatus.Ok, o.Status);
            Assert.Contains("4,0 Ko", o.Message);
        }

        // ---- Get-TweakPathSize --------------------------------------------------------------------

        [Fact]
        public void Estimation_renvoie_la_taille()
        {
            var d = Dir("est"); Fichier(d, "a.bin", 1024);
            Assert.Equal("1024", Un(Roots(d) + "Get-TweakPathSize -Path " + Q(d)));
            Assert.True(File.Exists(Path.Combine(d, "a.bin")), "l'estimation ne supprime rien");

            Fichier(Dir("est", "sous"), "b.bin", 500); Fichier(d, "thumbcache_1.db", 100);
            Assert.Equal("1624", Un(Roots(d) + "Get-TweakPathSize -Path " + Q(d)));
            Assert.Equal("100", Un(Roots(d) + "Get-TweakPathSize -Path " + Q(d) + " -Filter 'thumbcache_*.db' -NoRecurse"));
            Assert.Equal("0", Un(Roots(d) + "Get-TweakPathSize -Path " + Q(Path.Combine(d, "absent"))));
        }

        [Fact]
        public void Estimation_ne_suit_pas_les_jonctions_et_refuse_les_chemins_hors_liste()
        {
            var racine = Dir("racine"); var cible = Dir("cible"); Fichier(cible, "gros.bin", 5000); Fichier(racine, "a.bin", 7);
            Jonction(Path.Combine(racine, "lien"), cible);
            Assert.Equal("7", Un(Roots(racine) + "Get-TweakPathSize -Path " + Q(racine)));
            Assert.Equal("REFUSE", Un(Roots(racine) + "try { Get-TweakPathSize -Path " + Q(cible) + " } catch { 'REFUSE' }"));
        }

        [Fact]
        public void Les_tailles_sont_formatees_en_francais()
        {
            Assert.Equal("0 o", Estimation.FormatTaille(0));
            Assert.Equal("512 o", Estimation.FormatTaille(512));
            Assert.Equal("1,0 Ko", Estimation.FormatTaille(1024));
            Assert.Equal("1,5 Mo", Estimation.FormatTaille(1572864));
            Assert.Equal("2,3 Go", Estimation.FormatTaille(2469606195));
            Assert.Equal(Estimation.FormatTaille(1572864), Un("Format-GTSize 1572864"));
            Assert.Equal(Estimation.FormatTaille(2469606195), Un("Format-GTSize 2469606195"));
        }

        // ---- Estimation (C#) ----------------------------------------------------------------------

        private static CatalogItem AvecEstimation(string id, string script) => new CatalogItem { Id = id, Menu = Menu, Groupe = "G", Titre = "T", Risque = RiskLevel.Sur, Estimate = script };

        [Fact]
        public async Task Estimation_lit_les_octets_et_met_null_en_cas_d_echec_ou_de_script_absent()
        {
            var items = new[]
            {
                AvecEstimation("e.ok", "1024"),
                AvecEstimation("e.bruit", "Write-Output 'bruit'; [long]2048"),
                AvecEstimation("e.erreur", "throw 'boum'"),
                AvecEstimation("e.texte", "'pas un nombre'"),
                AvecEstimation("e.negatif", "-5"),
                AvecEstimation("e.vide", "$null"),
                AvecEstimation("e.apres", "7"),
                AvecEstimation("e.absent", null),
            };
            var r = await Estimation.RunAsync(items);
            Assert.Equal(1024L, r["e.ok"]);
            Assert.Equal(2048L, r["e.bruit"]);
            Assert.Null(r["e.erreur"]); Assert.Null(r["e.texte"]); Assert.Null(r["e.negatif"]); Assert.Null(r["e.vide"]);
            Assert.Equal(7L, r["e.apres"]);
            Assert.Null(r["e.absent"]);
            Assert.Equal(8, r.Count);
        }

        [Fact]
        public async Task Estimation_a_un_delai_global_et_garde_les_resultats_deja_obtenus()
        {
            var items = new[] { AvecEstimation("e.rapide", "42"), AvecEstimation("e.lent", "Start-Sleep -Seconds 60; 1"), AvecEstimation("e.apres", "9") };
            var sw = Stopwatch.StartNew();
            var r = await Estimation.RunAsync(items, TimeSpan.FromSeconds(8));
            sw.Stop();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(40), "délai dépassé : " + sw.Elapsed);
            Assert.Equal(42L, r["e.rapide"]);
            Assert.Null(r["e.lent"]);
            Assert.Null(r["e.apres"]);
        }

        [Fact]
        public async Task Estimation_sans_aucun_script_ne_lance_rien()
        {
            var r = await Estimation.RunAsync(new[] { AvecEstimation("e.absent", null) });
            Assert.Null(r["e.absent"]);
        }

        // ---- Aperçu : colonne Taille et total -----------------------------------------------------

        private MainViewModel Vm(Func<IEnumerable<CatalogItem>, Task<Dictionary<string, long?>>> estimateur, params CatalogItem[] items)
        {
            var paths = new DataPaths { ProfilsDir = Path.Combine(tmp, "Profils"), MachineDir = Path.Combine(tmp, "M"), SessionsDir = Path.Combine(tmp, "M", "Sessions"), QuarantineDir = Path.Combine(tmp, "Q") };
            return new MainViewModel(items.ToList(), new MachineFacts { WindowsMajor = "10", Edition = "Pro" }, paths,
                its => Task.FromResult(its.ToDictionary(i => i.Id, i => DetectState.NotApplied)), null, o => true, null, estimateur);
        }

        [Fact]
        public async Task Apercu_affiche_les_tailles_estimees_et_le_total()
        {
            var a = AvecEstimation("cln.a", "1024"); a.Apply = "x"; a.Detect = "d"; a.Description = "d";
            var b = AvecEstimation("cln.b", "3072"); b.Apply = "x"; b.Detect = "d"; b.Description = "d";
            var c = AvecEstimation("cln.c", null); c.Apply = "x"; c.Detect = "d"; c.Description = "d";
            var d = AvecEstimation("cln.d", "x"); d.Apply = "x"; d.Detect = "d"; d.Description = "d";
            var vm = Vm(its => Task.FromResult(its.ToDictionary(i => i.Id, i => i.Id == "cln.d" ? (long?)null : i.Id == "cln.a" ? 1024L : i.Id == "cln.b" ? 3072L : (long?)null)), a, b, c, d);
            foreach (var o in vm.Options) o.IsChecked = true;
            var lignes = vm.BuildPreview();
            Assert.All(lignes, l => Assert.Equal("—", l.Taille));
            var total = await vm.EstimerAsync(lignes);
            Assert.Equal("1,0 Ko", lignes.Single(l => l.Id == "cln.a").Taille);
            Assert.Equal("3,0 Ko", lignes.Single(l => l.Id == "cln.b").Taille);
            Assert.Equal("—", lignes.Single(l => l.Id == "cln.c").Taille);
            Assert.Equal("—", lignes.Single(l => l.Id == "cln.d").Taille);
            Assert.Contains("4,0 Ko", total);
            Assert.Contains("Total", total);
        }

        [Fact]
        public async Task Apercu_sans_estimation_disponible_affiche_des_tirets()
        {
            var a = AvecEstimation("cln.a", "1"); a.Apply = "x"; a.Detect = "d"; a.Description = "d";
            var vm = Vm(its => { throw new InvalidOperationException("boum"); }, a);
            vm.Options[0].IsChecked = true;
            var lignes = vm.BuildPreview();
            var total = await vm.EstimerAsync(lignes);
            Assert.Equal("—", lignes.Single().Taille);
            Assert.Equal("", total);
        }

        // ---- Orphelins de Windows Installer -------------------------------------------------------

        private string InstallerFactice(out string sousDossierPatchCache)
        {
            var inst = Dir("Installer");
            Fichier(inst, "aaaa1.msi"); Fichier(inst, "bbbb2.msp"); Fichier(inst, "Orphelin.MSI"); Fichier(inst, "autre.txt");
            sousDossierPatchCache = Dir("Installer", "$PatchCache$", "Managed");
            Fichier(sousDossierPatchCache, "cache.msi");
            return inst;
        }

        private string PrefixeInstaller(string inst, string enumerateur) =>
            "$GT_InstallerDir = " + Q(inst) + "\n$GT_InstallerEnumerator = { " + enumerateur + " }\n";

        [Fact]
        public async Task Orphelins_sans_liste_installer_ne_deplace_rien()
        {
            string pc; var inst = InstallerFactice(out pc);
            var it = AvecPrefixe(Catalogue()["cln.orphans"], PrefixeInstaller(inst, "@()"));
            var o = await Lance(it);
            Assert.Equal(OutcomeStatus.Failed, o.Status);
            Assert.True(File.Exists(Path.Combine(inst, "bbbb2.msp")));
            Assert.True(File.Exists(Path.Combine(inst, "Orphelin.MSI")));
            Assert.False(Directory.Exists(Path.Combine(tmp, "Quarantaine")) && Directory.GetFiles(Path.Combine(tmp, "Quarantaine")).Length > 0);
            Assert.False(File.Exists(Path.Combine(tmp, "Sessions", "s1", "journal.jsonl")), "rien n'est journalisé");
        }

        [Fact]
        public async Task Orphelins_enumeration_en_erreur_ne_deplace_rien()
        {
            string pc; var inst = InstallerFactice(out pc);
            var o = await Lance(AvecPrefixe(Catalogue()["cln.orphans"], PrefixeInstaller(inst, "throw 'COM indisponible'")));
            Assert.Equal(OutcomeStatus.Failed, o.Status);
            Assert.True(File.Exists(Path.Combine(inst, "bbbb2.msp")));
            Assert.True(File.Exists(Path.Combine(inst, "aaaa1.msi")));
        }

        [Fact]
        public async Task Orphelins_deplace_en_quarantaine_et_jamais_les_fichiers_utilises_ni_les_sous_dossiers()
        {
            string pc; var inst = InstallerFactice(out pc);
            var utilises = Q(Path.Combine(inst, "aaaa1.msi").ToUpperInvariant()) + ", " + Q(Path.Combine(inst, "bbbb2.msp"));
            var o = await Lance(AvecPrefixe(Catalogue()["cln.orphans"], PrefixeInstaller(inst, "@(" + utilises + ")")));
            Assert.Equal(OutcomeStatus.Ok, o.Status);
            Assert.True(File.Exists(Path.Combine(inst, "aaaa1.msi")), "fichier utilisé (casse différente)");
            Assert.True(File.Exists(Path.Combine(inst, "bbbb2.msp")));
            Assert.False(File.Exists(Path.Combine(inst, "Orphelin.MSI")));
            Assert.True(File.Exists(Path.Combine(inst, "autre.txt")));
            Assert.True(File.Exists(Path.Combine(pc, "cache.msi")), "$PatchCache$ n'est jamais touché");
            var q = Directory.GetFiles(Path.Combine(tmp, "Quarantaine"));
            Assert.Single(q);
            Assert.EndsWith("_Orphelin.MSI", q[0]);
            Assert.Contains("\"kind\":\"quarantine\"", File.ReadAllText(Path.Combine(tmp, "Sessions", "s1", "journal.jsonl")));
        }

        [Fact]
        public async Task Orphelins_est_restaurable_depuis_le_journal()
        {
            string pc; var inst = InstallerFactice(out pc);
            var journal = Path.Combine(tmp, "Sessions", "s1", "journal.jsonl");
            var o = await Lance(AvecPrefixe(Catalogue()["cln.orphans"], PrefixeInstaller(inst, "@(" + Q(Path.Combine(inst, "aaaa1.msi")) + ")")), journal);
            Assert.Equal(OutcomeStatus.Ok, o.Status);
            Assert.False(File.Exists(Path.Combine(inst, "bbbb2.msp")));
            var script = UndoService.BuildScript(journal, new[] { "cln.orphans" });
            var r = await Runner.RunAsync(script, null, CancellationToken.None, _ => 120);
            Assert.Equal(OutcomeStatus.Ok, r.Items.Single().Status);
            Assert.True(File.Exists(Path.Combine(inst, "bbbb2.msp")));
            Assert.True(File.Exists(Path.Combine(inst, "Orphelin.MSI")));
        }

        [Fact]
        public async Task Orphelins_dossier_sans_msi_ni_msp_est_ignore()
        {
            var inst = Dir("Installer"); Fichier(inst, "lisez-moi.txt");
            var o = await Lance(AvecPrefixe(Catalogue()["cln.orphans"], PrefixeInstaller(inst, "@()")));
            Assert.Equal(OutcomeStatus.Skipped, o.Status);
        }

        [Fact]
        public void L_enumeration_reelle_de_Windows_Installer_est_en_lecture_seule_et_renvoie_des_msi()
        {
            // Lecture seule : aucune suppression. Le SID « tout le monde » exige les droits administrateur ; le test lit l'utilisateur courant.
            if (!Directory.Exists(Path.Combine(Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows", "Installer"))) return;
            var r = Un("$GT_InstallerUserSid = ''
$l = @(Get-GTInstallerReferencedPackages)
Write-Output $l.Count\nWrite-Output (($l | Where-Object { $_ -notmatch '\\.(msi|msp)$' }).Count)");
            var lignes = r.Split('\n');
            Assert.True(int.Parse(lignes[0]) > 0, "aucun paquet référencé");
            Assert.Equal("0", lignes[1]);
        }

        // ---- Registre -----------------------------------------------------------------------------

        private string CleUn(string nom) => @"HKCU:\" + sub + @"\Uninstall\" + nom;
        private string RacineUn => @"HKCU:\" + sub + @"\Uninstall";
        private string Mui => @"HKCU:\" + sub + @"\MuiCache";

        private void Entree(string nom, string uninstallString, int? windowsInstaller = null)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(sub + @"\Uninstall\" + nom))
            {
                k.SetValue("DisplayName", nom);
                if (uninstallString != null) k.SetValue("UninstallString", uninstallString);
                if (windowsInstaller != null) k.SetValue("WindowsInstaller", windowsInstaller.Value, RegistryValueKind.DWord);
            }
        }
        private bool CleExiste(string nom) { using (var k = Registry.CurrentUser.OpenSubKey(sub + @"\Uninstall\" + nom)) return k != null; }
        private bool MuiExiste(string nom) { using (var k = Registry.CurrentUser.OpenSubKey(sub + @"\MuiCache")) return k != null && k.GetValue(nom) != null; }

        private static string LettreAbsente() => "ZYXWVUTSRQPONMLKJIHGFED".Select(c => c.ToString()).First(l => !Directory.Exists(l + @":\"));

        private string PrefixeRegistre() => "$GT_UninstallRoots = @(" + Q(RacineUn) + ")\n$GT_MuiCachePath = " + Q(Mui) + "\n";

        [Fact]
        public async Task Registre_garde_une_entree_dont_le_programme_existe()
        {
            var exe = Fichier(Dir("Programme"), "prog.exe");
            Entree("Present", "\"" + exe + "\" /uninstall");
            Entree("PresentSansGuillemets", exe + " /S");
            Entree("Disparu", "\"" + Path.Combine(tmp, "Disparu", "unins000.exe") + "\" /SILENT");
            Entree("DisparuSansGuillemets", Path.Combine(tmp, "Disparu", "remove.exe") + " /S");
            Entree("InstallerMsi", @"MsiExec.exe /X{11111111-2222-3333-4444-555555555555}");
            Entree("MsiDisparu", "\"" + Path.Combine(tmp, "Disparu", "x.exe") + "\"", 1);
            Entree("Relatif", "uninstall.exe /S");
            Entree("Rundll", @"rundll32.exe setupapi.dll,InstallHinfSection DefaultUninstall 132 foo.inf");
            Entree("SansChaine", null);
            Entree("LecteurAbsent", "\"" + LettreAbsente() + @":\Programmes\unins.exe""");
            var o = await Lance(AvecPrefixe(Catalogue()["cln.registry"], PrefixeRegistre()));
            Assert.Equal(OutcomeStatus.Ok, o.Status);
            foreach (var k in new[] { "Present", "PresentSansGuillemets", "InstallerMsi", "MsiDisparu", "Relatif", "Rundll", "SansChaine", "LecteurAbsent" })
                Assert.True(CleExiste(k), k + " doit rester");
            Assert.False(CleExiste("Disparu"));
            Assert.False(CleExiste("DisparuSansGuillemets"));
        }

        [Fact]
        public async Task Registre_exporte_les_cles_retirees_et_les_restaure_par_l_annulation()
        {
            Entree("Disparu", "\"" + Path.Combine(tmp, "Disparu", "unins000.exe") + "\"");
            var journal = Path.Combine(tmp, "Sessions", "s1", "journal.jsonl");
            var o = await Lance(AvecPrefixe(Catalogue()["cln.registry"], PrefixeRegistre()), journal);
            Assert.Equal(OutcomeStatus.Ok, o.Status);
            Assert.False(CleExiste("Disparu"));
            Assert.Contains("\"kind\":\"regkey\"", File.ReadAllText(journal));
            Assert.NotEmpty(Directory.GetFiles(Path.GetDirectoryName(journal), "regkey-*.reg"));

            var r = await Runner.RunAsync(UndoService.BuildScript(journal, new[] { "cln.registry" }), null, CancellationToken.None, _ => 120);
            Assert.Equal(OutcomeStatus.Ok, r.Items.Single().Status);
            Assert.True(CleExiste("Disparu"));
        }

        [Fact]
        public async Task Registre_export_echoue_aucune_suppression()
        {
            Entree("Disparu1", "\"" + Path.Combine(tmp, "Disparu", "a.exe") + "\"");
            Entree("Disparu2", "\"" + Path.Combine(tmp, "Disparu", "b.exe") + "\"");
            using (var k = Registry.CurrentUser.CreateSubKey(sub + @"\MuiCache")) k.SetValue(Path.Combine(tmp, "Disparu", "c.exe") + ".FriendlyAppName", "C");
            var prefixe = PrefixeRegistre() + "function Export-GTRegistryKey([string]$Path, [string]$File) { throw 'export impossible' }\n";
            var o = await Lance(AvecPrefixe(Catalogue()["cln.registry"], prefixe));
            Assert.Equal(OutcomeStatus.Failed, o.Status);
            Assert.True(CleExiste("Disparu1")); Assert.True(CleExiste("Disparu2"));
            Assert.True(MuiExiste(Path.Combine(tmp, "Disparu", "c.exe") + ".FriendlyAppName"));
        }

        [Fact]
        public async Task Registre_MuiCache_retire_les_valeurs_dont_le_fichier_a_disparu_avec_journal()
        {
            var present = Fichier(Dir("Programme"), "vivant.exe");
            var perdu = Path.Combine(tmp, "Perdu", "mort.exe");
            using (var k = Registry.CurrentUser.CreateSubKey(sub + @"\MuiCache"))
            {
                k.SetValue(present + ".FriendlyAppName", "Vivant");
                k.SetValue(present + ".ApplicationCompany", "Vivant SA");
                k.SetValue(perdu + ".FriendlyAppName", "Mort");
                k.SetValue(perdu + ".ApplicationCompany", "Mort SA");
                k.SetValue("LangID", 1036, RegistryValueKind.DWord);
                k.SetValue("@shell32.dll,-21769", "Aide");
                k.SetValue(LettreAbsente() + @":\x\y.exe.FriendlyAppName", "Lecteur absent");
            }
            var journal = Path.Combine(tmp, "Sessions", "s1", "journal.jsonl");
            var o = await Lance(AvecPrefixe(Catalogue()["cln.registry"], PrefixeRegistre()), journal);
            Assert.Equal(OutcomeStatus.Ok, o.Status);
            Assert.True(MuiExiste(present + ".FriendlyAppName")); Assert.True(MuiExiste(present + ".ApplicationCompany"));
            Assert.False(MuiExiste(perdu + ".FriendlyAppName")); Assert.False(MuiExiste(perdu + ".ApplicationCompany"));
            Assert.True(MuiExiste("LangID")); Assert.True(MuiExiste("@shell32.dll,-21769"));
            Assert.True(MuiExiste(LettreAbsente() + @":\x\y.exe.FriendlyAppName"));

            var r = await Runner.RunAsync(UndoService.BuildScript(journal, new[] { "cln.registry" }), null, CancellationToken.None, _ => 120);
            Assert.Equal(OutcomeStatus.Ok, r.Items.Single().Status);
            Assert.True(MuiExiste(perdu + ".FriendlyAppName"));
            Assert.True(MuiExiste(perdu + ".ApplicationCompany"));
        }

        [Fact]
        public async Task Registre_rien_a_nettoyer_est_ignore()
        {
            Entree("Present", "\"" + Fichier(Dir("P"), "p.exe") + "\"");
            var o = await Lance(AvecPrefixe(Catalogue()["cln.registry"], PrefixeRegistre()));
            Assert.Equal(OutcomeStatus.Skipped, o.Status);
            Assert.True(CleExiste("Present"));
        }

        [Fact]
        public void Les_racines_du_registre_par_defaut_sont_uniquement_les_cles_Uninstall_et_MuiCache()
        {
            var r = Un("Write-Output ($GT_UninstallRoots -join '|')\nWrite-Output $GT_MuiCachePath");
            var l = r.Split('\n');
            Assert.Equal(@"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall|HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall|HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall", l[0]);
            Assert.Equal(@"HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache", l[1]);
        }

        // ---- Variables Common.ps1 -----------------------------------------------------------------

        [Fact]
        public void Les_dossiers_autorises_par_defaut_ne_contiennent_ni_racine_de_disque_ni_profil_ni_donnees_personnelles()
        {
            var r = Un("$AllowedRoots | ForEach-Object { Write-Output $_ }");
            var racines = r.Split('\n').Where(x => x.Length > 0).ToList();
            Assert.True(racines.Count >= 10, "peu de dossiers autorisés : " + racines.Count);
            var profil = Environment.GetEnvironmentVariable("USERPROFILE").TrimEnd('\\');
            foreach (var x in racines)
            {
                Assert.True(x.Length > 3, x);
                Assert.False(string.Equals(profil, x.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase), x);
                Assert.DoesNotMatch(@"(?i)\\(Downloads|Documents|Desktop|Pictures|Videos|Music|OneDrive)(\\|$)", x);
            }
            Assert.Contains(racines, x => x.EndsWith(@"\Windows\Temp", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(racines, x => x.EndsWith(@"\SoftwareDistribution\Download", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Un_TEMP_deregle_n_elargit_pas_la_liste()
        {
            var r = Un("$env:TEMP = $env:USERPROFILE\n$l = @(Get-GTDefaultAllowedRoots)\nWrite-Output ($l -contains $env:USERPROFILE)\n"
                     + "$env:TEMP = 'C:\\'\n$l = @(Get-GTDefaultAllowedRoots)\nWrite-Output ($l -contains 'C:\\')");
            Assert.Equal("False\nFalse", r);
        }

        // ---- Anciens pilotes ----------------------------------------------------------------------

        [Fact]
        public void Anciens_pilotes_garde_le_plus_recent_et_ceux_qui_sont_utilises()
        {
            var r = Un(@"
function D($inf,$orig,$prov,$cls,$ver,$date) { [pscustomobject]@{ Driver=$inf; OriginalFileName=$orig; ProviderName=$prov; ClassName=$cls; Version=$ver; Date=[datetime]$date } }
$all = @(
  (D 'oem1.inf' 'C:\Windows\System32\DriverStore\FileRepository\nv_dispi.inf_amd64_a\nv_dispi.inf' 'NVIDIA' 'Display' '31.0.15.1' '2023-01-01'),
  (D 'oem2.inf' 'C:\Windows\System32\DriverStore\FileRepository\nv_dispi.inf_amd64_b\nv_dispi.inf' 'NVIDIA' 'Display' '31.0.15.4' '2023-06-01'),
  (D 'oem3.inf' 'C:\Windows\System32\DriverStore\FileRepository\nv_dispi.inf_amd64_c\nv_dispi.inf' 'NVIDIA' 'Display' '32.0.15.6' '2024-03-01'),
  (D 'oem4.inf' 'C:\x\rtlnic.inf' 'Realtek' 'Net' '10.1' '2022-01-01'),
  (D 'oem5.inf' 'C:\x\other.inf' 'Autre' 'Net' '1.0' '2020-01-01'),
  (D 'oem6.inf' 'C:\x\other.inf' 'Autre' 'Net' '1.0' '2020-01-01')
)
$old = @(Select-GTOldDrivers -Drivers $all -InUse @('OEM1.INF'))
Write-Output (($old | ForEach-Object { $_.Driver } | Sort-Object) -join ',')");
            // oem1 est utilisé (gardé), oem3 est le plus récent, oem2 est ancien ; oem4 est seul ; oem5/oem6 identiques : un seul est gardé.
            Assert.Equal("oem2.inf,oem5.inf", r);
        }

        // ---- DISM ---------------------------------------------------------------------------------

        [Theory]
        [InlineData(0, "OK")]
        [InlineData(3010, "OK|redemarrage")]
        [InlineData(1, "ECHEC")]
        [InlineData(87, "ECHEC")]
        public void DISM_interprete_le_code_de_sortie(int code, string attendu)
        {
            var fake = Path.Combine(tmp, "dism-factice.cmd");
            File.WriteAllText(fake, "@echo off\r\necho Deployment Image Servicing\r\nexit /b " + code + "\r\n");
            var r = Un("$GT_DismPath = " + Q(fake) + "\ntry { $x = Invoke-GTDism -Arguments @('/Online','/Cleanup-Image','/StartComponentCleanup'); if ($x.RebootRequired) { 'OK|redemarrage' } else { 'OK' } } catch { 'ECHEC' }");
            Assert.Equal(attendu, r);
        }

        // ---- Catalogue ----------------------------------------------------------------------------

        private static readonly Dictionary<string, RiskLevel> Minimum = new Dictionary<string, RiskLevel>
        {
            { "cln.temp", RiskLevel.Sur }, { "cln.recycle", RiskLevel.Sur }, { "cln.thumbs", RiskLevel.Sur }, { "cln.wu-cache", RiskLevel.Sur },
            { "cln.crash", RiskLevel.Sur }, { "cln.logs", RiskLevel.Sur }, { "cln.browsers", RiskLevel.Sur }, { "cln.launchers", RiskLevel.Sur },
            { "cln.dism", RiskLevel.Sur }, { "cln.shaders", RiskLevel.Avance }, { "cln.windows-old", RiskLevel.Avance }, { "cln.old-drivers", RiskLevel.Avance },
            { "cln.orphans", RiskLevel.Avance }, { "cln.resetbase", RiskLevel.Avance }, { "cln.prefetch", RiskLevel.Risque }, { "cln.registry", RiskLevel.Risque },
        };
        private static readonly string[] Reversibles = { "cln.orphans", "cln.registry" };
        private static readonly string[] AvecEstimate =
            { "cln.temp", "cln.recycle", "cln.thumbs", "cln.wu-cache", "cln.crash", "cln.logs", "cln.browsers", "cln.launchers", "cln.shaders", "cln.windows-old",
              "cln.orphans", "cln.prefetch", "cln.recent", "cln.apps-traces" };

        public static IEnumerable<object[]> Lignes() => Minimum.Select(kv => new object[] { kv.Key, kv.Value });

        [Theory]
        [MemberData(nameof(Lignes))]
        public void Chaque_option_du_tableau_existe_avec_le_bon_risque(string id, RiskLevel risque)
        {
            var cat = Catalogue();
            Assert.True(cat.ContainsKey(id), "option absente : " + id);
            var it = cat[id];
            Assert.Equal(risque, it.Risque);
            Assert.Equal(Reversibles.Contains(id), it.Reversible);
            Assert.Equal(it.Reversible, !string.IsNullOrWhiteSpace(it.Revert));
            Assert.False(it.Redemarrage);
            Assert.False(string.IsNullOrWhiteSpace(it.Groupe));
            Assert.False(string.IsNullOrWhiteSpace(it.Titre));
            Assert.False(string.IsNullOrWhiteSpace(it.Description), id + " : description");
            Assert.False(string.IsNullOrWhiteSpace(it.Detect), id + " : detect");
            Assert.False(string.IsNullOrWhiteSpace(it.Apply), id + " : apply");
            Assert.Equal(AvecEstimate.Contains(id), !string.IsNullOrWhiteSpace(it.Estimate));
        }

        [Fact]
        public void Le_catalogue_nettoyage_contient_exactement_le_tableau_et_le_recapitulatif()
        {
            var cat = Catalogue();
            var recap = Couverture.Recapitulatif(Menu);
            Assert.Equal(2, recap.Count);
            Assert.Empty(recap.Select(r => r.Id).Intersect(Minimum.Keys));
            var attendu = new HashSet<string>(Minimum.Keys.Concat(recap.Select(r => r.Id)));
            Assert.Equal(18, attendu.Count);
            Assert.Empty(cat.Keys.Where(k => !attendu.Contains(k)));
            Assert.Empty(attendu.Where(k => !cat.ContainsKey(k)));
            foreach (var r in recap)
            {
                var it = cat[r.Id];
                Assert.True(it.Risque == r.Risque, r.Id + " : risque");
                Assert.True(it.Reversible == r.EstReversible, r.Id + " : réversible différent du récapitulatif");
                Assert.Equal(r.Groupe, it.Groupe);
                Assert.False(it.Reversible);
                Assert.True(string.IsNullOrWhiteSpace(it.Revert));
            }
            foreach (var it in cat.Values) Assert.StartsWith("cln.", it.Id);
        }

        [Fact]
        public void Le_catalogue_complet_est_valide()
        {
            Assert.Empty(GamingTweaker.Core.Catalogue.Validate(GamingTweaker.Core.Catalogue.LoadEmbedded()));
        }

        [Fact]
        public void Les_options_cln_citees_par_la_couverture_existent()
        {
            var cat = Catalogue();
            var cites = Couverture.IdsCites().Where(id => id.StartsWith("cln.", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(cites);
            foreach (var id in cites) Assert.True(cat.ContainsKey(id), id + " cité par la couverture mais absent du catalogue");
        }

        [Fact]
        public void Les_scripts_cln_sont_syntaxiquement_valides()
        {
            Assert.Empty(PsSyntax.CheckAll(Catalogue().Values));
        }

        [Fact]
        public void Les_options_longues_ont_un_delai_adapte()
        {
            var cat = Catalogue();
            Assert.Equal(3600, cat["cln.dism"].DelaiSec);
            Assert.Equal(3600, cat["cln.resetbase"].DelaiSec);
            Assert.Equal(3600, cat["cln.windows-old"].DelaiSec);
            Assert.True(cat["cln.temp"].DelaiSec >= 600);
        }

        [Fact]
        public void Les_textes_disent_ce_qui_devient_impossible()
        {
            var cat = Catalogue();
            Assert.Contains("ne pourront plus être désinstallées", cat["cln.resetbase"].Description);
            Assert.Contains("/ResetBase", cat["cln.resetbase"].Apply);
            Assert.DoesNotContain("/ResetBase", cat["cln.dism"].Apply);
            Assert.Contains("/StartComponentCleanup", cat["cln.dism"].Apply);
            Assert.Contains("retour à la version précédente de Windows", cat["cln.windows-old"].Description);
            Assert.Contains("définitiv", cat["cln.prefetch"].Description);
            Assert.Contains("Ralentit", cat["cln.prefetch"].Description);
            Assert.Contains("quarantaine", cat["cln.orphans"].Description);
            Assert.Contains("export", cat["cln.registry"].Description);
            Assert.Contains("Définitif", cat["cln.recent"].Description);
            Assert.Contains("Définitif", cat["cln.apps-traces"].Description);
        }

        [Fact]
        public void Les_options_nettoyage_n_effacent_que_par_les_fonctions_gardees()
        {
            var interdits = new[] { "Remove-Item", "rmdir", "rd ", "del ", "erase", "[IO.File]::Delete", "[System.IO.File]::Delete", "Directory]::Delete", "Delete(", "Move-Item",
                                    "Invoke-Expression", "iex ", "Set-ItemProperty", "reg delete", "reg.exe", "Format-Volume", "cleanmgr", "takeown", "icacls", "Start-Process" };
            foreach (var it in Catalogue().Values)
            {
                foreach (var m in interdits)
                    Assert.False((it.Apply ?? "").IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0, it.Id + " : apply contient « " + m + " »");
                foreach (var m in new[] { "Remove-", "Move-", "Clear-", "Delete", "Set-", "wevtutil", "dism.exe", "pnputil" })
                    Assert.False((it.Estimate ?? "").IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0, it.Id + " : estimate contient « " + m + " »");
                Assert.DoesNotContain("Remove-Tweak", it.Detect ?? "");
            }
        }

        [Fact]
        public void Navigateurs_ne_cible_jamais_les_donnees_personnelles()
        {
            var it = Catalogue()["cln.browsers"];
            foreach (var s in new[] { it.Apply, it.Estimate, it.Detect })
                foreach (var m in new[] { "Login Data", "History", "Cookies", "Bookmarks", "Web Data" })
                    Assert.DoesNotContain(m, s, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Code Cache", it.Apply);
            Assert.Contains("GPUCache", it.Apply);
            Assert.Contains("cache2", it.Apply);
            Assert.Contains("Chrome", it.Apply);
            Assert.Contains("Edge", it.Apply);
        }

        [Fact]
        public void Aucun_script_ne_reference_un_dossier_personnel()
        {
            var perso = new[] { "Downloads", "Téléchargements", "Documents", "Desktop", "Bureau", "Pictures", "Images", "Videos", "Music", "Musique", "OneDrive", "Saved Games",
                                "Login Data", "History", "Cookies", "Bookmarks", "Web Data", "Favorites", "Contacts", "Key4.db", "logins.json", "places.sqlite" };
            foreach (var it in Catalogue().Values)
                foreach (var s in new[] { it.Detect, it.Apply, it.Revert, it.Estimate })
                    foreach (var m in perso)
                        Assert.False((s ?? "").IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0, it.Id + " : un script contient « " + m + " »");
        }

        [Fact]
        public void Les_options_a_fichiers_passent_par_Remove_TweakPath_et_Get_TweakPathSize()
        {
            var cat = Catalogue();
            foreach (var id in new[] { "cln.temp", "cln.thumbs", "cln.wu-cache", "cln.crash", "cln.logs", "cln.browsers", "cln.launchers", "cln.shaders", "cln.windows-old",
                                       "cln.prefetch", "cln.recent", "cln.apps-traces" })
                Assert.Contains("Remove-TweakPath", cat[id].Apply);
            foreach (var id in AvecEstimate.Where(i => i != "cln.recycle" && i != "cln.orphans" && i != "cln.logs"))
                Assert.Contains("Get-TweakPathSize", cat[id].Estimate);
            Assert.Contains("-RemoveRoot", cat["cln.windows-old"].Apply);
            Assert.Contains("-TakeOwnership", cat["cln.windows-old"].Apply);
            foreach (var it in cat.Values.Where(i => i.Id != "cln.windows-old"))
            {
                Assert.DoesNotContain("-RemoveRoot", it.Apply);
                Assert.DoesNotContain("-TakeOwnership", it.Apply);
            }
        }

        [Fact]
        public void Prefetch_ne_vise_que_les_fichiers_pf_du_dossier()
        {
            var a = Catalogue()["cln.prefetch"].Apply;
            Assert.Contains("*.pf", a);
            Assert.Contains("-NoRecurse", a);
            Assert.Contains(@"Prefetch", a);
        }

        [Fact]
        public void La_corbeille_est_videe_pour_tous_les_lecteurs_et_ignoree_si_elle_est_deja_vide()
        {
            var a = Catalogue()["cln.recycle"].Apply;
            Assert.Contains("Clear-RecycleBin", a);
            Assert.Contains("-Force", a);
            Assert.DoesNotContain("-DriveLetter", a);
            Assert.Contains("Skip-GTItem", a);
            Assert.True(a.IndexOf("Skip-GTItem", StringComparison.Ordinal) < a.IndexOf("Clear-RecycleBin", StringComparison.Ordinal));
        }

        [Fact]
        public void Les_journaux_d_evenements_gardent_le_journal_Securite()
        {
            var a = Catalogue()["cln.logs"].Apply;
            Assert.Contains("wevtutil", a);
            Assert.Contains("Security", a);
            Assert.Contains(@"Logs\CBS", a);
            Assert.Contains(@"Logs\DISM", a);
        }

        [Fact]
        public void Les_anciens_pilotes_sont_supprimes_sans_force()
        {
            var it = Catalogue()["cln.old-drivers"];
            Assert.Contains("pnputil", it.Apply);
            Assert.Contains("/delete-driver", it.Apply);
            Assert.DoesNotContain("/force", it.Apply, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/uninstall", it.Apply, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Select-GTOldDrivers", it.Apply);
        }

        [Fact]
        public void Les_options_ciblant_le_profil_de_l_utilisateur_sont_marquees_hkcu()
        {
            var cat = Catalogue();
            foreach (var it in cat.Values)
                if ((it.Apply ?? "").Contains("HKCU:") || (it.Apply ?? "").Contains("$env:TEMP") || (it.Apply ?? "").Contains("$env:LOCALAPPDATA") || (it.Apply ?? "").Contains("$env:APPDATA"))
                    Assert.True(it.Hkcu, it.Id + " : cible le profil de l'utilisateur mais hkcu=false");
        }
    }
}
