using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingTweaker.Core;
using GamingTweaker.Lists;
using GamingTweaker.Ui;
using Xunit;

namespace GamingTweaker.Tests
{
    public class ListeSelectionTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-liste-" + Guid.NewGuid().ToString("N"));

        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        internal class Fournisseur : IListProvider, IListDefaults
        {
            public string ListId { get; set; } = "startup";
            public int Appels;
            public Exception Echec;
            public List<ListEntry> Entrees = new List<ListEntry>
            {
                new ListEntry { Key = "HKCU:Run:Mon App", Titre = "Mon App", Detail = "Utilisateur", Risque = RiskLevel.Avance },
                new ListEntry { Key = "HKCU:Run:Autre", Titre = "Autre", Detail = "Utilisateur", Risque = RiskLevel.Sur },
                new ListEntry { Key = "HKLM:Run:Tiers", Titre = "Tiers", Detail = "Machine", Risque = RiskLevel.Risque },
            };
            public IReadOnlyList<string> SelectionParDefaut { get; set; } = new string[0];
            public Task<IReadOnlyList<ListEntry>> LoadAsync(CancellationToken ct)
            {
                Interlocked.Increment(ref Appels);
                if (Echec != null) throw Echec;
                return Task.FromResult<IReadOnlyList<ListEntry>>(Entrees);
            }
        }

        internal static CatalogItem It(string id, string liste = null)
            => new CatalogItem { Id = id, Menu = "Optimisation", Groupe = "G", Titre = "Titre " + id, Description = "d", Risque = RiskLevel.Avance, Reversible = true, Liste = liste };

        internal MainViewModel Cree(Fournisseur f, params CatalogItem[] items)
        {
            var reg = new ListProviderRegistry();
            if (f != null) reg.Register(f);
            var paths = new DataPaths { ProfilsDir = Path.Combine(tmp, "Profils"), MachineDir = Path.Combine(tmp, "M"), SessionsDir = Path.Combine(tmp, "M", "S"), QuarantineDir = Path.Combine(tmp, "Q") };
            return new MainViewModel(items.ToList(), new MachineFacts { WindowsMajor = "10", Edition = "Pro" }, paths,
                its => Task.FromResult(its.ToDictionary(i => i.Id, i => DetectState.NotApplied)), null, o => true, reg);
        }

        [Fact]
        public void Registre_retrouve_un_fournisseur_par_identifiant()
        {
            var reg = new ListProviderRegistry(); var f = new Fournisseur();
            reg.Register(f);
            Assert.Same(f, reg.Get("startup")); Assert.Null(reg.Get("autre")); Assert.Null(reg.Get(null));
        }

        [Fact]
        public void Option_sans_liste_ou_sans_fournisseur_n_a_pas_de_liste()
        {
            var vm = Cree(new Fournisseur(), It("a.b"), It("c.d", "inconnue"));
            Assert.All(vm.Options, o => Assert.Null(o.Liste));
        }

        [Fact]
        public async Task Tous_et_exclusions_donnent_etoile_et_points_d_exclamation()
        {
            var vm = Cree(new Fournisseur(), It("opt.start", "startup"));
            var o = vm.Options.Single(); await o.Liste.ChargerAsync();
            o.Liste.Tous = true;
            o.Liste.Elements.Single(e => e.Key == "HKCU:Run:Autre").IsChecked = false;
            Assert.Equal(new[] { "*", "!HKCU:Run:Autre" }, o.Liste.VersSelection());
            o.IsChecked = true;
            Assert.Equal(new[] { "*", "!HKCU:Run:Autre" }, vm.BuildSelection().Listes["opt.start"]);
            Assert.Contains("opt.start", vm.BuildSelection().OptionIds);
        }

