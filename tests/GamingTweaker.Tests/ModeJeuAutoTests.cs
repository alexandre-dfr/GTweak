using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using GamingTweaker.Core;
using GamingTweaker.Ui;
using Xunit;

namespace GamingTweaker.Tests
{
    public class ModeJeuAutoTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "gt-mja-" + Guid.NewGuid().ToString("N"));
        private readonly string verrou = "Local\\GT.Test.ModeJeuAuto." + Guid.NewGuid().ToString("N");

        public ModeJeuAutoTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

        private class Actions : IModeJeuActions
        {
            public List<string> Journal = new List<string>();
            public string Plan = "plan-initial";
            public ServiceState StopService(string nom) { Journal.Add("stop:" + nom); return ServiceState.Running; }
            public void StartService(string nom) { Journal.Add("start:" + nom); }
            public bool KillProcess(string nom, string chemin) { Journal.Add("kill:" + nom); return true; }
            public void RestartProcess(string chemin) { Journal.Add("restart:" + chemin); }
            public string SetPlan(string guid) { var a = Plan; Plan = guid; Journal.Add("plan:" + guid); return a; }
            public string GetPlan() { return Plan; }
        }

        // ---------- Reconnaissance des jeux ----------

        private static JeuSurveille Dossier(string c) => new JeuSurveille { Nom = "J", Chemin = c, Source = "Steam" };
        private static JeuSurveille Exe(string c) => new JeuSurveille { Nom = "J", Chemin = c, Source = "exe" };

        [Theory]
        [InlineData(@"D:\Steam\steamapps\common\Jeu\bin\jeu.exe", true)]
        [InlineData(@"D:\Steam\steamapps\common\JEU\jeu.exe", true)]
        [InlineData(@"D:\Steam\steamapps\common\Jeu2\jeu.exe", false)]           // dossier voisin au nom proche
        [InlineData(@"D:\Steam\steamapps\common\Jeu", false)]
        [InlineData(@"D:\Steam\steamapps\common\Jeu\UnityCrashHandler64.exe", false)]
        [InlineData(@"D:\Steam\steamapps\common\Jeu\EasyAntiCheat\EasyAntiCheat.exe", false)]
        [InlineData("", false)]
        public void Un_processus_du_dossier_d_un_jeu_correspond(string processus, bool attendu) =>
            Assert.Equal(attendu, Jeux.Correspond(processus, Dossier(@"D:\Steam\steamapps\common\Jeu")));

        [Fact]
        public void Un_jeu_exe_ne_correspond_qu_a_cet_exe()
        {
            var j = Exe(@"C:\Jeux\Mon Jeu\jeu.exe");
            Assert.True(Jeux.Correspond(@"c:\jeux\mon jeu\JEU.EXE", j));
            Assert.False(Jeux.Correspond(@"C:\Jeux\Mon Jeu\autre.exe", j));
        }

        [Fact] public void Jamais_une_racine_de_disque() => Assert.False(Jeux.Correspond(@"C:\Windows\explorer.exe", Dossier(@"C:\")));

        [Fact]
        public void Jeux_en_cours_dans_l_ordre_de_la_liste()
        {
            var a = Dossier(@"D:\Jeux\A"); var b = Exe(@"D:\Jeux\B\b.exe");
            var r = Jeux.EnCours(new[] { @"C:\Windows\explorer.exe", @"D:\Jeux\B\b.exe", @"D:\Jeux\A\x\a.exe" }, new[] { a, b });
            Assert.Equal(new[] { a, b }, r);
            Assert.Empty(Jeux.EnCours(new[] { @"C:\Windows\explorer.exe" }, new[] { a, b }));
        }

        // ---------- Bibliothèques ----------

        [Fact]
        public void Steam_libraryfolders_et_appmanifest()
        {
            const string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t\t\"label\"\t\t\"\"\n\t}\n"
                             + "\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"730\"\t\t\"123\"\n\t\t}\n\t}\n}";
            Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, Jeux.BibliothequesSteam(vdf));

            const string acf = "\"AppState\"\n{\n\t\"appid\"\t\t\"730\"\n\t\"name\"\t\t\"Counter-Strike 2\"\n\t\"installdir\"\t\t\"Counter-Strike Global Offensive\"\n}";
            var m = Jeux.ManifesteSteam(acf);
            Assert.Equal("Counter-Strike 2", m.Item1);
            Assert.Equal("Counter-Strike Global Offensive", m.Item2);
            Assert.Null(Jeux.ManifesteSteam("\"AppState\"\n{\n\t\"name\"\t\t\"X\"\n\t\"installdir\"\t\t\"..\\\\..\\\\Windows\"\n}"));
            Assert.Null(Jeux.ManifesteSteam("\"AppState\"\n{\n\t\"name\"\t\t\"X\"\n}"));
        }

        [Fact]
        public void Steam_depuis_un_dossier_avec_bibliotheque_secondaire()
        {
            var steam = Path.Combine(dir, "Steam"); var lib = Path.Combine(dir, "Lib");
            Directory.CreateDirectory(Path.Combine(steam, "steamapps", "common", "Jeu A"));
            Directory.CreateDirectory(Path.Combine(lib, "steamapps", "common", "Jeu B"));
            File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"1\" { \"path\" \"" + lib.Replace("\\", "\\\\") + "\" } }");
            File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_1.acf"), "\"AppState\" { \"name\" \"Jeu A\" \"installdir\" \"Jeu A\" }");
            File.WriteAllText(Path.Combine(lib, "steamapps", "appmanifest_2.acf"), "\"AppState\" { \"name\" \"Jeu B\" \"installdir\" \"Jeu B\" }");
            File.WriteAllText(Path.Combine(lib, "steamapps", "appmanifest_3.acf"), "\"AppState\" { \"name\" \"Steamworks Common Redistributables\" \"installdir\" \"Jeu B\" }");
            File.WriteAllText(Path.Combine(lib, "steamapps", "appmanifest_4.acf"), "\"AppState\" { \"name\" \"Pas installé\" \"installdir\" \"Absent\" }");
            var j = Jeux.JeuxSteam(steam);
            Assert.Equal(new[] { "Jeu A", "Jeu B" }, j.Select(x => x.Nom).ToArray());
            Assert.All(j, x => Assert.Equal("Steam", x.Source));
        }

        [Fact]
        public void Epic_manifestes()
        {
            var m = Jeux.ManifesteEpic("{\"DisplayName\":\"Fortnite\",\"InstallLocation\":\"C:/Program Files/Epic Games/Fortnite\",\"AppCategories\":[\"public\",\"games\",\"applications\"]}");
            Assert.Equal("Fortnite", m.Item1);
            Assert.Equal(@"C:\Program Files\Epic Games\Fortnite", m.Item2);
            Assert.Null(Jeux.ManifesteEpic("{\"DisplayName\":\"Unreal Engine\",\"InstallLocation\":\"C:/UE\",\"AppCategories\":[\"engines\"]}"));
            Assert.Null(Jeux.ManifesteEpic("{\"DisplayName\":\"X\",\"InstallLocation\":\"relatif\"}"));
            Assert.Null(Jeux.ManifesteEpic("pas du json"));
        }

        // ---------- Automate ----------

        [Fact]
        public void Automate_active_au_lancement_et_retablit_apres_deux_releves_sans_jeu()
        {
            var a = new AutomateModeJeu();
            Assert.Equal(ActionAuto.Rien, a.Decider(false, false));
            Assert.Equal(ActionAuto.Activer, a.Decider(true, false));
            a.Resultat(ActionAuto.Activer, true);
            Assert.Equal(ActionAuto.Rien, a.Decider(true, true));
            Assert.Equal(ActionAuto.Rien, a.Decider(false, true));       // un relevé sans jeu : on attend (relance, lanceur)
            Assert.Equal(ActionAuto.Rien, a.Decider(true, true));        // le jeu est revenu : rien
            Assert.Equal(ActionAuto.Rien, a.Decider(false, true));
            Assert.Equal(ActionAuto.Retablir, a.Decider(false, true));
            a.Resultat(ActionAuto.Retablir, false);
            Assert.False(a.ActiveParAuto);
        }

        [Fact]
        public void Automate_ne_retablit_jamais_un_mode_jeu_active_a_la_main()
        {
            var a = new AutomateModeJeu();
            Assert.Equal(ActionAuto.Rien, a.Decider(true, true));
            for (int i = 0; i < 5; i++) Assert.Equal(ActionAuto.Rien, a.Decider(false, true));
        }

        [Fact]
        public void Automate_ne_reactive_pas_apres_un_retablissement_a_la_main_pendant_la_partie()
        {
            var a = new AutomateModeJeu();
            Assert.Equal(ActionAuto.Activer, a.Decider(true, false)); a.Resultat(ActionAuto.Activer, true);
            Assert.Equal(ActionAuto.Rien, a.Decider(true, false));       // rétabli à la main
            Assert.Equal(ActionAuto.Rien, a.Decider(true, false));
            Assert.Equal(ActionAuto.Rien, a.Decider(false, false));      // partie terminée
            Assert.Equal(ActionAuto.Activer, a.Decider(true, false));    // partie suivante
        }

        [Fact]
        public void Automate_reessaie_si_l_activation_n_a_pas_abouti()
        {
            var a = new AutomateModeJeu();
            Assert.Equal(ActionAuto.Activer, a.Decider(true, false)); a.Resultat(ActionAuto.Activer, false);
            Assert.Equal(ActionAuto.Activer, a.Decider(true, false));
        }

        // ---------- Surveillance (Mode Jeu réel, actions factices) ----------

        [Fact]
        public void Surveillance_active_puis_retablit_et_notifie()
        {
            var actions = new Actions();
            var mj = new ModeJeu(dir, actions, verrou);
            var cfg = new ModeJeuAutoConfig { Actif = true, Plan = true, Services = new List<string> { "SysMain" }, Jeux = new List<JeuSurveille> { Dossier(@"D:\Jeux\A") } };
            var procs = new List<string> { @"C:\Windows\explorer.exe" };
            var notes = new List<string>();
            using (var s = new SurveillanceJeux(mj, () => cfg, () => procs))
            {
                s.Notification += (t, x) => notes.Add(t);
                s.Releve(); Assert.False(mj.IsActive);
                procs.Add(@"D:\Jeux\A\a.exe");
                s.Releve(); Assert.True(mj.IsActive);
                Assert.Contains("stop:SysMain", actions.Journal);
                Assert.Equal(ModeJeu.PlanPerformances, actions.Plan);
                procs.RemoveAt(1);
                s.Releve(); Assert.True(mj.IsActive);
                s.Releve(); Assert.False(mj.IsActive);
                Assert.Equal("plan-initial", actions.Plan);
                Assert.Equal(new[] { "Mode Jeu activé", "Mode Jeu rétabli" }, notes);
            }
        }

        [Fact]
        public void Surveillance_inactive_si_le_mode_automatique_est_desactive()
        {
            var actions = new Actions();
            var mj = new ModeJeu(dir, actions, verrou);
            var cfg = new ModeJeuAutoConfig { Actif = false, Jeux = new List<JeuSurveille> { Dossier(@"D:\Jeux\A") } };
            using (var s = new SurveillanceJeux(mj, () => cfg, () => new[] { @"D:\Jeux\A\a.exe" }))
            {
                s.Releve();
                Assert.False(mj.IsActive);
                Assert.Empty(actions.Journal);
            }
        }

        [Fact]
        public void Fermeture_retablit_un_mode_jeu_active_par_la_surveillance()
        {
            var mj = new ModeJeu(dir, new Actions(), verrou);
            var cfg = new ModeJeuAutoConfig { Actif = true, Plan = true, Jeux = new List<JeuSurveille> { Dossier(@"D:\Jeux\A") } };
            var s = new SurveillanceJeux(mj, () => cfg, () => new[] { @"D:\Jeux\A\a.exe" });
            s.Releve(); Assert.True(mj.IsActive);
            s.RetablirSiActiveParAuto();
            Assert.False(mj.IsActive);
        }

        // ---------- Réglages ----------

        [Fact]
        public void Reglages_enregistres_puis_relus_et_elements_proteges_ecartes()
        {
            var c = new ModeJeuAutoConfig { Actif = true, Plan = false, Services = new List<string> { "SysMain", "WinDefend" },
                Processus = new List<ProcessusChoix> { new ProcessusChoix { Nom = "Discord", Chemin = @"C:\D\Discord.exe" }, new ProcessusChoix { Nom = "explorer" } },
                Jeux = new List<JeuSurveille> { Exe(@"C:\Jeux\j.exe") } };
            c.Enregistrer(dir);
            var r = ModeJeuAutoConfig.Charger(dir);
            Assert.True(r.Actif); Assert.False(r.Plan);
            Assert.Equal(@"C:\Jeux\j.exe", r.Jeux.Single().Chemin);
            var mj = r.VersModeJeu();
            Assert.Equal(new[] { "SysMain" }, mj.Services);              // WinDefend : protégé, jamais arrêté
            Assert.Equal(new[] { "Discord" }, mj.Processus.Select(p => p.Nom));
            Assert.Null(mj.PlanGuid);
        }

        [Fact]
        public void Reglages_illisibles_mode_desactive()
        {
            File.WriteAllText(Path.Combine(dir, ModeJeuAutoConfig.NomFichier), "{pas du json");
            var r = ModeJeuAutoConfig.Charger(dir);
            Assert.False(r.Actif); Assert.Empty(r.Jeux);
        }

        [Fact]
        public void Vue_modele_enregistre_chaque_modification()
        {
            var vm = new ModeJeuAutoViewModel(dir, null);
            vm.Ajouter(new[] { Exe(@"C:\Jeux\a.exe"), Exe(@"C:\Jeux\a.exe"), Dossier(@"D:\Jeux\B") });
            Assert.Equal(2, vm.Jeux.Count);
            vm.Actif = true;
            var relu = ModeJeuAutoConfig.Charger(dir);
            Assert.True(relu.Actif); Assert.Equal(2, relu.Jeux.Count);
            vm.JeuSelectionne = vm.Jeux[0]; vm.Retirer();
            Assert.Single(ModeJeuAutoConfig.Charger(dir).Jeux);
            Assert.Equal(@"D:\Jeux\B", new ModeJeuAutoViewModel(dir, null).Jeux.Single().Chemin);
        }

        // ---------- Démarrage avec Windows ----------

        [Fact]
        public void Tache_de_demarrage_a_l_ouverture_de_session_elevee_depuis_la_copie_protegee()
        {
            var xml = DemarrageWindows.BuildTaskXml(DemarrageWindows.ExeCopie, "S-1-5-21-1-2-3-1001");
            var doc = XDocument.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var logon = doc.Descendants(ns + "LogonTrigger").Single();
            Assert.Equal("S-1-5-21-1-2-3-1001", logon.Element(ns + "UserId").Value);
            Assert.Equal("HighestAvailable", doc.Descendants(ns + "RunLevel").Single().Value);
            Assert.Equal("\"" + DemarrageWindows.ExeCopie + "\"", doc.Descendants(ns + "Command").Single().Value);
            Assert.Equal("--tray", doc.Descendants(ns + "Arguments").Single().Value);
            Assert.Equal("PT0S", doc.Descendants(ns + "ExecutionTimeLimit").Single().Value);
            Assert.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GamingTweaker"), DemarrageWindows.ExeCopie);
            Assert.NotEqual(Planification.ExeCopie, DemarrageWindows.ExeCopie);   // jamais la copie de la routine
        }
    }
}
