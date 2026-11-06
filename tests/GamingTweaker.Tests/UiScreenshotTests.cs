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
            Assert.Empty(erreurs);
            Assert.Equal(12, Directory.GetFiles(Dossier, "*.png").Length);
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
            w.Close(); Pump(d);
            try { Directory.Delete(tmp, true); } catch (IOException) { }
        }

        private class Faux14 : IModeJeuActions
        {
            public ServiceState StopService(string n) { return ServiceState.Running; }
            public void StartService(string n) { }
            public bool KillProcess(string n) { return true; }
            public void RestartProcess(string c) { }
            public string SetPlan(string g) { return g; }
        }

        private static void Run()
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
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

            var noms = new[] { "Mises à jour", "Télémétrie", "Nettoyage", "Optimisation", "Bloatware", "Informations", "Mode Jeu", "Historique" };
            for (int i = 0; i < noms.Length; i++)
            {
                vm.MenuCourant = noms[i]; Pump(d);
                Capture(win, "0" + (i + 1) + "-" + noms[i].Replace(' ', '-').Replace("à", "a").Replace("é", "e"));
            }
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
            run.Close(); win.Close(); Pump(d);
            var apercuFerme = !prev.IsLoaded;
            if (!runFerme || !apercuFerme || !mainFerme) throw new InvalidOperationException("Une fenêtre ne s'est pas fermée (RunWindow=" + runFerme + ", Aperçu=" + apercuFerme + ", Principale=" + mainFerme + ").");
            try { Directory.Delete(tmp, true); } catch (IOException) { }
        }
    }
}