        [Fact]
        public async Task Liste_explicite_contient_les_coches()
        {
            var vm = Cree(new Fournisseur(), It("opt.start", "startup"));
            var o = vm.Options.Single(); await o.Liste.ChargerAsync();
            o.Liste.Elements.Single(e => e.Key == "HKCU:Run:Mon App").IsChecked = true;
            o.Liste.Elements.Single(e => e.Key == "HKLM:Run:Tiers").IsChecked = true;
            Assert.False(o.Liste.Tous);
            Assert.Equal(new[] { "HKCU:Run:Mon App", "HKLM:Run:Tiers" }, o.Liste.VersSelection());
            Assert.Contains("2", o.Liste.Resume);
        }

        [Fact]
        public async Task Cocher_Tous_coche_tous_les_elements()
        {
            var vm = Cree(new Fournisseur(), It("opt.start", "startup"));
            var o = vm.Options.Single(); await o.Liste.ChargerAsync();
            o.Liste.Tous = true;
            Assert.All(o.Liste.Elements, e => Assert.True(e.IsChecked));
            Assert.Equal(new[] { "*" }, o.Liste.VersSelection());
            Assert.Contains("Tous", o.Liste.Resume);
        }

        [Fact]
        public async Task Chargement_unique_meme_appele_deux_fois_en_parallele()
        {
            var f = new Fournisseur(); var vm = Cree(f, It("opt.start", "startup"));
            var l = vm.Options.Single().Liste;
            await Task.WhenAll(l.ChargerAsync(), l.ChargerAsync());
            await l.ChargerAsync();
            Assert.Equal(1, f.Appels); Assert.True(l.Charge); Assert.Equal(3, l.Elements.Count);
        }

        [Fact]
        public async Task Erreur_du_fournisseur_est_affichee_sans_planter_et_reessayable()
        {
            var f = new Fournisseur { Echec = new InvalidOperationException("boum") };
            var vm = Cree(f, It("opt.start", "startup"));
            var l = vm.Options.Single().Liste;
            await l.ChargerAsync();
            Assert.False(l.Charge); Assert.Contains("Impossible", l.Erreur); Assert.DoesNotContain("boum", l.Erreur); Assert.Contains("boum", l.ErreurDetail);
            f.Echec = null;
            await l.ChargerAsync();
            Assert.True(l.Charge); Assert.Equal("", l.Erreur); Assert.Equal("", l.ErreurDetail); Assert.Equal(2, f.Appels);
        }

        [Fact]
        public void Option_cochee_avec_liste_vide_est_exclue_et_signalee()
        {
            var vm = Cree(new Fournisseur(), It("opt.start", "startup"), It("opt.simple"));
            vm.Options.Single(o => o.Id == "opt.start").IsChecked = true;
            vm.Options.Single(o => o.Id == "opt.simple").IsChecked = true;
            var s = vm.BuildSelection();
            Assert.DoesNotContain("opt.start", s.OptionIds); Assert.Contains("opt.simple", s.OptionIds);
            var ligne = vm.BuildPreview().Single(r => r.Titre.Contains("opt.start"));
            Assert.Contains("aucun élément choisi", ligne.Titre);
            Assert.DoesNotContain("aucun", vm.BuildPreview().Single(r => r.Titre.Contains("opt.simple")).Titre);
        }

        [Fact]
        public void Selection_par_defaut_du_fournisseur_a_la_coche()
        {
            var f = new Fournisseur { SelectionParDefaut = new[] { "HKCU:Run:Autre" } };
            var vm = Cree(f, It("opt.start", "startup"));
            vm.Options.Single().IsChecked = true;
            Assert.Equal(new[] { "HKCU:Run:Autre" }, vm.BuildSelection().Listes["opt.start"]);
        }

        [Fact]
        public async Task Selection_restauree_avant_chargement_est_appliquee_au_chargement_et_les_absents_sont_signales()
        {
            var vm = Cree(new Fournisseur(), It("opt.start", "startup"));
            var l = vm.Options.Single().Liste;
            l.DepuisSelection(new[] { "HKCU:Run:Mon App", "HKCU:Run:Disparu" });
            Assert.Equal(new[] { "HKCU:Run:Mon App", "HKCU:Run:Disparu" }, l.VersSelection());
            await l.ChargerAsync();
            Assert.True(l.Elements.Single(e => e.Key == "HKCU:Run:Mon App").IsChecked);
            Assert.False(l.Elements.Single(e => e.Key == "HKCU:Run:Autre").IsChecked);
            var absent = l.Elements.Single(e => e.Key == "HKCU:Run:Disparu");
            Assert.True(absent.Absent); Assert.True(absent.IsChecked);
            Assert.Equal(new[] { "HKCU:Run:Mon App", "HKCU:Run:Disparu" }, l.VersSelection());
        }

