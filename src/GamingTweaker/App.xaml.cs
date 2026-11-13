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
        public App()
        {
            // Doit être appelé avant toute résolution d'assembly embarqué.
            AssemblyLoader.Register();
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
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

            vm.ModeJeu = new ModeJeuViewModel(new ModeJeu(paths.MachineDir, new ModeJeuSysteme()), ModeJeuListes.ListerServices, ModeJeuListes.ListerProcessus)
            {
                Confirmer = texte => MessageBox.Show(fenetre, texte, "Mode Jeu", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes,
            };

            vm.Routine = new RoutineViewModel(vm, Planification.Install, Planification.Remove, Planification.Query)
            {
                MemeUtilisateur = ElevationCheck.SameUserAsDesktop,
                ConfirmerRisque = titres => MessageBox.Show(fenetre,
                    "Ce profil contient des options classées Risqué :\r\n\r\n- " + string.Join("\r\n- ", titres)
                    + "\r\n\r\nLa routine les appliquera chaque semaine, sans interface ni confirmation. Planifier quand même ?",
                    "Routine avec options risquées", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes,
            };

            fenetre = new Ui.MainWindow(vm);
            fenetre.Show();
            fenetre.LancerControlesDemarrage(paths, facts);
            var _ = vm.DetectAsync();
            var __ = vm.Routine.ActualiserAsync();
        }
    }
}
