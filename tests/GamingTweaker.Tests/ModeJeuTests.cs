using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GamingTweaker.Core;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>Mode Jeu avec des actions factices : aucun service ni processus réel n'est touché.</summary>
    public class ModeJeuTests : IDisposable
    {
        private class Faux : IModeJeuActions
        {
            public List<string> Ordre;
            public HashSet<string> Tournaient = new HashSet<string> { "tourne" };
            public List<string> Stopped = new List<string>();
            public List<string> Started = new List<string>();
            public List<string> Killed = new List<string>();
            public List<string> Restarted = new List<string>();
            public string PlanActif = "plan-initial";
            public HashSet<string> EchecArret = new HashSet<string>();
            public HashSet<string> AvecDependants = new HashSet<string>();
            public List<string> CheminsKill = new List<string>();

            public ServiceState StopService(string nom)
            {
                if (Ordre != null) Ordre.Add("stop:" + nom);
                if (EchecArret.Contains(nom)) throw new InvalidOperationException("refusé");
                if (AvecDependants.Contains(nom)) throw new ServiceRefuseException("des services qui en dépendent sont en cours d'exécution : Enfant");
                if (!Tournaient.Contains(nom)) return ServiceState.Stopped;
                Stopped.Add(nom); Tournaient.Remove(nom);
                return ServiceState.Running;
            }
            public void StartService(string nom) { Started.Add(nom); Tournaient.Add(nom); }
            public bool KillProcess(string nom, string chemin) { if (Ordre != null) Ordre.Add("kill:" + nom); Killed.Add(nom); CheminsKill.Add(chemin); return nom != "absent"; }
            public void RestartProcess(string chemin) { Restarted.Add(chemin); }
            public string SetPlan(string guid) { var avant = PlanActif; PlanActif = guid; if (Ordre != null) Ordre.Add("plan"); return avant; }
            public string GetPlan() { return PlanActif; }
        }

        private class FauxEchecDemarrage : IModeJeuActions
        {
            private readonly Faux f; public FauxEchecDemarrage(Faux f) { this.f = f; }
            public ServiceState StopService(string n) { return f.StopService(n); }
            public void StartService(string n) { throw new InvalidOperationException("échec"); }
            public bool KillProcess(string n, string c) { return f.KillProcess(n, c); }
            public void RestartProcess(string c) { f.RestartProcess(c); }
            public string SetPlan(string g) { return f.SetPlan(g); }
            public string GetPlan() { return f.GetPlan(); }
        }

        private readonly string dir = Path.Combine(Path.GetTempPath(), "gt-modejeu-" + Guid.NewGuid().ToString("N"));
        private readonly string verrou = "Local\\GT.Test.ModeJeu." + Guid.NewGuid().ToString("N");
        private readonly List<string> ordre = new List<string>();

        public ModeJeuTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

        private ModeJeu Nouveau(IModeJeuActions a) { return new ModeJeu(dir, a, verrou, () => { ordre.Add("etat"); }); }

        private static ModeJeuConfig Config()
        {
            var c = new ModeJeuConfig { PlanGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" };
            c.Services.Add("tourne"); c.Services.Add("arrete");
            c.Processus.Add(new ProcessusChoix { Nom = "overlay", Chemin = @"C:\Apps\overlay.exe" });
            return c;
        }

        [Fact]
        public void Activer_ecrit_l_etat_avant_d_agir()
        {
            var a = new Faux { Ordre = ordre };
            Nouveau(a).Activate(Config());
            Assert.Equal("etat", ordre[0]);
            Assert.True(File.Exists(Path.Combine(dir, "modejeu.json")));
        }

        [Fact]
        public void Restaurer_ne_redemarre_que_ce_qui_tournait()
        {
            var a = new Faux();
            var m = Nouveau(a);
            m.Activate(Config());
            m.Restore();
            Assert.Contains("tourne", a.Started);
            Assert.DoesNotContain("arrete", a.Started);
            Assert.Equal(new[] { @"C:\Apps\overlay.exe" }, a.Restarted.ToArray());
        }

        [Fact]
        public void Restaurer_remet_le_plan_initial()
        {
            var a = new Faux();
            var m = Nouveau(a);
            m.Activate(Config());
            Assert.Equal("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", a.PlanActif);
            m.Restore();
            Assert.Equal("plan-initial", a.PlanActif);
            Assert.False(m.IsActive);
        }

        [Fact]
        public void Etat_present_au_demarrage_signale_le_mode_actif()
        {
            var a = new Faux();
            Nouveau(a).Activate(Config());
            Assert.True(new ModeJeu(dir, a, verrou).IsActive);
        }

        [Fact]
        public void Service_deja_redemarre_apres_reboot_ne_provoque_pas_d_erreur()
        {
            var a = new Faux();
            var m = Nouveau(a);
            m.Activate(Config());
            m.Restore();
            var second = m.Restore(); // état déjà supprimé : rien à faire, pas d'erreur
            Assert.Empty(second.Avertissements);
            Assert.False(m.IsActive);
        }

        [Fact]
        public void Restaurer_apres_echec_partiel_conserve_ce_qui_reste()
        {
            var a = new Faux();
            Nouveau(a).Activate(Config());
            var m2 = new ModeJeu(dir, new FauxEchecDemarrage(a), verrou);
            var b = m2.Restore();
            Assert.NotEmpty(b.Avertissements);
            Assert.True(m2.IsActive);            // l'état reste pour réessayer
            var m3 = new ModeJeu(dir, a, verrou);
            var b2 = m3.Restore();
            Assert.Empty(b2.Avertissements);
            Assert.False(m3.IsActive);
        }

        [Fact]
        public void Les_processus_critiques_ou_proteges_ne_sont_jamais_tues()
        {
            var a = new Faux();
            var c = new ModeJeuConfig();
            foreach (var n in new[] { "csrss", "LSASS", "dwm", "MsMpEng", "audiodg", "nvcontainer", "NVDisplay.Container", "RtkAudUService", "GamingTweaker", "overlay" })
                c.Processus.Add(new ProcessusChoix { Nom = n, Chemin = @"C:\x\" + n + ".exe" });
            var bilan = Nouveau(a).Activate(c);
            Assert.Equal(new[] { "overlay" }, a.Killed.ToArray());
            Assert.True(bilan.Avertissements.Count >= 9);
        }

        [Fact]
        public void Les_services_proteges_ne_sont_jamais_arretes()
        {
            var a = new Faux { Tournaient = new HashSet<string> { "WinDefend", "AudioSrv", "RpcSs", "Sympa" } };
            var c = new ModeJeuConfig();
            foreach (var n in new[] { "WinDefend", "AudioSrv", "RpcSs", "Sympa" }) c.Services.Add(n);
            Nouveau(a).Activate(c);
            Assert.Equal(new[] { "Sympa" }, a.Stopped.ToArray());
        }

        [Fact]
        public void Un_service_qui_refuse_de_s_arreter_n_est_pas_redemarre()
        {
            var a = new Faux { EchecArret = new HashSet<string> { "tourne" } };
            var m = Nouveau(a);
            var bilan = m.Activate(Config());
            Assert.NotEmpty(bilan.Avertissements);
            m.Restore();
            Assert.DoesNotContain("tourne", a.Started);
        }

        [Fact]
        public void Verrou_pris_ailleurs_rien_n_est_fait()
        {
            using (var autre = SingleInstance.TryAcquire(verrou))
            {
                Assert.NotNull(autre);
                var a = new Faux();
                var m = Nouveau(a);
                var bilan = m.Activate(Config());
                Assert.True(bilan.Verrouille);
                Assert.Empty(a.Stopped);
                Assert.False(m.IsActive);
            }
        }

        [Fact]
        public void Deuxieme_activation_est_refusee()
        {
            var a = new Faux();
            var m = Nouveau(a);
            m.Activate(Config());
            var b = m.Activate(Config());
            Assert.NotEmpty(b.Avertissements);
            Assert.Single(a.Stopped);
        }

        // ---------- Revue lots 14-16 ----------

        [Fact]
        public void Service_avec_dependants_actifs_est_refuse_signale_et_jamais_redemarre()
        {
            var a = new Faux { Tournaient = new HashSet<string> { "parent", "tourne" }, AvecDependants = new HashSet<string> { "parent" } };
            var c = new ModeJeuConfig(); c.Services.Add("parent"); c.Services.Add("tourne");
            var m = Nouveau(a);
            var bilan = m.Activate(c);
            Assert.Contains(bilan.Avertissements, w => w.Contains("parent") && w.Contains("dépendent"));
            Assert.Equal(new[] { "tourne" }, a.Stopped.ToArray());
            var r = m.Restore();
            Assert.DoesNotContain("parent", a.Started);
            Assert.DoesNotContain(r.Avertissements, w => w.Contains("parent")); // refusé : rien d'incertain
            Assert.False(m.IsActive);
        }

        [Fact]
        public void Dependants_actifs_ignore_les_services_arretes()
        {
            var l = ModeJeuSysteme.DependantsActifs(new[]
            {
                Tuple.Create("A", System.ServiceProcess.ServiceControllerStatus.Running),
                Tuple.Create("B", System.ServiceProcess.ServiceControllerStatus.Stopped),
                Tuple.Create("C", System.ServiceProcess.ServiceControllerStatus.StartPending),
            });
            Assert.Equal(new[] { "A", "C" }, l.ToArray());
        }

        [Fact]
        public void Seul_le_processus_de_la_session_courante_au_chemin_enregistre_est_tue()
        {
            Assert.True(ModeJeuSysteme.DoitTuer(2, @"C:\Apps\overlay.exe", 2, @"c:\apps\OVERLAY.exe"));
            Assert.False(ModeJeuSysteme.DoitTuer(1, @"C:\Apps\overlay.exe", 2, @"C:\Apps\overlay.exe"));      // autre session
            Assert.False(ModeJeuSysteme.DoitTuer(2, @"C:\Autre\overlay.exe", 2, @"C:\Apps\overlay.exe"));     // autre binaire du même nom
            Assert.False(ModeJeuSysteme.DoitTuer(2, null, 2, @"C:\Apps\overlay.exe"));                         // chemin illisible
            Assert.False(ModeJeuSysteme.DoitTuer(2, @"C:\Apps\overlay.exe", 2, null));                         // chemin enregistré inconnu
        }

        [Fact]
        public void Le_chemin_enregistre_est_transmis_a_la_fermeture()
        {
            var a = new Faux();
            Nouveau(a).Activate(Config());
            Assert.Equal(new[] { @"C:\Apps\overlay.exe" }, a.CheminsKill.ToArray());
        }

        [Fact]
        public void Plan_change_depuis_par_l_utilisateur_n_est_pas_ecrase()
        {
            var a = new Faux();
            var m = Nouveau(a);
            m.Activate(Config());
            a.PlanActif = "plan-choisi-ensuite";
            var b = m.Restore();
            Assert.Equal("plan-choisi-ensuite", a.PlanActif);
            Assert.Contains(b.Avertissements, w => w.Contains("plan"));
            Assert.False(m.IsActive);
        }

        [Fact]
        public void Oublier_l_etat_apres_un_retablissement_en_echec()
        {
            var a = new Faux();
            Nouveau(a).Activate(Config());
            var m2 = new ModeJeu(dir, new FauxEchecDemarrage(a), verrou);
            m2.Restore();
            Assert.True(m2.IsActive);
            var b = m2.Oublier();
            Assert.False(b.Verrouille);
            Assert.False(m2.IsActive);
        }

        [Fact]
        public void Etat_illisible_peut_etre_oublie()
        {
            File.WriteAllText(Path.Combine(dir, "modejeu.json"), "{ pas du json");
            var m = new ModeJeu(dir, new Faux(), verrou);
            Assert.NotEmpty(m.Restore().Avertissements);
            Assert.True(m.IsActive);
            m.Oublier();
            Assert.False(m.IsActive);
        }

        [Fact]
        public async System.Threading.Tasks.Task Vue_modele_propose_oublier_apres_echec_avec_confirmation()
        {
            var a = new Faux();
            Nouveau(a).Activate(Config());
            var vm = new GamingTweaker.Ui.ModeJeuViewModel(new ModeJeu(dir, new FauxEchecDemarrage(a), verrou), () => new List<ServiceChoix>(), () => new List<ProcessusChoix>());
            Assert.False(vm.PeutOublier);
            await vm.RetablirAsync();
            Assert.True(vm.EstActif);
            Assert.True(vm.PeutOublier);
            string vu = null;
            vm.Confirmer = t => { vu = t; return false; };
            await vm.OublierAsync();
            Assert.True(vm.EstActif);
            Assert.NotNull(vu);
            vm.Confirmer = t => true;
            await vm.OublierAsync();
            Assert.False(vm.EstActif);
            Assert.False(vm.PeutOublier);
        }

        [Fact]
        public async System.Threading.Tasks.Task Confirmation_annonce_le_plan_performances_elevees()
        {
            var vm = new GamingTweaker.Ui.ModeJeuViewModel(Nouveau(new Faux()), () => new List<ServiceChoix>(), () => new List<ProcessusChoix>());
            string vu = null; vm.Confirmer = t => { vu = t; return false; };
            await vm.ActiverAsync();
            Assert.Contains("Performances élevées", vu);
            Assert.DoesNotContain("maximales", vu);
            var xaml = File.ReadAllText(Path.Combine(Helpers.PsCommon.Racine(), "src", "GamingTweaker", "Ui", "ModeJeuView.xaml"));
            Assert.Contains("Performances élevées", xaml);
            Assert.DoesNotContain("Performances maximales", xaml);
        }

        [Fact]
        public void Processus_du_shell_et_des_pilotes_sont_proteges()
        {
            foreach (var n in new[] { "sihost", "ctfmon", "ShellExperienceHost", "StartMenuExperienceHost", "TextInputHost", "SearchHost", "RuntimeBroker", "conhost", "igfxEM", "RadeonSoftware", "ctfmon.exe" })
                Assert.True(ModeJeu.EstProcessusProtege(n), n);
        }
    }
}