        [Fact]
        public async Task Exclusion_d_un_element_absent_est_conservee()
        {
            var vm = Cree(new Fournisseur(), It("opt.start", "startup"));
            var l = vm.Options.Single().Liste;
            l.DepuisSelection(new[] { "*", "!HKCU:Run:Disparu" });
            await l.ChargerAsync();
            Assert.True(l.Tous);
            Assert.Equal(new[] { "*", "!HKCU:Run:Disparu" }, l.VersSelection());
        }

        [Fact]
        public async Task Profil_aller_retour_avec_cle_generique_a_espaces()
        {
            var vm = Cree(new Fournisseur(), It("opt.start", "startup"));
            var o = vm.Options.Single(); await o.Liste.ChargerAsync();
            o.Liste.Elements.Single(e => e.Key == "HKCU:Run:Mon App").IsChecked = true;
            o.IsChecked = true;
            Directory.CreateDirectory(tmp);
            var chemin = Path.Combine(tmp, "p.json");
            Profils.Save(vm.ToProfil("p"), chemin);

            var vm2 = Cree(new Fournisseur(), It("opt.start", "startup"));
            var load = Profils.Load(chemin, new[] { "startup" });
            Assert.Null(load.Erreur); Assert.Empty(load.Avertissements);
            vm2.LoadProfil(load.Profil);
            Assert.True(vm2.Options.Single().IsChecked);
            Assert.Equal(new[] { "HKCU:Run:Mon App" }, vm2.BuildSelection().Listes["opt.start"]);
            Assert.Equal(1, vm2.SelectedCount);
        }

        [Fact]
        public void Profil_tous_avec_exclusion_survit_a_l_enregistrement_sans_chargement_du_fournisseur()
        {
            var vm = Cree(new Fournisseur(), It("opt.start", "startup"));
            var o = vm.Options.Single();
            o.Liste.DepuisSelection(new[] { "*", "!HKCU:Run:Autre" });
            o.IsChecked = true;
            Directory.CreateDirectory(tmp);
            var chemin = Path.Combine(tmp, "p.json");
            Profils.Save(vm.ToProfil("p"), chemin);
            var load = Profils.Load(chemin, new[] { "startup" });
            Assert.Null(load.Erreur);
            var res = Profils.Resolve(load.Profil, vm.Catalogue, new MachineFacts { WindowsMajor = "10", Edition = "Pro" });
            Assert.Equal(new[] { "*", "!HKCU:Run:Autre" }, res.Selection.Listes["opt.start"]);
        }

        private static Profil ProfilAvecServices()
            => new Profil { Nom = "t", Options = new List<string> { "opt.svc" }, Listes = new ProfilListes { Services = new List<string> { "Fax" } } };

        [Fact]
        public async Task Valeur_perimee_d_un_profil_charge_ne_survit_pas_a_l_enregistrement()
        {
            var f = new Fournisseur { ListId = "services", Entrees = new List<ListEntry> { new ListEntry { Key = "Fax", Titre = "Fax", Risque = RiskLevel.Avance }, new ListEntry { Key = "Spooler", Titre = "Spooler", Risque = RiskLevel.Avance } } };
            var vm = Cree(f, It("opt.svc", "services"));
            vm.LoadProfil(ProfilAvecServices());
            var o = vm.Options.Single();
            Assert.Equal(new[] { "Fax" }, vm.ToProfil("p").Listes.Services);
            await o.Liste.ChargerAsync();
            foreach (var e in o.Liste.Elements) e.IsChecked = false;   // liste vide
            Assert.Null(vm.ToProfil("p").Listes.Services);
            o.Liste.Elements[0].IsChecked = true;
            Assert.Equal(new[] { o.Liste.Elements[0].Key }, vm.ToProfil("p").Listes.Services);
            o.IsChecked = false;                                         // option décochée : clé retirée aussi
            Assert.Null(vm.ToProfil("p").Listes.Services);
        }

