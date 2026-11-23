using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GamingTweaker.Core;
using GamingTweaker.Ui;
using Xunit;

namespace GamingTweaker.Tests
{
    public class RapportErreurTests
    {
        [Fact]
        public void Rapport_contient_l_exception_et_les_dernieres_lignes_du_journal()
        {
            TraceAppli.Ecrire("étape témoin avant l'erreur");
            Exception ex;
            try { throw new InvalidOperationException("panne de test"); } catch (Exception e) { ex = e; }
            var t = RapportErreur.Texte("démarrage", ex);
            Assert.Contains("Contexte : démarrage", t);
            Assert.Contains("InvalidOperationException: panne de test", t);
            Assert.Contains("étape témoin avant l'erreur", t);
        }

        [Fact]
        public void Rapport_ecrit_dans_un_dossier_lisible_sans_droits_administrateur()
        {
            var chemin = RapportErreur.Ecrire("test", new Exception("x"));
            Assert.NotNull(chemin);
            Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), chemin);
            Assert.Contains("Contexte : test", File.ReadAllText(chemin));
        }

        [Fact]
        public void Message_indique_le_rapport_et_si_l_appli_continue()
        {
            Assert.Contains("va se fermer", RapportErreur.Message(new Exception("x"), @"C:\r.log", false));
            Assert.Contains("continue", RapportErreur.Message(new Exception("x"), @"C:\r.log", true));
            Assert.Contains(@"C:\r.log", RapportErreur.Message(new Exception("x"), @"C:\r.log", true));
        }
    }

    /// <summary>Mode Jeu : une seule sélection, enregistrée, recochée au chargement et partagée avec le mode automatique.</summary>
    public class ModeJeuSelectionTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "gt-mjsel-" + Guid.NewGuid().ToString("N"));
        public ModeJeuSelectionTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

        private ModeJeuViewModel Vm()
        {
            return new ModeJeuViewModel(new ModeJeu(dir, new ActionsInertes()),
                () => new List<ServiceChoix> { new ServiceChoix { Nom = "Spooler", Affichage = "Spouleur" }, new ServiceChoix { Nom = "WSearch", Affichage = "Recherche" } },
                () => new List<ProcessusChoix> { new ProcessusChoix { Nom = "OneDrive", Chemin = @"C:\OD\OneDrive.exe" } });
        }

        [Fact]
        public async Task Cocher_enregistre_et_la_selection_revient_au_prochain_lancement()
        {
            var mj = Vm();
            new ModeJeuAutoViewModel(dir, mj);
            await mj.ActualiserAsync();
            Assert.Equal(0, mj.NombreServices);
            mj.Services.Single(s => s.Nom == "WSearch").IsChecked = true;
            mj.Processus.Single().IsChecked = true;
            var cfg = ModeJeuAutoConfig.Charger(dir);
            Assert.Equal(new[] { "WSearch" }, cfg.Services);
            Assert.Equal("OneDrive", cfg.Processus.Single().Nom);

            var mj2 = Vm();
            new ModeJeuAutoViewModel(dir, mj2);
            await mj2.ActualiserAsync();
            Assert.True(mj2.Services.Single(s => s.Nom == "WSearch").IsChecked);
            Assert.False(mj2.Services.Single(s => s.Nom == "Spooler").IsChecked);
            Assert.True(mj2.Processus.Single().IsChecked);
            Assert.Equal("1 service coché", mj2.TexteServices);
        }

        [Fact]
        public async Task Un_service_enregistre_qui_ne_tourne_pas_reste_visible_et_coche()
        {
            new ModeJeuAutoConfig { Services = new List<string> { "DiagTrack" } }.Enregistrer(dir);
            var mj = Vm();
            new ModeJeuAutoViewModel(dir, mj);
            await mj.ActualiserAsync();
            var d = mj.Services.First();
            Assert.Equal("DiagTrack", d.Nom);
            Assert.True(d.IsChecked);
            Assert.Contains("Pas démarré", d.Detail);
        }

        [Fact]
        public void Avant_le_chargement_des_listes_la_selection_enregistree_n_est_pas_ecrasee()
        {
            new ModeJeuAutoConfig { Services = new List<string> { "WSearch" } }.Enregistrer(dir);
            var mj = Vm();
            var auto = new ModeJeuAutoViewModel(dir, mj);
            auto.Actif = true;              // le mode automatique n'efface plus la sélection
            mj.UtiliserPlan = false;        // un réglage changé avant d'ouvrir les listes non plus
            Assert.Equal(new[] { "WSearch" }, ModeJeuAutoConfig.Charger(dir).Services);
        }

        [Fact]
        public async Task Resume_de_ce_que_fait_le_mode_jeu()
        {
            var mj = Vm();
            await mj.ActualiserAsync();
            mj.UtiliserPlan = false;
            Assert.StartsWith("Rien n'est choisi", mj.ResumeSelection);
            mj.Services[0].IsChecked = true; mj.UtiliserPlan = true;
            Assert.Contains("arrête 1 service", mj.ResumeSelection);
            Assert.Contains("Performances élevées", mj.ResumeSelection);
            Assert.Equal("Mode Jeu inactif", mj.TitreEtat);
        }

        private class ActionsInertes : IModeJeuActions
        {
            public ServiceState StopService(string nom) { return default(ServiceState); }
            public void StartService(string nom) { }
            public bool KillProcess(string nom, string chemin) { return true; }
            public void RestartProcess(string chemin) { }
            public string SetPlan(string guid) { return null; }
            public string GetPlan() { return null; }
        }
    }
}
