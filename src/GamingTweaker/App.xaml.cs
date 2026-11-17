using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GamingTweaker.Core;
using GamingTweaker.Lists;
using GamingTweaker.Ui;

namespace GamingTweaker
{
    public partial class App : Application
    {
        public const string VerrouInterface = @"Global\GamingTweaker.Interface";

        /// <summary>Lancé avec --tray (démarrage avec Windows) : la fenêtre reste cachée, seule l'icône près de l'horloge est affichée.</summary>
        public static bool DemarrageEnZone { get; set; }

        private IDisposable verrouInterface;

        public App()
        {
            // Doit être appelé avant toute résolution d'assembly embarqué.
            AssemblyLoader.Register();
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
            // Une seule interface à la fois (l'icône près de l'horloge en est une) : deux surveillances du Mode Jeu se contrediraient.
            verrouInterface = SingleInstance.TryAcquire(VerrouInterface);
            if (verrouInterface == null)
            {
                if (!DemarrageEnZone)
                    MessageBox.Show("GamingTweaker est déjà ouvert : cherchez son icône près de l'horloge (zone de notification).", "GamingTweaker", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(2); return;
            }
            Exit += (s, ev) => verrouInterface.Dispose();
            // La fenêtre peut être cachée (zone de notification) : l'application s'arrête explicitement (Quitter, ou fermeture hors mode automatique).
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Avant toute lecture ou écriture de %ProgramFiles%\GamingTweaker : créé avec sa DACL protégée, ou vérifié (refus s'il n'est pas sûr).
            // Verrou d'instance pris AVANT le parcours (occupé : message, aucun parcours), libéré ensuite.
            var erreur = DemarrageSur.VerifierDossierProtege(DataPaths.RacineProtegeeParDefaut, ElevationCheck.EstAdministrateur);
            if (erreur == DemarrageSur.MessageOccupe)
            {
                MessageBox.Show(erreur, "GamingTweaker", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(2); return;
            }
            if (erreur != null)
            {
                MessageBox.Show(erreur + "\r\n\r\nPar sécurité, GamingTweaker s'arrête.", "GamingTweaker", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(2); return;
            }

            IReadOnlyList<CatalogItem> catalogue;
            try
            {
                catalogue = Catalogue.LoadEmbedded();
                // Un catalogue incohérent (id en double, script manquant) ne doit jamais atteindre l'exécution.
                var erreurs = Catalogue.Validate(catalogue);
                if (erreurs.Count > 0) throw new InvalidOperationException(string.Join(" ", erreurs));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Le catalogue des options est illisible : " + ex.Message, "GamingTweaker", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(2); return;
            }
            var facts = SystemInfo.Detect();
            var paths = DataPaths.Resolve(AppDomain.CurrentDomain.BaseDirectory, facts, DataPaths.RacineProtegeeParDefaut);
            TraceAppli.Configurer(paths.MachineDir);
            TraceAppli.Ecrire("interface : Windows " + facts.WindowsMajor + " " + facts.Edition + ", CPU " + facts.CpuVendor + ", GPU " + string.Join("/", facts.GpuVendors) + (facts.EstPortable ? ", portable" : ""));
            var pipeline = new RunPipeline(new RestorePoint(), ElevationCheck.SameUserAsDesktop);

            var registre = new ListProviderRegistry();
            registre.Register(ServicesProvider.Standard());
            registre.Register(ServicesProvider.Xbox());
            registre.Register(new StartupProvider());
            registre.Register(BloatwareProvider.Standard());
            registre.Register(BloatwareProvider.Tierces());
            registre.Register(new WingetProvider());
            registre.Register(new WindowsUpdateProvider());
            registre.Register(new GpuDriverProvider());

            MainWindow fenetre = null;
            var vm = new MainViewModel(catalogue, facts, paths, its => Detection.RunAsync(its),
                (req, log, ct) => pipeline.ExecuteAsync(req, log, ct), o => Ui.MainWindow.ConfirmerRisque(fenetre, o), registre);
            vm.ConfirmerElementRisque = (o, titre, detail) => MessageBox.Show(fenetre,
                o.Titre + "\r\n\r\n" + titre + (string.IsNullOrEmpty(detail) ? "" : "\r\n" + detail)
                + "\r\n\r\nCet élément est classé Risqué : le retirer peut gêner Windows ou d'autres applications. Le cocher quand même ?",
                "Élément risqué", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
            vm.ConfirmerProfilRisque = risques => MessageBox.Show(fenetre,
                "Ce profil contient des options classées Risqué :\r\n\r\n- " + string.Join("\r\n- ", risques.Select(r => r.TitreRisque))
                + "\r\n\r\nLe charger plus tard les appliquera à nouveau. L'enregistrer quand même ?",
                "Profil avec options risquées", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

            var info = new InfoViewModel(() => Info.InfoCollector.Collect(), () => new Info.Capteurs())
            {
                ConfirmerCapteurs = t => MessageBox.Show(fenetre, t, "Activer les capteurs", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes,
                ConfirmerPawnIo = t => MessageBox.Show(fenetre, t, "Installer PawnIO", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes,
                InstallerPawnIo = Info.PawnIoInstaller.Install,
            };
            vm.Info = info;
            Exit += (s, ev) => info.Dispose();

            // Une seule racine de sessions (%ProgramFiles%\GamingTweaker\Machines) pour l'interface et la routine.
            var restauration = new RestaurationService(paths, ElevationCheck.SameUserAsDesktop);
            vm.Restauration = restauration;
            vm.Historique = new HistoriqueViewModel(paths, restauration, catalogue, new QuarantineService(paths.QuarantineDir, paths.SessionsDir))
            {
                Confirmer = texte => MessageBox.Show(fenetre, texte, "Historique", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes,
                ApresAnnulation = () => { var _ = vm.DetectAsync(); },
                ApresDesinstallation = () =>
                {
                    MessageBox.Show(fenetre, "GamingTweaker est désinstallé. Supprimez GamingTweaker.exe à la main si vous ne voulez plus l'utiliser.", "GamingTweaker", MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown(0);
                },
            };

            var modeJeu = new ModeJeu(paths.MachineDir, new ModeJeuSysteme());
            vm.ModeJeu = new ModeJeuViewModel(modeJeu, ModeJeuListes.ListerServices, ModeJeuListes.ListerProcessus)
            {
                Confirmer = texte => MessageBox.Show(fenetre, texte, "Mode Jeu", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes,
            };
            var auto = new ModeJeuAutoViewModel(paths.MachineDir, vm.ModeJeu, DemarrageWindows.EstInstalle,
                v => { if (v) DemarrageWindows.Installer(); else DemarrageWindows.Retirer(); })
            {
                ChoisirExe = () =>
                {
                    var d = new Microsoft.Win32.OpenFileDialog { Title = "Choisir l'exécutable du jeu", Filter = "Programme (*.exe)|*.exe", CheckFileExists = true };
                    return d.ShowDialog(fenetre) == true ? d.FileName : null;
                },
                ChoisirJeux = (titre, jeux) =>
                {
                    var w = new ChoixJeuxWindow(titre, jeux) { Owner = fenetre };
                    return w.ShowDialog() == true ? w.Choisis : null;
                },
            };
            vm.ModeJeu.Auto = auto;

            vm.Routine = new RoutineViewModel(vm, Planification.Install, Planification.Remove, Planification.Query)
            {
                MemeUtilisateur = ElevationCheck.SameUserAsDesktop,
                ConfirmerRisque = titres => MessageBox.Show(fenetre,
                    "Ce profil contient des options classées Risqué :\r\n\r\n- " + string.Join("\r\n- ", titres)
                    + "\r\n\r\nLa routine les appliquera chaque semaine, sans interface ni confirmation. Planifier quand même ?",
                    "Routine avec options risquées", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes,
            };

            fenetre = new Ui.MainWindow(vm);

            // ---- Mode Jeu automatique et zone de notification ----
            var surveillance = new SurveillanceJeux(modeJeu, () => auto.Config);
            bool quitter = false, bulleMontree = false, premiereOuverture = true;
            ZoneNotification zone = null;
            Action premiersControles = () =>
            {
                if (!premiereOuverture) return;
                premiereOuverture = false;
                fenetre.LancerControlesDemarrage(paths, facts);
                var _ = vm.DetectAsync();
                var __ = vm.Routine.ActualiserAsync();
            };
            Action ouvrir = () =>
            {
                fenetre.Show();
                if (fenetre.WindowState == WindowState.Minimized) fenetre.WindowState = WindowState.Normal;
                fenetre.Activate();
                vm.ModeJeu.Synchroniser();
                premiersControles();
            };
            Action quitterApp = () => { quitter = true; fenetre.Close(); Shutdown(0); };
            zone = new ZoneNotification(() => Dispatcher.Invoke(ouvrir), () => Dispatcher.Invoke(quitterApp), () => auto.Actif, v => Dispatcher.Invoke(() => auto.Actif = v));
            surveillance.Notification += (titre, texte) => Dispatcher.BeginInvoke(new Action(() => { vm.ModeJeu.Synchroniser(); zone.Notifier(titre, texte); }));
            Action majAuto = () =>
            {
                zone.Visible = DemarrageEnZone || auto.Actif || auto.DemarrageAvecWindows;
                if (auto.Actif) surveillance.Demarrer(); else surveillance.Arreter();
            };
            auto.ConfigChangee += majAuto;
            auto.PropertyChanged += (s, ev) => { if (ev.PropertyName == "DemarrageAvecWindows") majAuto(); };
            majAuto();
            // Icône affichée (mode automatique ou démarrage avec Windows) : fermer la fenêtre la cache ; sinon l'application s'arrête.
            fenetre.Closing += (s, ev) =>
            {
                if (quitter || ev.Cancel || !zone.Visible) return;
                ev.Cancel = true;
                fenetre.Hide();
                if (!bulleMontree)
                {
                    bulleMontree = true;
                    zone.Notifier("GamingTweaker reste ouvert", (auto.Actif ? "Le Mode Jeu automatique continue. " : "") + "Clic droit sur l'icône près de l'horloge pour rouvrir ou quitter.");
                }
            };
            fenetre.Closed += (s, ev) => { if (!quitter) Shutdown(0); };
            Exit += (s, ev) =>
            {
                surveillance.Dispose();
                // Mode Jeu activé par le mode automatique : rétabli avant de quitter (sinon il resterait actif sans surveillance).
                try { surveillance.RetablirSiActiveParAuto(); } catch (Exception ex) { TraceAppli.Erreur("rétablissement du Mode Jeu à la fermeture", ex); }
                zone.Dispose();
            };
            var ___ = auto.ActualiserDemarrageAsync();

            if (DemarrageEnZone) zone.Visible = true;
            else ouvrir();
        }
    }
}