        [Fact]
        public void Valeur_perimee_d_une_cle_generique_est_retiree_quand_la_liste_devient_vide()
        {
            var vm = Cree(new Fournisseur(), It("opt.start", "startup"));
            var p = new Profil { Nom = "t", Options = new List<string> { "opt.start" }, Listes = new ProfilListes { Inconnues = new Dictionary<string, Newtonsoft.Json.Linq.JToken> { { "startup", new Newtonsoft.Json.Linq.JArray("HKCU:Run:Mon App") } } } };
            vm.LoadProfil(p);
            vm.Options.Single().Liste.DepuisSelection(new string[0]);
            Assert.True(vm.ToProfil("p").Listes.Inconnues == null || !vm.ToProfil("p").Listes.Inconnues.ContainsKey("startup"));
        }

        [Fact]
        public async Task Tout_n_est_pas_propose_quand_la_cle_de_profil_ne_peut_pas_le_contenir()
        {
            var vm = Cree(new Fournisseur { ListId = "services" }, It("opt.svc", "services"));
            var l = vm.Options.Single().Liste;
            Assert.False(l.SupporteTous);
            await l.ChargerAsync();
            l.Tous = true;
            Assert.False(l.Tous);
            Assert.DoesNotContain("*", l.VersSelection());
            Assert.True(Cree(new Fournisseur(), It("opt.start", "startup")).Options.Single().Liste.SupporteTous);
        }

        [Fact]
        public void Lancer_et_apercu_exigent_une_option_executable()
        {
            var vm = Cree(new Fournisseur(), It("opt.start", "startup"));
            vm.Options.Single().IsChecked = true;                        // liste vide : rien à exécuter
            Assert.False(vm.AUneSelection);
            vm.Options.Single().Liste.DepuisSelection(new[] { "HKCU:Run:Autre" });
            Assert.True(vm.AUneSelection);
        }

        [Fact]
        public void Apercu_affiche_le_resume_de_la_liste()
        {
            var vm = Cree(new Fournisseur(), It("opt.start", "startup"));
            var o = vm.Options.Single();
            o.Liste.DepuisSelection(new[] { "*", "!a", "!b" });
            o.IsChecked = true;
            Assert.Contains("Tous sauf 2", vm.BuildPreview().Single().Titre);
            o.Liste.DepuisSelection(new[] { "a", "b", "c" });
            Assert.Contains("3 sélectionnés", vm.BuildPreview().Single().Titre);
        }

        [Fact]
        public async Task Nom_non_ascii_survit_a_l_aller_retour_du_profil()
        {
            var f = new Fournisseur();
            f.Entrees.Add(new ListEntry { Key = "HKCU:Run:Café d'Été & Cie, ®", Titre = "Café", Detail = "", Risque = RiskLevel.Sur });
            var vm = Cree(f, It("opt.start", "startup"));
            var o = vm.Options.Single(); await o.Liste.ChargerAsync();
            o.Liste.Elements.Single(e => e.Key.Contains("Café")).IsChecked = true;
            o.IsChecked = true;
            Directory.CreateDirectory(tmp);
            var chemin = Path.Combine(tmp, "p.json");
            Profils.Save(vm.ToProfil("p"), chemin);
            var load = Profils.Load(chemin, new[] { "startup" });
            Assert.Null(load.Erreur);
            var res = Profils.Resolve(load.Profil, vm.Catalogue, new MachineFacts { WindowsMajor = "10", Edition = "Pro" });
            Assert.Equal(new[] { "HKCU:Run:Café d'Été & Cie, ®" }, res.Selection.Listes["opt.start"]);
        }

