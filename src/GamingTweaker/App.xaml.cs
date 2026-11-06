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
            IReadOnlyList<CatalogItem> catalogue;
            try { catalogue = Catalogue.LoadEmbedded(); }
            catch (Exception ex)
            {
                MessageBox.Show("Le catalogue des options est illisible : " + ex.Message, "GamingTweaker", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(2); return;
            }
            var facts = SystemInfo.Detect();
            var paths = DataPaths.Resolve(AppDomain.CurrentDomain.BaseDirectory, facts, DataPaths.DefaultProgramDataRoot);
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
            vm.ConfirmerProfilRisque = risques => MessageBox.Show(fenetre,
                "Ce profil contient des options classées Risqué :\r\n\r\n- " + string.Join("\r\n- ", risques.Select(r => r.Titre))
                + "\r\n\r\nLe charger plus tard les appliquera à nouveau. L'enregistrer quand même ?",
                "Profil avec options risquées", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

            var restauration = new RestaurationService(paths, ElevationCheck.SameUserAsDesktop);
            vm.Restauration = restauration;
            vm.Historique = new HistoriqueViewModel(paths, restauration, catalogue, new QuarantineService(paths.QuarantineDir, paths.SessionsDir))
            {
                Confirmer = texte => MessageBox.Show(fenetre, texte, "Historique", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes,
                ApresAnnulation = () => { var _ = vm.DetectAsync(); },
            };

            vm.ModeJeu = new ModeJeuViewModel(new ModeJeu(paths.MachineDir, new ModeJeuSysteme()), ModeJeuListes.ListerServices, ModeJeuListes.ListerProcessus)
            {
                Confirmer = texte => MessageBox.Show(fenetre, texte, "Mode Jeu", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes,
            };

            vm.Routine = new RoutineViewModel(vm, Planification.Install, Planification.Remove, Planification.Query)
            {
                ConfirmerRisque = titres => MessageBox.Show(fenetre,
                    "Ce profil contient des options classées Risqué :\r\n\r\n- " + string.Join("\r\n- ", titres)
                    + "\r\n\r\nLa routine les appliquera chaque semaine, sans interface ni confirmation. Planifier quand même ?",
                    "Routine avec options risquées", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes,
            };

            fenetre = new Ui.MainWindow(vm);
            fenetre.Show();
            fenetre.LancerControlesDemarrage(paths);
            var _ = vm.DetectAsync();
            var __ = vm.Routine.ActualiserAsync();
        }
    }
}
