using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GamingTweaker.Core;
using GamingTweaker.Ui;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>
    /// Construit les fenêtres avec des données d'exemple, les affiche hors écran et enregistre des PNG
    /// dans %TEMP%\GamingTweakerShots pour contrôle visuel (aucune interaction, aucun effet système).
    /// </summary>
    public class UiScreenshotTests
    {
        private static readonly string Dossier = Path.Combine(Path.GetTempPath(), "GamingTweakerShots");

        private static CatalogItem It(string id, string menu, string groupe, string titre, string desc, RiskLevel r, bool rev = true, bool redem = false)
            => new CatalogItem { Id = id, Menu = menu, Groupe = groupe, Titre = titre, Description = desc, Risque = r, Reversible = rev, Redemarrage = redem };

        private static List<CatalogItem> Exemples()
        {
            var l = new List<CatalogItem>
            {
                It("upd.auto-off", "Mises à jour", "Windows Update", "Mises à jour automatiques de Windows bloquées", "Stratégie NoAutoUpdate. La mise à jour manuelle continue de fonctionner, mais le PC ne reçoit plus de correctifs de sécurité tant que la stratégie est active.", RiskLevel.Risque),
                It("upd.drivers", "Mises à jour", "Windows Update", "Pilotes exclus de Windows Update", "Windows Update n'installe plus de pilotes à votre place.", RiskLevel.Avance),
                It("upd.winget", "Mises à jour", "Applications", "Mettre à jour les applications avec winget", "Met à jour toutes les applications installées, sauf celles que vous excluez.", RiskLevel.Sur),
                It("tel.sur", "Télémétrie", "Windows", "Télémétrie réduite au minimum", "Réduit les données de diagnostic envoyées à Microsoft.", RiskLevel.Sur),
                It("tel.ads", "Télémétrie", "Windows", "Identifiant publicitaire désactivé", "Empêche les applications de suivre vos usages à des fins publicitaires.", RiskLevel.Sur),
                It("tel.hosts", "Télémétrie", "Domaines", "Domaines de suivi bloqués dans le fichier hosts", "Ajoute une liste de domaines de télémétrie au fichier hosts, avec sauvegarde préalable.", RiskLevel.Avance),
                It("net.temp", "Nettoyage", "Fichiers temporaires", "Fichiers temporaires de Windows supprimés", "Vide les dossiers temporaires du système et de l'utilisateur.", RiskLevel.Sur, false),
                It("net.prefetch", "Nettoyage", "Fichiers temporaires", "Dossier Prefetch vidé", "Fichiers .pf de C:\\Windows\\Prefetch. Ralentit les lancements suivants le temps que Windows reconstruise le cache.", RiskLevel.Risque, false),
                It("opt.mouse", "Optimisation", "Souris et clavier", "Désactiver la précision du pointeur", "Le déplacement du curseur devient proportionnel au mouvement de la souris.", RiskLevel.Sur),
                It("opt.usb", "Optimisation", "Alimentation", "Économie d'énergie USB désactivée", "Empêche Windows de couper l'alimentation des périphériques USB inactifs.", RiskLevel.Sur),
                It("opt.spectre", "Optimisation", "Sécurité", "Protections Spectre et Meltdown désactivées pour gagner quelques pour cent de performances dans certains jeux", "Retire des protections du processeur contre des attaques par canal auxiliaire. Un redémarrage est nécessaire.", RiskLevel.Risque, true, true),
                It("opt.hags", "Optimisation", "Affichage", "Planification GPU accélérée par le matériel activée", "Active HAGS pour réduire la latence sur les cartes compatibles.", RiskLevel.Avance, true, true),
                It("blo.edge", "Bloatware", "Navigateur", "Edge désinstallé", "Méthode de WinUtil, sans suppression du profil, WebView2 conservé. Windows peut le réinstaller.", RiskLevel.Risque),
                It("blo.apps", "Bloatware", "Applications préinstallées", "Applications préinstallées inutiles retirées", "Retire les jeux et applications promotionnelles installés par Microsoft ou le constructeur.", RiskLevel.Avance),
            };
            for (int i = 1; i <= 8; i++)
                l.Add(It("opt.extra" + i, "Optimisation", "Divers", "Réglage supplémentaire n° " + i, "Description d'un réglage supplémentaire pour vérifier le défilement de la liste.", RiskLevel.Sur));
            return l;
        }

        private static void Pump(Dispatcher d) { d.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { })); }

        private static void Capture(FrameworkElement fe, string nom, string dossier = null)
        {
            fe.UpdateLayout();
            var w = (int)Math.Ceiling(fe.ActualWidth); var h = (int)Math.Ceiling(fe.ActualHeight);
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(fe);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(Path.Combine(dossier ?? Dossier, nom + ".png"))) enc.Save(f);
        }

        private static void Prepare(Window w, int largeur, int hauteur)
        {
            w.WindowStyle = WindowStyle.None; w.ResizeMode = ResizeMode.NoResize; w.ShowInTaskbar = false;
            w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = -6000; w.Top = 0;
            w.Width = largeur; w.Height = hauteur;
        }

        [Fact]
        public void Captures_de_tous_les_menus_et_de_l_apercu()
        {
            Directory.CreateDirectory(Dossier);
            foreach (var f in Directory.GetFiles(Dossier, "*.png")) File.Delete(f);
            var erreurs = new List<string>();
            var t = new Thread(() =>
            {
                try { Run(); } catch (Exception ex) { erreurs.Add(ex.ToString()); }
            });
            t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
            Assert.True(erreurs.Count == 0, string.Join(Environment.NewLine, erreurs));
            Assert.Equal(14, Directory.GetFiles(Dossier, "*.png").Length);
        }

        /// <summary>Option à liste dépliée (éléments chargés, un absent du PC) : PNG dans le sous-dossier « liste ».</summary>
        private static void CapturerOptionAListe(Dispatcher d)
        {
            var sousDossier = Path.Combine(Dossier, "liste");
            Directory.CreateDirectory(sousDossier);
            var f = new ListeSelectionTests.Fournisseur();
            f.Entrees.Add(new GamingTweaker.Lists.ListEntry { Key = "HKCU:Run:Discord", Titre = "Discord", Detail = "Utilisateur (registre) : C:/Users/Moi/AppData/Local/Discord/Update.exe --processStart Discord.exe", Risque = RiskLevel.Sur });
            f.Entrees.Add(new GamingTweaker.Lists.ListEntry { Key = "HKLM:Run32:Steam Client", Titre = "Steam Client", Detail = "Tous les utilisateurs (registre, 32 bits) : désactivé", Risque = RiskLevel.Avance });
            var reg = new GamingTweaker.Lists.ListProviderRegistry(); reg.Register(f);
            var items = new List<CatalogItem>
            {
                It("opt.mouse", "Optimisation", "Souris et clavier", "Désactiver la précision du pointeur", "Le déplacement du curseur devient proportionnel au mouvement de la souris.", RiskLevel.Sur),
                new CatalogItem { Id = "opt.startup", Menu = "Optimisation", Groupe = "Démarrage", Titre = "Programmes de démarrage désactivés", Description = "Désactive les programmes choisis au lancement de Windows. Ils restent installés et peuvent être réactivés.", Risque = RiskLevel.Avance, Reversible = true, Liste = "startup" },
                It("opt.usb", "Optimisation", "Démarrage", "Économie d'énergie USB désactivée", "Empêche Windows de couper l'alimentation des périphériques USB inactifs.", RiskLevel.Sur),
            };
            var tmp = Path.Combine(Path.GetTempPath(), "gt-shots-l-" + Guid.NewGuid().ToString("N"));
            var paths = new DataPaths { ProfilsDir = Path.Combine(tmp, "Profils"), MachineDir = tmp, SessionsDir = Path.Combine(tmp, "S"), QuarantineDir = Path.Combine(tmp, "Q") };
            var vm = new MainViewModel(items, new MachineFacts { WindowsMajor = "11", Edition = "Pro" }, paths,
                its => Task.FromResult(its.ToDictionary(i => i.Id, i => DetectState.NotApplied)), null, o => true, reg);
            var w = new MainWindow(vm);
            Prepare(w, 1280, 720);
            vm.MenuCourant = "Optimisation";
            var o2 = vm.Options.Single(o => o.Id == "opt.startup");
            o2.Liste.DepuisSelection(new[] { "HKCU:Run:Mon App", "HKCU:Run:Discord", "HKCU:Run:Ancien programme" });
            o2.IsChecked = true;
            w.Show(); Pump(d);
            o2.BasculerListeCommand.Execute(null);
            for (int i = 0; i < 20 && !o2.Liste.Charge; i++) { Thread.Sleep(50); Pump(d); }
            Pump(d);
            Capture(w, "12-option-liste-depliee", sousDossier);
            if (!o2.Liste.Charge) throw new InvalidOperationException("La liste de l'option n'a pas été chargée.");
            w.Close(); Pump(d);
        }

        /// <summary>Source d'exemple du compteur (aucune session ETW, aucun capteur).</summary>
        private sealed class SourceExemple : ISourceCompteur
        {
            public void Demarrer() { }
            public void Arreter() { }
            public void Choisir(bool cpu, bool gpu, bool ram) { }
            public void ChoisirTemperatures(bool cpu, bool gpu) { }
            public ReleveCompteur Lire() { return null; }
            public string Erreur { get { return null; } }
        }

        /// <summary>Compteur en jeu : réglages (mode placement) et surimpression avec des valeurs d'exemple, PNG dans le sous-dossier « compteur ».</summary>
        private static void CapturerCompteur(Dispatcher d, string tmp)
        {
            var dossier = Path.Combine(Dossier, "compteur");
            Directory.CreateDirectory(dossier);
            var cfg = new CompteurConfig { TempCpu = true, TempGpu = true };
            cfg.DefinirCouleur(ElementCompteur.Cpu, "#FF9F1C");
            var c = new CompteurViewModel(Path.Combine(tmp, "compteur"), new SourceExemple(), cfg);
            for (int i = 0; i < 60; i++)
                c.Rafraichir(new ReleveCompteur
                {
                    Processus = "MonJeu",
                    Images = new MesureImages { Fps = 140 + 10 * Math.Sin(i / 5.0), TempsImageMs = 7.0, UnPourcentBas = 98, Source = "DirectX" },
                    Charge = new ChargeSysteme { Cpu = 34, Gpu = 91, Ram = 52, RamUtiliseeGo = 16.6, RamTotaleGo = 32, TempCpu = 61, TempGpu = 72 },
                });
            var reglages = new CompteurReglagesWindow(c); Prepare(reglages, 560, 900); reglages.SizeToContent = SizeToContent.Height;
            reglages.Show(); Pump(d);
            Capture(reglages, "19-compteur-reglages", dossier);
            var surimpression = new CompteurWindow(c);
            surimpression.Show(); Pump(d);   // ModePlacement : cadre pointillé et poignée visibles
            Capture(surimpression, "20-compteur-placement", dossier);
            reglages.Close(); Pump(d);       // fin du placement : sans cadre
            c.Actif = true; surimpression.Appliquer(); Pump(d);
            Capture(surimpression, "21-compteur", dossier);
            surimpression.Close(); Pump(d);
        }

        /// <summary>Vues des tâches 14 à 16 avec des données d'exemple : PNG dans le sous-dossier « taches-14-16 ».</summary>
        private static void CapturerVuesAvancees(Dispatcher d)
        {
            var sousDossier = Path.Combine(Dossier, "taches-14-16");
            Directory.CreateDirectory(sousDossier);
            var tmp = Path.Combine(Path.GetTempPath(), "gt-shots-v-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var paths = new DataPaths { ProfilsDir = Path.Combine(tmp, "Profils"), MachineDir = tmp, SessionsDir = Path.Combine(tmp, "S"), QuarantineDir = Path.Combine(tmp, "Q") };
            var vm = new MainViewModel(Exemples(), new MachineFacts { WindowsMajor = "11", Edition = "Pro" }, paths,
                its => Task.FromResult(its.ToDictionary(i => i.Id, i => DetectState.NotApplied)), null, o => true);
            vm.ModeJeu = new ModeJeuViewModel(new ModeJeu(tmp, new Faux14()),
                () => new List<ServiceChoix>
                {
                    new ServiceChoix { Nom = "SysMain", Affichage = "SysMain (Superfetch)" },
                    new ServiceChoix { Nom = "WSearch", Affichage = "Windows Search" },
                    new ServiceChoix { Nom = "Spooler", Affichage = "Spouleur d'impression" },
                    new ServiceChoix { Nom = "DiagTrack", Affichage = "Expériences des utilisateurs connectés et télémétrie" },
                    new ServiceChoix { Nom = "XblGameSave", Affichage = "Jeux sauvegardés sur Xbox Live" },
                },
                () => new List<ProcessusChoix>
                {
                    new ProcessusChoix { Nom = "Discord", Chemin = @"C:\Users\Moi\AppData\Local\Discord\app-1.0.9\Discord.exe" },
                    new ProcessusChoix { Nom = "OneDrive", Chemin = @"C:\Program Files\Microsoft OneDrive\OneDrive.exe" },
                    new ProcessusChoix { Nom = "chrome", Chemin = @"C:\Program Files\Google\Chrome\Application\chrome.exe" },
                    new ProcessusChoix { Nom = "Steam", Chemin = @"C:\Program Files (x86)\Steam\steam.exe" },
                });
            vm.ModeJeu.Actualiser();
            for (int i = 0; i < 40 && vm.ModeJeu.Services.Count == 0; i++) { Thread.Sleep(25); Pump(d); }
            vm.ModeJeu.Services[0].IsChecked = true; vm.ModeJeu.Services[1].IsChecked = true;
            vm.ModeJeu.Processus[0].IsChecked = true; vm.ModeJeu.Processus[1].IsChecked = true;
            var w = new MainWindow(vm); Prepare(w, 1280, 720);
            vm.MenuCourant = "Mode Jeu";
            w.Show(); Pump(d);
            Capture(w, "14-mode-jeu", sousDossier);
            CapturerCompteur(d, tmp);

            // Historique : sessions d'exemple (terminée avec un échec, interrompue), un fichier en quarantaine, deux relevés.
            var sess = paths.SessionsDir;
            Action<string, string, string, SessionReport, string[]> session = (nom, etat, debut, rapport, journal) =>
            {
                var dir = Path.Combine(sess, nom); Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "state.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new { etat, debut }));
                if (rapport != null)
                {
                    File.WriteAllText(Path.Combine(dir, "report.json"), Newtonsoft.Json.JsonConvert.SerializeObject(rapport, new Newtonsoft.Json.Converters.StringEnumConverter()));
                    File.WriteAllText(Path.Combine(dir, "report.txt"), "GamingTweaker - rapport du " + debut + "\r\n\r\n[Appliquée] Désactiver la précision du pointeur (opt.mouse)\r\n[ÉCHEC] Protections Spectre et Meltdown désactivées (opt.spectre) : Accès refusé à la clé de registre.\r\n[Ignorée] Économie d'énergie USB désactivée (opt.usb)\r\n\r\nCode de sortie : 1\r\n");
                }
                if (journal != null)
                    File.WriteAllText(Path.Combine(dir, "journal.jsonl"), string.Join("\n", journal.Select(i =>
                        Newtonsoft.Json.JsonConvert.SerializeObject(new { item = i, kind = "task", target = "\\X\\" + i, existed = true, before = "Enabled", after = "Disabled" }))) + "\n");
            };
            var rap = new SessionReport();
            rap.Items.Add(new ItemOutcome { Id = "opt.mouse", Status = OutcomeStatus.Ok });
            rap.Items.Add(new ItemOutcome { Id = "opt.spectre", Status = OutcomeStatus.Failed, Message = "Accès refusé à la clé de registre." });
            rap.Items.Add(new ItemOutcome { Id = "opt.usb", Status = OutcomeStatus.Skipped });
            var rap2 = new SessionReport(); rap2.Items.Add(new ItemOutcome { Id = "net.temp", Status = OutcomeStatus.Ok });
            session("20260103-214500", "Done", "2026-01-03T21:45:00", rap, new[] { "opt.mouse" });
            session("20260102-090000", "Done", "2026-01-02T09:00:00", rap2, new[] { "net.temp" });
            session("20260101-180000", "Running", "2026-01-01T18:00:00", null, new[] { "opt.hags" });
            Directory.CreateDirectory(paths.QuarantineDir);
            File.WriteAllText(Path.Combine(paths.QuarantineDir, "0123456789abcdef0123456789abcdef_msvcr71.dll"), new string('x', 350000));
            File.WriteAllText(Path.Combine(paths.QuarantineDir, "fedcba9876543210fedcba9876543210_ancien-pilote.sys"), new string('x', 2400000));
            File.AppendAllText(Path.Combine(sess, "20260102-090000", "journal.jsonl"), Newtonsoft.Json.JsonConvert.SerializeObject(new { item = "net.temp", kind = "quarantine", target = @"C:\Windows\System32\msvcr71.dll", existed = true, before = @"C:\Windows\System32\msvcr71.dll", after = Path.Combine(paths.QuarantineDir, "0123456789abcdef0123456789abcdef_msvcr71.dll") }) + "\n");
            Mesures.Save(new MesureSnapshot { Date = "2026-01-02T09:30:00", DureeSec = 30, DpcTime = 0.62, InterruptTime = 0.31, Services = 168, Processus = 214, RamDisponibleMo = 9120 }, tmp);
            Mesures.Save(new MesureSnapshot { Date = "2026-01-03T22:10:00", DureeSec = 30, DpcTime = 0.41, InterruptTime = 0.28, Services = 151, Processus = 187, RamDisponibleMo = 10480 }, tmp);
            vm.Historique = new HistoriqueViewModel(paths, new RestaurationService(paths), Exemples(), new QuarantineService(paths.QuarantineDir, paths.SessionsDir));
            vm.MenuCourant = "Historique"; Pump(d);
            vm.Historique.SessionSelectionnee = vm.Historique.Sessions[0]; Pump(d);
            Capture(w, "15-historique-sessions", sousDossier);
            vm.Historique.Section = "Quarantaine"; vm.Historique.Message = ""; Pump(d);
            Capture(w, "16-historique-quarantaine", sousDossier);
            vm.Historique.Section = "Mesures";
            foreach (var r in vm.Historique.Releves) r.IsChecked = true;
            vm.Historique.Comparer(); Pump(d);
            Capture(w, "17-historique-mesures", sousDossier);
            w.Close(); Pump(d);
            try { Directory.Delete(tmp, true); } catch (IOException) { }
        }

        private class Faux14 : IModeJeuActions
        {
            public ServiceState StopService(string n) { return ServiceState.Running; }
            public void StartService(string n) { }
            public bool KillProcess(string n, string c) { return true; }
            public void RestartProcess(string c) { }
            public string SetPlan(string g) { return g; }
            public string GetPlan() { return ""; }
        }

        private static void Run()
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(ThemeApplication.Couleurs(true));
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/GamingTweaker;component/Ui/Theme.xaml", UriKind.Relative) });
            var d = Dispatcher.CurrentDispatcher;

            var items = Exemples();
            var tmp = Path.Combine(Path.GetTempPath(), "gt-shots-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tmp, "Profils"));
            File.WriteAllText(Path.Combine(tmp, "Profils", "gaming.json"), "{\"version\":1,\"nom\":\"gaming\",\"options\":[]}");
            File.WriteAllText(Path.Combine(tmp, "Profils", "bureau.json"), "{\"version\":1,\"nom\":\"bureau\",\"options\":[]}");
            var paths = new DataPaths { ProfilsDir = Path.Combine(tmp, "Profils"), MachineDir = tmp, SessionsDir = Path.Combine(tmp, "S"), QuarantineDir = Path.Combine(tmp, "Q") };
            var gate = new TaskCompletionSource<Dictionary<string, DetectState>>();
            var rapport = new SessionReport { SessionDir = tmp, ExitCode = 1, RedemarrageRequis = true };
            rapport.Items.Add(new ItemOutcome { Id = "opt.mouse", Status = OutcomeStatus.Ok });
            rapport.Items.Add(new ItemOutcome { Id = "opt.usb", Status = OutcomeStatus.AlreadyApplied });
            rapport.Items.Add(new ItemOutcome { Id = "opt.spectre", Status = OutcomeStatus.Failed, Message = "Accès refusé à la clé de registre." });
            rapport.Avertissements.Add("Un point de restauration n'a pas pu être créé : la protection du système est désactivée.");

            MainWindow win = null;
            var vm = new MainViewModel(items, new MachineFacts { WindowsMajor = "11", Edition = "Pro" }, paths, its => gate.Task,
                (req, log, ct) => { log.Report("##GT|start|opt.mouse"); log.Report("##GT|ok|opt.mouse"); return Task.FromResult(rapport); }, o => true);
            win = new MainWindow(vm);
            Prepare(win, 1280, 720);
            win.Show(); Pump(d);

            // Instantané 1 : détection encore en cours.
            var tache = vm.DetectAsync();
            vm.MenuCourant = "Optimisation"; Pump(d);
            Capture(win, "00-optimisation-detection");

            // Détection terminée, avec une sélection (dont une option Risqué) et un avertissement d'élévation.
            gate.SetResult(items.ToDictionary(i => i.Id, i => i.Id == "opt.mouse" || i == items[3] ? DetectState.Applied : i.Id == "opt.hags" ? DetectState.Unknown : DetectState.NotApplied));
            Pump(d); tache.Wait(2000); Pump(d);
            vm.ProfilSelectionne = "gaming"; vm.Message = ""; // le chargement du profil (vide) décoche tout : on coche ensuite
            vm.Options.Single(o => o.Id == "opt.mouse").IsChecked = true;
            vm.Options.Single(o => o.Id == "opt.usb").IsChecked = true;
            vm.Options.Single(o => o.Id == "opt.spectre").IsChecked = true;
            vm.Bandeau = "Les options du profil utilisateur seront ignorées : l'application est exécutée sous un autre compte administrateur.";

            var echantillon = new GamingTweaker.Info.InfoReport();
            Action<string, string[]> sec = (t, kv) => { var x = new GamingTweaker.Info.InfoSection { Titre = t }; for (int k = 0; k + 1 < kv.Length; k += 2) x.Lignes.Add(new KeyValuePair<string, string>(kv[k], kv[k + 1])); echantillon.Sections.Add(x); };
            sec("Windows", new[] { "Système", "Microsoft Windows 11 Pro", "Version", "10.0.26200 (build 26200)", "Architecture", "64 bits" });
            sec("CPU", new[] { "Processeur", "AMD Ryzen 7 7800X3D 8-Core Processor", "Cœurs / threads", "8 / 16", "Fréquence max", "4201 MHz" });
            sec("Carte mère", new[] { "Fabricant", "ASUSTeK COMPUTER INC.", "Modèle", "ROG STRIX B650-A GAMING WIFI", "BIOS", "American Megatrends 3040" });
            sec("RAM", new[] { "Total", "32 Go", "Barrette 1", "16 Go DDR5 6000 MHz (G.Skill F5-6000J3038F16G)", "Barrette 2", "16 Go DDR5 6000 MHz (G.Skill F5-6000J3038F16G)" });
            sec("GPU", new[] { "NVIDIA GeForce RTX 4070", "pilote 32.0.15.6094", "nvidia-smi", "NVIDIA GeForce RTX 4070, 12282 MiB, 560.94" });
            sec("Disques", new[] { "Samsung SSD 990 PRO 1TB", "SSD 931,5 Go - état : Sain", "SMART", "indisponible sans droits administrateur" });
            sec("Réseau", new[] { "Ethernet", "Realtek PCIe 2.5GbE Family Controller - 2500 Mb/s" });
            sec("Écrans", new[] { "DISPLAY1 (NVIDIA GeForce RTX 4070)", "2560x1440 à 165 Hz" });
            sec("Pilotes", new[] { "Affichage : NVIDIA GeForce RTX 4070", "32.0.15.6094, 14/09/2024", "Réseau : Realtek PCIe 2.5GbE", "10.72.0.0, 02/05/2024" });
            var info = new InfoViewModel(() => echantillon, () => null);
            vm.Info = info;
            vm.Diagnostic = new DiagnosticViewModel(() =>
            {
                var m = new MesuresDiag { ModeJeuWindows = true, Hags = 1, SurBatterie = false, LibreSysteme = 12L * 1073741824, TotalSysteme = 500L * 1073741824,
                                          DatePiloteGpu = DateTime.Now.AddDays(-400), NomGpu = "NVIDIA GeForce RTX 4070", CaptureArrierePlan = true };
                m.Ecrans.Add(new EcranDiag { Nom = "Moniteur 27 pouces", Largeur = 2560, Hauteur = 1440, HzActuel = 60, HzMax = 165 });
                m.Barrettes.Add(new BarretteDiag { Vitesse = 4800, Configuree = 4800, Reference = "F5-6000J3038F16G" });
                m.Barrettes.Add(new BarretteDiag { Vitesse = 4800, Configuree = 4800, Reference = "F5-6000J3038F16G" });
                return m;
            });

            var noms = new[] { "Accueil", "Diagnostic", "Mises à jour", "Télémétrie", "Nettoyage", "Optimisation", "Bloatware", "Informations", "Mode Jeu", "Historique" };
            for (int i = 0; i < noms.Length; i++)
            {
                vm.MenuCourant = noms[i]; Pump(d);
                if (noms[i] == "Diagnostic")
                    for (int k = 0; k < 40 && !vm.Diagnostic.Analyse; k++) { Thread.Sleep(50); Pump(d); }
                if (noms[i] == "Informations")
                {
                    for (int k = 0; k < 40 && info.Sections.Count == 0; k++) { Thread.Sleep(50); Pump(d); }
                    if (info.Sections.Count == 0) throw new InvalidOperationException("La fiche d'informations n'a pas été chargée : " + info.Statut + " / " + info.EnCours);
                    info.Appliquer(new[] {
                        new GamingTweaker.Info.SensorReading { Materiel = "AMD Ryzen 7 7800X3D", Nom = "Core (Tctl/Tdie)", Type = "Temperature", Value = 54.5f },
                        new GamingTweaker.Info.SensorReading { Materiel = "AMD Ryzen 7 7800X3D", Nom = "CPU Total", Type = "Load", Value = 12.3f },
                        new GamingTweaker.Info.SensorReading { Materiel = "NVIDIA GeForce RTX 4070", Nom = "GPU Core", Type = "Temperature", Value = 47f },
                        new GamingTweaker.Info.SensorReading { Materiel = "NVIDIA GeForce RTX 4070", Nom = "GPU Core", Type = "Clock", Value = 2490f } });
                    Pump(d);
                }
                Capture(win, "0" + (i + 1) + "-" + noms[i].Replace(' ', '-').Replace("à", "a").Replace("é", "e"));
            }
            // Thème clair : mêmes écrans, couleurs remplacées à chaud (sous-dossier « clair »).
            var dossierClair = Path.Combine(Dossier, "clair"); Directory.CreateDirectory(dossierClair);
            app.Resources.MergedDictionaries[0] = ThemeApplication.Couleurs(false); Pump(d);
            foreach (var m in new[] { "Accueil", "Optimisation", "Mode Jeu" }) { vm.MenuCourant = m; Pump(d); Capture(win, "clair-" + m.Replace(' ', '-'), dossierClair); }
            app.Resources.MergedDictionaries[0] = ThemeApplication.Couleurs(true); Pump(d);

            // Barre de routine (tâche 16) : état simulé, aucune tâche réelle.
            vm.Routine = new RoutineViewModel(vm, c => { }, () => { }, () => new RoutineEtat { Existe = true, Jour = DayOfWeek.Sunday, Heure = new TimeSpan(20, 0, 0), ProfilChemin = @"C:\Program Files\GamingTweaker\App\gaming.json" });
            vm.Routine.ActualiserAsync().Wait(2000); Pump(d);
            vm.MenuCourant = "Optimisation"; Pump(d);
            var dossierRoutine = Path.Combine(Dossier, "taches-14-16"); Directory.CreateDirectory(dossierRoutine);
            Capture(win, "18-barre-routine", dossierRoutine);
            vm.MenuCourant = "Optimisation"; vm.Recherche = "windows"; Pump(d);
            Capture(win, "09-recherche");
            vm.Recherche = "";

            CapturerOptionAListe(d);
            CapturerVuesAvancees(d);

            var prev = new PreviewWindow(vm); Prepare(prev, 1000, 680); prev.Show(); Pump(d);
            Capture(prev, "10-apercu"); prev.Close();

            var run = new RunWindow(vm); bool runFerme = false, mainFerme = false; run.Closed += (a, b) => runFerme = true; win.Closed += (a, b) => mainFerme = true;
            Prepare(run, 900, 640); run.Show(); Pump(d); Thread.Sleep(200); Pump(d); Pump(d);
            Capture(run, "11-execution-rapport");
            // Le RunWindow refuse la fermeture tant que l'exécution n'est pas terminée ; ici elle l'est.
            // Sur un runner lent, l'exécution simulée peut finir après la capture : la fermeture est retentée (10 s au plus).
            for (int k = 0; k < 100 && !runFerme; k++) { run.Close(); Pump(d); if (!runFerme) Thread.Sleep(100); }
            win.Close(); Pump(d);
            var apercuFerme = !prev.IsLoaded;
            if (!runFerme || !apercuFerme || !mainFerme) throw new InvalidOperationException("Une fenêtre ne s'est pas fermée (RunWindow=" + runFerme + ", Aperçu=" + apercuFerme + ", Principale=" + mainFerme + ").");
            try { Directory.Delete(tmp, true); } catch (IOException) { }
        }
    }
}