        [Fact]
        public void Liste_typee_services_reste_enregistree_en_cle_typee()
        {
            var f = new Fournisseur { ListId = "services" };
            var vm = Cree(f, It("opt.svc", "services"));
            var o = vm.Options.Single();
            o.Liste.DepuisSelection(new[] { "Fax", "Spooler" });
            o.IsChecked = true;
            var p = vm.ToProfil("p");
            Assert.Equal(new[] { "Fax", "Spooler" }, p.Listes.Services);
        }
    }

    public class ProfilsListesGeneriquesTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-pl-" + Guid.NewGuid().ToString("N"));
        public ProfilsListesGeneriquesTests() { Directory.CreateDirectory(tmp); }
        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        private string Ecrire(string json) { var p = Path.Combine(tmp, Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(p, json); return p; }
        private static string Json(string listes) => "{\"version\":1,\"nom\":\"n\",\"options\":[\"opt.a\"],\"listes\":" + listes + "}";
        private static readonly string[] Cles = { "startup", "services" };

        [Fact]
        public void Cle_generique_connue_avec_etoile_et_exclusion_est_acceptee_et_rattachee()
        {
            var l = Profils.Load(Ecrire(Json("{\"startup\":[\"*\",\"!HKCU:Run:Mon App\"]}")), Cles);
            Assert.Null(l.Erreur); Assert.Empty(l.Avertissements);
            var cat = new List<CatalogItem> { new CatalogItem { Id = "opt.a", Menu = "M", Titre = "t", Liste = "startup" } };
            var r = Profils.Resolve(l.Profil, cat, new MachineFacts { WindowsMajor = "10", Edition = "Pro" });
            Assert.Equal(new[] { "*", "!HKCU:Run:Mon App" }, r.Selection.Listes["opt.a"]);
        }

        [Theory]
        [InlineData("[\"HKCU:Run:Café\"]")]
        [InlineData("[\"HKCU:Run:L'appli\"]")]
        [InlineData("[\"HKCU:Run:A & B, C\"]")]
        [InlineData("[\"!HKCU:Run:Logitech®\"]")]
        [InlineData("[\"HKCU:Run:Спутник 日本\"]")]
        public void Element_unicode_est_accepte(string valeur)
        {
            Assert.Null(Profils.Load(Ecrire(Json("{\"startup\":" + valeur + "}")), Cles).Erreur);
        }

        [Theory]
        [InlineData("[\"a\nb\"]")]
        [InlineData("[\"a\u0000b\"]")]
        [InlineData("[\"a`b\"]")]
        [InlineData("[\"a<b\"]")]
        [InlineData("[\"a>b\"]")]
        [InlineData("[\"a\u202eb\"]")]
        [InlineData("[\"\u0301a\"]")]
        [InlineData("[\"a;b\"]")]
        [InlineData("[\"$(x)\"]")]
        [InlineData("[\"a\\\"b\"]")]
        [InlineData("[\"\"]")]
        [InlineData("[\"!\"]")]
        [InlineData("[\"!!x\"]")]
        [InlineData("[\"a|b\"]")]
        [InlineData("[1]")]
        [InlineData("\"texte\"")]
        public void Element_invalide_d_une_cle_generique_est_une_erreur_de_chargement(string valeur)
        {
            var l = Profils.Load(Ecrire(Json("{\"startup\":" + valeur + "}")), Cles);
            Assert.NotNull(l.Erreur); Assert.Null(l.Profil);
        }

        [Fact]
        public void Cle_hors_catalogue_reste_un_avertissement()
        {
            var l = Profils.Load(Ecrire(Json("{\"truc\":[\"x;y\"]}")), Cles);
            Assert.Null(l.Erreur); Assert.Contains(l.Avertissements, a => a.Contains("truc"));
        }

        [Fact]
        public void Les_cles_typees_gardent_leurs_regles()
        {
            Assert.NotNull(Profils.Load(Ecrire(Json("{\"services\":[\"*\"]}")), Cles).Erreur);
            Assert.NotNull(Profils.Load(Ecrire(Json("{\"services\":[\"a b\"]}")), Cles).Erreur);
            Assert.Null(Profils.Load(Ecrire(Json("{\"services\":[\"Fax\"]}")), Cles).Erreur);
        }
    }
}
