using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using GamingTweaker.Ui;
using Newtonsoft.Json;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>Historique, quarantaine et mesures : uniquement des fichiers sous %TEMP%.</summary>
    public class HistoriqueTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-histo-" + Guid.NewGuid().ToString("N"));
        private DataPaths paths;

        public HistoriqueTests()
        {
            paths = new DataPaths
            {
                ProfilsDir = Path.Combine(tmp, "Profils"), MachineDir = Path.Combine(tmp, "Machine"),
                SessionsDir = Path.Combine(tmp, "Machine", "Sessions"), QuarantineDir = Path.Combine(tmp, "Quarantaine"),
            };
            Directory.CreateDirectory(paths.SessionsDir); Directory.CreateDirectory(paths.QuarantineDir);
        }
        public void Dispose() { try { Directory.Delete(tmp, true); } catch (IOException) { } }

        private static MesureSnapshot A(int services = 100, int processus = 200, long ram = 8000, double? dpc = 0.5, double? irq = 0.2)
            => new MesureSnapshot { Date = "2026-01-01T10:00:00", Services = services, Processus = processus, RamDisponibleMo = ram, DpcTime = dpc, InterruptTime = irq };

        // ---------- Mesures ----------

        [Fact]
        public void Comparaison_calcule_les_ecarts()
        {
            var d = Mesures.Compare(A(services: 100, processus: 210, ram: 8000, dpc: 0.5, irq: 0.4), A(services: 95, processus: 190, ram: 9500, dpc: 0.3, irq: 0.4));
            Assert.Equal(-5, d.Services);
            Assert.Equal(-20, d.Processus);
            Assert.Equal(1500, d.RamDisponibleMo);
            Assert.Equal(-0.2, d.DpcTime.Value, 3);
            Assert.Equal(0.0, d.InterruptTime.Value, 3);
        }

        [Fact]
        public void Comparaison_sans_compteur_donne_un_ecart_inconnu()
        {
            var d = Mesures.Compare(A(dpc: null), A());
            Assert.Null(d.DpcTime);
        }

        [Fact]
        public void Releves_enregistres_puis_relus_du_plus_recent_au_plus_ancien()
        {
            var a = A(services: 1); a.Date = "2026-01-01T10:00:00";
            var b = A(services: 2); b.Date = "2026-01-02T10:00:00";
            Mesures.Save(a, paths.MachineDir); Mesures.Save(b, paths.MachineDir);
            var l = Mesures.List(paths.MachineDir);
            Assert.Equal(new[] { 2, 1 }, l.Select(x => x.Services).ToArray());
            Assert.True(Directory.Exists(Path.Combine(paths.MachineDir, "Mesures")));
        }

        [Fact]
        public async Task Releve_reel_court_renseigne_les_compteurs_resolus_par_index()
        {
            var s = await Mesures.TakeAsync(TimeSpan.FromSeconds(3));
            Assert.True(s.Services > 0);
            Assert.True(s.Processus > 0);
            Assert.True(s.RamDisponibleMo > 0);
            Assert.NotNull(s.DpcTime);
            Assert.NotNull(s.InterruptTime);
            Assert.InRange(s.DpcTime.Value, 0, 100);
        }

        [Fact]
        public async Task Deux_releves_reels_se_comparent()
        {
            var a = await Mesures.TakeAsync(TimeSpan.FromSeconds(3));
            var b = await Mesures.TakeAsync(TimeSpan.FromSeconds(3));
            var texte = HistoriqueViewModel.FormaterComparaison(a, b);
            Assert.Contains("Temps DPC", texte);
            var sortie = Path.Combine(Path.GetTempPath(), "GamingTweakerShots"); Directory.CreateDirectory(sortie);
            File.WriteAllText(Path.Combine(sortie, "mesures-comparaison.txt"), texte);
        }

        // ---------- Quarantaine ----------

        private string Mettre(string nomOrigine, string contenu, out string id)
        {
            var origine = Path.Combine(tmp, "Origine", nomOrigine);
            id = Guid.NewGuid().ToString("N") + "_" + nomOrigine;
            var dest = Path.Combine(paths.QuarantineDir, id);
            File.WriteAllText(dest, contenu);
            var sess = Path.Combine(paths.SessionsDir, "20260101-100000");
            Directory.CreateDirectory(sess);
            var ligne = JsonConvert.SerializeObject(new { item = "cln.orphans", kind = "quarantine", target = origine, existed = true, before = origine, after = dest });
            File.AppendAllText(Path.Combine(sess, "journal.jsonl"), ligne + "\n");
            return origine;
        }

        [Fact]
        public void Quarantaine_liste_avec_l_origine_du_journal()
        {
            string id; var origine = Mettre("résidu.dll", "x", out id);
            var l = new QuarantineService(paths.QuarantineDir, paths.SessionsDir).List();
            Assert.Single(l);
            Assert.Equal(id, l[0].Id);
            Assert.Equal(origine, l[0].Origine);
            Assert.Equal("résidu.dll", l[0].Nom);
        }

        [Fact]
        public void Quarantaine_restaure_le_fichier()
        {
            string id; var origine = Mettre("a.dll", "contenu", out id);
            new QuarantineService(paths.QuarantineDir, paths.SessionsDir).Restore(id);
            Assert.True(File.Exists(origine));
            Assert.Equal("contenu", File.ReadAllText(origine));
            Assert.False(File.Exists(Path.Combine(paths.QuarantineDir, id)));
        }

        [Fact]
        public void Quarantaine_refuse_d_ecraser_un_fichier_existant()
        {
            string id; var origine = Mettre("b.dll", "quarantaine", out id);
            Directory.CreateDirectory(Path.GetDirectoryName(origine)); File.WriteAllText(origine, "recent");
            Assert.Throws<InvalidOperationException>(() => new QuarantineService(paths.QuarantineDir, paths.SessionsDir).Restore(id));
            Assert.Equal("recent", File.ReadAllText(origine));
            Assert.True(File.Exists(Path.Combine(paths.QuarantineDir, id)));
        }

        [Fact]
        public void Purge_supprime_uniquement_dans_la_quarantaine()
        {
            var svc = new QuarantineService(paths.QuarantineDir, paths.SessionsDir);
            Assert.Throws<InvalidOperationException>(() => svc.Purge(@"..\..\x"));
            var dehors = Path.Combine(tmp, "dehors.txt"); File.WriteAllText(dehors, "garder");
            Assert.Throws<InvalidOperationException>(() => svc.Purge(@"..\dehors.txt"));
            Assert.Throws<InvalidOperationException>(() => svc.Purge(dehors));
            Assert.Throws<InvalidOperationException>(() => svc.Purge(""));
            Assert.Throws<InvalidOperationException>(() => svc.Purge("."));
            Assert.True(File.Exists(dehors));
            string id; Mettre("c.dll", "x", out id);
            svc.Purge(id);
            Assert.False(File.Exists(Path.Combine(paths.QuarantineDir, id)));
        }

        // ---------- Sessions ----------

        private void Session(string nom, string etat, string debut, SessionReport rapport = null, string[] journalItems = null)
        {
            var d = Path.Combine(paths.SessionsDir, nom); Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, "state.json"), JsonConvert.SerializeObject(new { etat, debut }));
            if (rapport != null)
                File.WriteAllText(Path.Combine(d, "report.json"), JsonConvert.SerializeObject(rapport, new Newtonsoft.Json.Converters.StringEnumConverter()));
            if (journalItems != null)
                File.WriteAllText(Path.Combine(d, "journal.jsonl"), string.Join("\n", journalItems.Select(i =>
                    JsonConvert.SerializeObject(new { item = i, kind = "task", target = "\\X\\" + i, existed = true, before = "Enabled", after = "Disabled" }))) + "\n");
        }

        /// <summary>Racine unique (%ProgramFiles%\GamingTweaker) : l'interface et la routine partagent les mêmes sessions.</summary>
        private HistoriqueViewModel Vm(FauxRestauration f = null, IReadOnlyList<CatalogItem> cat = null)
        {
            var q = new QuarantineService(paths.QuarantineDir, paths.SessionsDir);
            return new HistoriqueViewModel(paths, f ?? new FauxRestauration(), cat ?? new List<CatalogItem>(), q);
        }

        [Fact]
        public void Sessions_triees_du_plus_recent_au_plus_ancien()
        {
            Session("a", "Done", "2026-01-01T10:00:00");
            Session("b", "Done", "2026-01-02T10:00:00");
            var vm = Vm();
            Assert.Equal("b", vm.Sessions[0].Id);
            Assert.Equal("a", vm.Sessions[1].Id);
        }

        [Fact]
        public void Sessions_interrompues_ou_en_echec_sont_signalees()
        {
            Session("a", "Done", "2026-01-01T10:00:00");
            Session("b", "Running", "2026-01-02T10:00:00");
            Session("c", "Failed", "2026-01-03T10:00:00");
            var vm = Vm();
            Assert.True(vm.Sessions.Single(s => s.Id == "b").Anormale);
            Assert.True(vm.Sessions.Single(s => s.Id == "c").Anormale);
            Assert.False(vm.Sessions.Single(s => s.Id == "a").Anormale);
            Assert.Contains("interrompue", vm.Sessions.Single(s => s.Id == "b").EtatLibelle);
        }

        [Fact]
        public void Session_affiche_les_compteurs_et_les_options_annulables()
        {
            var r = new SessionReport();
            r.Items.Add(new ItemOutcome { Id = "opt.a", Status = OutcomeStatus.Ok });
            r.Items.Add(new ItemOutcome { Id = "opt.b", Status = OutcomeStatus.Failed, Message = "refusé" });
            r.Items.Add(new ItemOutcome { Id = "opt.c", Status = OutcomeStatus.Skipped });
            Session("a", "Done", "2026-01-01T10:00:00", r, new[] { "opt.a" });
            var s = Vm().Sessions[0];
            Assert.Equal(1, s.Appliquees); Assert.Equal(1, s.Echecs); Assert.Equal(1, s.Ignorees);
            Assert.True(s.Options.Single(o => o.Id == "opt.a").Annulable);
            Assert.False(s.Options.Single(o => o.Id == "opt.b").Annulable);
        }

        private class FauxRestauration : IRestauration
        {
            public List<string> Appels = new List<string>(); public List<string> Oublies = new List<string>();
            public bool Occupe; public bool MemeUser = true; public bool Tenu;
            public Dictionary<string, OutcomeStatus> Statuts = new Dictionary<string, OutcomeStatus>();
            public IReadOnlyList<string> TrouverJournauxActifs(string id) { return new List<string>(); }
            public Task<ItemOutcome> AnnulerParJournalAsync(string j, string id)
            {
                Appels.Add(id);
                OutcomeStatus st; if (!Statuts.TryGetValue(id, out st)) st = OutcomeStatus.Ok;
                return Task.FromResult(new ItemOutcome { Id = id, Status = st, Message = st == OutcomeStatus.Skipped ? "Rien à annuler" : null });
            }
            public Task<ItemOutcome> ExecuterRevertAsync(CatalogItem item) { throw new NotSupportedException(); }
            public IDisposable PrendreVerrou() { if (Occupe || Tenu) return null; Tenu = true; return new Rien(this); }
            public bool MemeUtilisateur() { return MemeUser; }
            public void OublierDernieresApplications(IEnumerable<string> ids) { Oublies.AddRange(ids); }
            private class Rien : IDisposable { private readonly FauxRestauration f; public Rien(FauxRestauration f) { this.f = f; } public void Dispose() { f.Tenu = false; } }
        }

        [Fact]
        public async Task Annuler_la_session_annule_les_options_du_journal_et_les_oublie()
        {
            var r = new SessionReport(); r.Items.Add(new ItemOutcome { Id = "opt.a", Status = OutcomeStatus.Ok }); r.Items.Add(new ItemOutcome { Id = "opt.b", Status = OutcomeStatus.Ok });
            Session("a", "Done", "2026-01-01T10:00:00", r, new[] { "opt.a", "opt.b" });
            var f = new FauxRestauration(); var vm = Vm(f);
            await vm.AnnulerSessionAsync(vm.Sessions[0]);
            Assert.Equal(new[] { "opt.a", "opt.b" }.OrderBy(x => x), f.Appels.OrderBy(x => x));
            Assert.Equal(new[] { "opt.a", "opt.b" }.OrderBy(x => x), f.Oublies.OrderBy(x => x));
        }

        [Fact]
        public async Task Annuler_sous_un_autre_compte_ignore_les_options_hkcu()
        {
            var r = new SessionReport(); r.Items.Add(new ItemOutcome { Id = "opt.a", Status = OutcomeStatus.Ok }); r.Items.Add(new ItemOutcome { Id = "opt.b", Status = OutcomeStatus.Ok });
            Session("a", "Done", "2026-01-01T10:00:00", r, new[] { "opt.a", "opt.b" });
            var cat = new List<CatalogItem> { new CatalogItem { Id = "opt.a", Titre = "A", Hkcu = true }, new CatalogItem { Id = "opt.b", Titre = "B" } };
            var f = new FauxRestauration { MemeUser = false }; var vm = Vm(f, cat);
            await vm.AnnulerSessionAsync(vm.Sessions[0]);
            Assert.Equal(new[] { "opt.b" }, f.Appels.ToArray());
            Assert.Contains("autre compte", vm.Message);
        }

        [Fact]
        public async Task Annuler_avec_verrou_pris_ne_fait_rien()
        {
            var r = new SessionReport(); r.Items.Add(new ItemOutcome { Id = "opt.a", Status = OutcomeStatus.Ok });
            Session("a", "Done", "2026-01-01T10:00:00", r, new[] { "opt.a" });
            var f = new FauxRestauration { Occupe = true }; var vm = Vm(f);
            await vm.AnnulerSessionAsync(vm.Sessions[0]);
            Assert.Empty(f.Appels);
            Assert.Contains("autre exécution", vm.Message);
        }

        [Fact]
        public async Task Quarantaine_du_vm_restaure_et_purge_apres_confirmation()
        {
            string id; var origine = Mettre("d.dll", "x", out id);
            var vm = Vm(); var oui = true; vm.Confirmer = _ => oui;
            Assert.Single(vm.Quarantaine);
            oui = false; vm.PurgerQuarantaine(vm.Quarantaine[0]);
            Assert.True(File.Exists(Path.Combine(paths.QuarantineDir, id)));
            oui = true; vm.RestaurerQuarantaine(vm.Quarantaine[0]);
            Assert.True(File.Exists(origine));
            Assert.Empty(vm.Quarantaine);
            await Task.CompletedTask;
        }

        [Fact]
        public async Task Une_annulation_ignoree_n_est_pas_un_succes()
        {
            var r = new SessionReport(); r.Items.Add(new ItemOutcome { Id = "opt.a", Status = OutcomeStatus.Ok }); r.Items.Add(new ItemOutcome { Id = "opt.b", Status = OutcomeStatus.Ok });
            Session("a", "Done", "2026-01-01T10:00:00", r, new[] { "opt.a", "opt.b" });
            var f = new FauxRestauration(); f.Statuts["opt.b"] = OutcomeStatus.Skipped;
            var vm = Vm(f);
            await vm.AnnulerSessionAsync(vm.Sessions[0]);
            Assert.Equal(new[] { "opt.a" }, f.Oublies.ToArray());
            Assert.StartsWith("1 option(s) annulée(s)", vm.Message);
            Assert.Contains("opt.b", vm.Message);
        }

        [Fact]
        public async Task Les_conflits_sont_calcules_sous_le_verrou()
        {
            var r = new SessionReport(); r.Items.Add(new ItemOutcome { Id = "opt.a", Status = OutcomeStatus.Ok });
            Session("a", "Done", "2026-01-01T10:00:00", r, new[] { "opt.a" });
            var f = new FauxRestauration(); var vm = Vm(f);
            bool? tenu = null;
            vm.TrouverConflits = (j, ids) => { tenu = f.Tenu; return new List<Conflict>(); };
            vm.Confirmer = _ => true;
            await vm.AnnulerSessionAsync(vm.Sessions[0]);
            Assert.True(tenu);
            Assert.Single(f.Appels);

            f.Occupe = true; tenu = null; f.Appels.Clear();
            Session("b", "Done", "2026-01-02T10:00:00", r, new[] { "opt.a" });
            vm.Charger();
            await vm.AnnulerSessionAsync(vm.Sessions.Single(x => x.Id == "b"));
            Assert.Null(tenu); // verrou pris ailleurs : ni conflits ni annulation
            Assert.Empty(f.Appels);
        }

        [Fact]
        public void Session_running_affichee_en_cours_quand_le_verrou_est_pris()
        {
            var r = new SessionReport(); r.Items.Add(new ItemOutcome { Id = "opt.a", Status = OutcomeStatus.Ok });
            Session("a", "Running", "2026-01-01T10:00:00", null, new[] { "opt.a" });
            Session("b", "Running", "2026-01-02T10:00:00", null, new[] { "opt.a" });
            var f = new FauxRestauration { Occupe = true };
            var vm = Vm(f);
            var b = vm.Sessions.Single(x => x.Id == "b");
            Assert.Equal("en cours", b.EtatLibelle);
            Assert.False(b.Anormale);
            Assert.False(b.Annulable);
            Assert.Contains("interrompue", vm.Sessions.Single(x => x.Id == "a").EtatLibelle); // seule la plus récente est en cours

            f.Occupe = false; vm.Charger();
            Assert.Contains("interrompue", vm.Sessions.Single(x => x.Id == "b").EtatLibelle);
            Assert.True(vm.Sessions.Single(x => x.Id == "b").Annulable);
        }

        private string MettreDans(string session, string item, string nomOrigine, out string id)
        {
            var origine = Path.Combine(tmp, "Origine", nomOrigine);
            id = Guid.NewGuid().ToString("N") + "_" + nomOrigine;
            var dest = Path.Combine(paths.QuarantineDir, id);
            File.WriteAllText(dest, "x");
            var sess = Path.Combine(paths.SessionsDir, session);
            Directory.CreateDirectory(sess);
            File.AppendAllText(Path.Combine(sess, "journal.jsonl"), JsonConvert.SerializeObject(new { item, kind = "quarantine", target = origine, existed = true, before = origine, after = dest }) + "\n");
            return origine;
        }

        [Fact]
        public void Restaurer_ou_purger_marque_l_entree_du_journal()
        {
            string id1, id2;
            MettreDans("20260101-100000", "cln.orphans", "a.msi", out id1);
            MettreDans("20260101-100000", "cln.orphans", "b.msi", out id2);
            var jf = Path.Combine(paths.SessionsDir, "20260101-100000", "journal.jsonl");
            var svc = new QuarantineService(paths.QuarantineDir, paths.SessionsDir);
            svc.Restore(id1);
            var actives = Journal.Active(Journal.Read(jf));
            Assert.Single(actives);                                   // b.msi reste à annuler
            Assert.EndsWith("b.msi", actives[0].Target);
            svc.Purge(id2);
            Assert.Empty(Journal.Active(Journal.Read(jf)));           // plus rien : la session n'est plus annulable
            var vm = Vm();
            Assert.False(vm.Sessions.Single(x => x.Id == "20260101-100000").Annulable);
        }

        [Fact]
        public void Marqueur_cible_n_annule_que_son_entree()
        {
            var e = new List<JournalEntry>
            {
                new JournalEntry { Item = "x", Kind = "quarantine", Target = "A" },
                new JournalEntry { Item = "x", Kind = "quarantine", Target = "B" },
                new JournalEntry { Item = "x", Kind = Journal.UndoneKind, Target = "A" },
                new JournalEntry { Item = "x", Kind = "quarantine", Target = "A" }, // réécrite après le marqueur : active
            };
            Assert.Equal(new[] { "B", "A" }, Journal.Active(e).Select(x => x.Target).ToArray());
            e.Add(new JournalEntry { Item = "x", Kind = Journal.UndoneKind });     // marqueur d'option : tout est annulé
            Assert.Empty(Journal.Active(e));
        }

        [Fact]
        public void Marquage_apres_une_ligne_finale_tronquee_garde_le_journal_lisible()
        {
            string id; MettreDans("20260101-100000", "cln.orphans", "c.msi", out id);
            var jf = Path.Combine(paths.SessionsDir, "20260101-100000", "journal.jsonl");
            File.AppendAllText(jf, "{\"item\":\"cln.orph");
            new QuarantineService(paths.QuarantineDir, paths.SessionsDir).Purge(id);
            Assert.Empty(Journal.Active(Journal.Read(jf)));
        }


        // ---------- Désinstallation ----------

        [Fact]
        public async Task La_desinstallation_montre_un_apercu_puis_n_execute_qu_apres_confirmation()
        {
            var vm = Vm(); var appels = new List<bool>(); string texteVu = null; bool ferme = false;
            vm.Desinstaller = confirme =>
            {
                appels.Add(confirme);
                var b = new BilanDesinstallation { Code = confirme ? 0 : 2 };
                b.Actions.Add(confirme ? "Dossier de données supprimé." : "- 3 session(s) seraient supprimées");
                if (!confirme) b.Avertissements.Add("Les réglages appliqués ne sont pas annulés par la désinstallation.");
                return b;
            };
            vm.ApresDesinstallation = () => ferme = true;
            vm.Confirmer = t => { texteVu = t; return false; };
            await vm.DesinstallerAsync();
            Assert.Equal(new[] { false }, appels);
            Assert.Contains("3 session(s)", texteVu); Assert.Contains("Attention : Les réglages appliqués", texteVu);
            Assert.False(ferme); Assert.Equal("Désinstallation annulée.", vm.Message);

            vm.Confirmer = t => true;
            await vm.DesinstallerAsync();
            Assert.Equal(new[] { false, false, true }, appels);
            Assert.True(ferme); Assert.Contains("Dossier de données supprimé.", vm.Message);
        }

        [Fact]
        public void L_apercu_de_desinstallation_ne_supprime_rien()
        {
            var racine = Path.Combine(tmp, "racine-desinstall");
            Directory.CreateDirectory(Path.Combine(racine, "Machines", "PC-1", "Sessions", "20260101-100000"));
            Directory.CreateDirectory(Path.Combine(racine, "Quarantaine")); File.WriteAllText(Path.Combine(racine, "Quarantaine", "x.msi"), "x");
            var b = Desinstallation.Executer(false, racine, @"Local\GamingTweakerTest.Desinstall." + Guid.NewGuid().ToString("N"));
            Assert.Equal(2, b.Code);
            Assert.Contains(b.Actions, a => a.Contains("1 session(s)") && a.Contains("1 fichier(s) en quarantaine"));
            Assert.Equal(2, b.Avertissements.Count);
            Assert.True(File.Exists(Path.Combine(racine, "Quarantaine", "x.msi")));
            Assert.True(Directory.Exists(Path.Combine(racine, "Machines", "PC-1", "Sessions", "20260101-100000")));
        }
    }
}
