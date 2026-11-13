using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using GamingTweaker.Core;
using GamingTweaker.Ui;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>Seul BuildTaskXml (pur) est testé : Install, Remove et Query créent, suppriment ou lisent une vraie tâche planifiée.</summary>
    public class PlanificationTests
    {
        private const string Exe = @"C:\Program Files\GamingTweaker\App\GamingTweaker.exe";
        private const string Profil = @"C:\Program Files\GamingTweaker\App\profil.json";
        private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        private static RoutineConfig Cfg() { return new RoutineConfig { Jour = DayOfWeek.Sunday, Heure = new TimeSpan(20, 30, 0), Profil = "gaming" }; }
        private static string Xml() { return Planification.BuildTaskXml(Cfg(), Exe, Profil, @"PC\moi"); }

        [Fact] public void Xml_utilise_la_copie_dans_program_files() => Assert.Contains(@"C:\Program Files\GamingTweaker\App\GamingTweaker.exe", Xml());

        [Fact]
        public void Xml_passe_le_profil_et_silent()
        {
            var xml = Xml();
            Assert.Contains("--profile", xml); Assert.Contains("--silent", xml);
            Assert.Contains(@"C:\Program Files\GamingTweaker\App\profil.json", xml);
        }

        [Fact]
        public void Xml_droits_eleves_compte_interactif()
        {
            var xml = Xml();
            Assert.Contains("HighestAvailable", xml); Assert.Contains("InteractiveToken", xml);
        }

        [Fact]
        public void Chemin_avec_espace_est_echappe()
        {
            var xml = Planification.BuildTaskXml(Cfg(), @"C:\Program Files\Mon Jeu\GamingTweaker.exe", @"C:\Mes Profils\gaming.json", @"PC\moi");
            Assert.Contains("&quot;", xml);
            var cmd = XDocument.Parse(xml).Descendants(Ns + "Command").Single().Value;
            Assert.Equal("\"C:\\Program Files\\Mon Jeu\\GamingTweaker.exe\"", cmd);
            var args = XDocument.Parse(xml).Descendants(Ns + "Arguments").Single().Value;
            Assert.Equal("--profile \"C:\\Mes Profils\\gaming.json\" --silent", args);
        }

        [Fact]
        public void Xml_est_valide_en_utf16_version_1_2()
        {
            var xml = Xml();
            Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-16\"?>", xml);
            var doc = XDocument.Parse(xml);
            Assert.Equal("1.2", (string)doc.Root.Attribute("version"));
            Assert.Equal(Ns, doc.Root.Name.Namespace);
        }

        [Fact]
        public void Declencheur_hebdomadaire_au_bon_jour_et_a_la_bonne_heure()
        {
            var doc = XDocument.Parse(Xml());
            var cal = doc.Descendants(Ns + "CalendarTrigger").Single();
            Assert.EndsWith("T20:30:00", cal.Element(Ns + "StartBoundary").Value);
            var jours = cal.Descendants(Ns + "DaysOfWeek").Single().Elements().Select(e => e.Name.LocalName).ToArray();
            Assert.Equal(new[] { "Sunday" }, jours);
            Assert.Equal("1", cal.Descendants(Ns + "WeeksInterval").Single().Value);
        }

        [Fact]
        public void Reglages_rattrapage_et_limite_de_duree()
        {
            var s = XDocument.Parse(Xml()).Descendants(Ns + "Settings").Single();
            Assert.Equal("true", s.Element(Ns + "StartWhenAvailable").Value);
            Assert.Equal("PT2H", s.Element(Ns + "ExecutionTimeLimit").Value);
            Assert.Equal("IgnoreNew", s.Element(Ns + "MultipleInstancesPolicy").Value);
            Assert.Equal("false", s.Element(Ns + "DisallowStartIfOnBatteries").Value);
        }

        [Fact]
        public void Utilisateur_avec_accents_espaces_et_apostrophe_reste_intact()
        {
            var xml = Planification.BuildTaskXml(Cfg(), Exe, Profil, @"DOMAINE\Zoé d'Arc & Fils <test>");
            var doc = XDocument.Parse(xml);
            Assert.Equal(@"DOMAINE\Zoé d'Arc & Fils <test>", doc.Descendants(Ns + "UserId").First().Value);
        }

        [Fact]
        public void Profil_avec_caracteres_speciaux_est_echappe()
        {
            var xml = Planification.BuildTaskXml(Cfg(), Exe, @"C:\Users\Zoé & O'Neil\profil <1>.json", "S-1-5-21-1");
            var args = XDocument.Parse(xml).Descendants(Ns + "Arguments").Single().Value;
            Assert.Contains(@"C:\Users\Zoé & O'Neil\profil <1>.json", args);
        }

        [Fact]
        public void Lecture_de_la_tache_retrouve_jour_heure_et_profil()
        {
            var e = Planification.ParseTaskXml(Planification.BuildTaskXml(new RoutineConfig { Jour = DayOfWeek.Wednesday, Heure = new TimeSpan(7, 5, 0), Profil = "x" }, Exe, Profil, "S-1-5-21-1"));
            Assert.True(e.Existe);
            Assert.Equal(DayOfWeek.Wednesday, e.Jour);
            Assert.Equal(new TimeSpan(7, 5, 0), e.Heure);
            Assert.Equal(Profil, e.ProfilChemin);
        }

        [Fact]
        public void Lecture_d_un_xml_illisible_ne_leve_pas()
        {
            var e = Planification.ParseTaskXml("pas du xml");
            Assert.False(e.Existe);
            Assert.False(string.IsNullOrEmpty(e.Message));
        }

        // ---------- Vue-modèle : delegués factices, aucune tâche réelle ----------

        private static RoutineViewModel Vm(string optionsJson, System.Collections.Generic.List<RoutineConfig> installs, out string dossier, Func<System.Collections.Generic.IReadOnlyList<string>, bool> confirmer = null, string nomProfil = "gaming")
        {
            dossier = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gt-routine-vm-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dossier);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dossier, nomProfil + ".json"), "{\"version\":1,\"nom\":\"gaming\",\"options\":" + optionsJson + "}");
            var cat = new System.Collections.Generic.List<CatalogItem>
            {
                new CatalogItem { Id = "opt.sur", Menu = "Optimisation", Groupe = "G", Titre = "Sûr", Description = "d", Risque = RiskLevel.Sur },
                new CatalogItem { Id = "opt.risque", Menu = "Optimisation", Groupe = "G", Titre = "Dangereux", Description = "d", Risque = RiskLevel.Risque },
            };
            var paths = new DataPaths { ProfilsDir = dossier, MachineDir = dossier, SessionsDir = System.IO.Path.Combine(dossier, "S"), QuarantineDir = System.IO.Path.Combine(dossier, "Q") };
            var main = new GamingTweaker.Ui.MainViewModel(cat, new MachineFacts(), paths, its => System.Threading.Tasks.Task.FromResult(new System.Collections.Generic.Dictionary<string, DetectState>()), null, o => true);
            main.ProfilSelectionne = nomProfil;
            return new RoutineViewModel(main, c => installs.Add(c), () => { }, () => new RoutineEtat { Existe = false }) { ConfirmerRisque = confirmer };
        }

        [Fact]
        public async System.Threading.Tasks.Task Profil_sans_risque_est_planifie_sans_confirmation()
        {
            var installs = new System.Collections.Generic.List<RoutineConfig>(); string dossier;
            var vm = Vm("[\"opt.sur\"]", installs, out dossier);
            vm.Jour = DayOfWeek.Friday; vm.Heure = "21:15";
            await vm.PlanifierAsync();
            Assert.Single(installs);
            Assert.Equal(DayOfWeek.Friday, installs[0].Jour);
            Assert.Equal(new TimeSpan(21, 15, 0), installs[0].Heure);
            Assert.EndsWith("gaming.json", installs[0].Profil);
            try { System.IO.Directory.Delete(dossier, true); } catch (System.IO.IOException) { }
        }

        [Fact]
        public async System.Threading.Tasks.Task Profil_risque_exige_la_confirmation()
        {
            var installs = new System.Collections.Generic.List<RoutineConfig>(); string dossier;
            var vm = Vm("[\"opt.sur\",\"opt.risque\"]", installs, out dossier);
            await vm.PlanifierAsync();                 // aucune confirmation configurée : refus
            Assert.Empty(installs);
            string vu = null;
            vm.ConfirmerRisque = t => { vu = string.Join(",", t); return false; };
            await vm.PlanifierAsync();
            Assert.Empty(installs); Assert.Equal("Dangereux", vu);
            vm.ConfirmerRisque = t => true;
            await vm.PlanifierAsync();
            Assert.Single(installs);
            try { System.IO.Directory.Delete(dossier, true); } catch (System.IO.IOException) { }
        }

        [Fact]
        public async System.Threading.Tasks.Task Heure_invalide_ne_planifie_pas()
        {
            var installs = new System.Collections.Generic.List<RoutineConfig>(); string dossier;
            var vm = Vm("[\"opt.sur\"]", installs, out dossier);
            foreach (var h in new[] { "", "25:00", "abc", "20h30", "-1:00" }) { vm.Heure = h; await vm.PlanifierAsync(); }
            Assert.Empty(installs);
            Assert.Contains("Heure invalide", vm.Message);
            try { System.IO.Directory.Delete(dossier, true); } catch (System.IO.IOException) { }
        }

        [Fact]
        public void Etat_est_decrit_en_francais()
        {
            Assert.Equal("Aucune routine planifiée.", RoutineViewModel.DecrireEtat(new RoutineEtat { Existe = false }));
            Assert.Equal("Routine active : dimanche à 20:30, profil « gaming ».",
                RoutineViewModel.DecrireEtat(new RoutineEtat { Existe = true, Jour = DayOfWeek.Sunday, Heure = new TimeSpan(20, 30, 0), ProfilChemin = @"C:\x\gaming.json" }));
        }

        [Fact]
        public void Nom_de_tache_et_dossiers_sont_ceux_de_la_specification()
        {
            Assert.Equal(@"\GamingTweaker\Routine", Planification.NomTache);
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GamingTweaker", "App", "GamingTweaker.exe"), Planification.ExeCopie);
            Assert.Equal(@"C:\Program Files\GamingTweaker\App\GamingTweaker.exe", Planification.ExeCopie); // processus x64
        }

        [Fact]
        public void Reinstallation_depuis_la_copie_ne_supprime_ni_ne_recopie_l_exe_en_cours()
        {
            Assert.True(Planification.DepuisLaCopie(Planification.ExeCopie, Planification.ExeCopie));
            Assert.True(Planification.DepuisLaCopie(Planification.ExeCopie.ToUpperInvariant(), Planification.ExeCopie));
            Assert.False(Planification.DepuisLaCopie(@"C:\Users\moi\Downloads\GamingTweaker.exe", Planification.ExeCopie));
        }

        // ---------- Revue lots 14-16 ----------

        [Fact]
        public void Suppression_introuvable_vaut_succes_mais_pas_un_refus()
        {
            Assert.True(Planification.SuppressionReussie(0, ""));
            Assert.True(Planification.SuppressionReussie(1, "ERREUR : Le fichier spécifié est introuvable.\r\n"));
            Assert.True(Planification.SuppressionReussie(1, "ERROR: The system cannot find the file specified.\r\n"));
            Assert.True(Planification.SuppressionReussie(1, "ERREUR : La tâche spécifiée n'existe pas dans le système.\r\n"));
            Assert.False(Planification.SuppressionReussie(1, "ERREUR : Accès refusé.\r\n"));
            Assert.False(Planification.SuppressionReussie(1, ""));
        }

        [Fact]
        public void Nom_de_profil_avec_pourcent_est_refuse()
        {
            Assert.False(Planification.NomProfilAccepte("jeu%USERPROFILE%"));
            Assert.False(Planification.NomProfilAccepte(@"C:\Profils\a%b.json"));
            Assert.True(Planification.NomProfilAccepte(@"C:\Profils\Zoé d'Arc.json"));
        }

        [Fact]
        public async System.Threading.Tasks.Task Profil_avec_pourcent_n_est_pas_planifie()
        {
            var installs = new System.Collections.Generic.List<RoutineConfig>(); string dossier;
            var vm = Vm("[\"opt.sur\"]", installs, out dossier, nomProfil: "jeu%TEMP%");
            await vm.PlanifierAsync();
            Assert.Empty(installs);
            Assert.Contains("%", vm.Message);
            try { System.IO.Directory.Delete(dossier, true); } catch (System.IO.IOException) { }
        }

        [Fact]
        public async System.Threading.Tasks.Task Eleve_par_un_autre_administrateur_la_planification_est_refusee()
        {
            var installs = new System.Collections.Generic.List<RoutineConfig>(); string dossier;
            var vm = Vm("[\"opt.sur\"]", installs, out dossier);
            vm.MemeUtilisateur = () => false;
            await vm.PlanifierAsync();
            Assert.Empty(installs);
            Assert.Contains("Planifiez depuis la session de l'utilisateur, avec un compte administrateur", vm.Message);
            vm.MemeUtilisateur = () => true;
            await vm.PlanifierAsync();
            Assert.Single(installs);
            try { System.IO.Directory.Delete(dossier, true); } catch (System.IO.IOException) { }
        }

        [Fact]
        public void Exe_de_la_routine_dans_programdata_app_utilise_la_racine_programdata()
        {
            var pd = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gt-pd-" + Guid.NewGuid().ToString("N"));
            var app = System.IO.Path.Combine(pd, "App");
            System.IO.Directory.CreateDirectory(app); // inscriptible : sans la règle, les données iraient dans App\Machines
            try
            {
                var f = new MachineFacts { MachineName = "PC", MachineGuid = "12345678-aaaa" };
                var p = DataPaths.Resolve(app, f, pd);
                Assert.Equal(System.IO.Path.Combine(pd, "Machines", "PC-12345678"), p.MachineDir);
                Assert.Equal(System.IO.Path.Combine(pd, "Profils"), p.ProfilsDir);
                // Racine unique : la routine et l'interface résolvent le même dossier Machines.
                Assert.Equal(p.MachineDir, DataPaths.Resolve(System.IO.Path.Combine(pd, "ailleurs"), f, pd).MachineDir);
            }
            finally { try { System.IO.Directory.Delete(pd, true); } catch (System.IO.IOException) { } }
        }
    }
}
