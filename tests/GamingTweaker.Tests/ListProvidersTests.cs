using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using GamingTweaker.Core;
using GamingTweaker.Lists;
using GamingTweaker.Tests.Helpers;
using Xunit;

namespace GamingTweaker.Tests
{
    /// <summary>Fournisseurs de listes du menu Optimisation : services, services Xbox, démarrage.</summary>
    public class ListProvidersTests
    {
        private sealed class FakeServices : IServiceSource
        {
            public Dictionary<string, ServiceInfo> Donnees = new Dictionary<string, ServiceInfo>(StringComparer.OrdinalIgnoreCase);
            public IReadOnlyDictionary<string, ServiceInfo> Lire(IEnumerable<string> noms)
            {
                var res = new Dictionary<string, ServiceInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var n in noms) if (Donnees.ContainsKey(n)) res[n] = Donnees[n];
                return res;
            }
        }

        private static IReadOnlyList<ListEntry> Charger(IListProvider p) => p.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();

        [Fact]
        public void Les_services_proposes_sont_ceux_de_la_liste_presents_sur_la_machine()
        {
            var src = new FakeServices();
            src.Donnees["Spooler"] = new ServiceInfo { Name = "Spooler", DisplayName = "Spouleur d'impression", StartType = "Automatique" };
            src.Donnees["Fax"] = new ServiceInfo { Name = "Fax", DisplayName = "Télécopie", StartType = "Manuel" };
            src.Donnees["Winmgmt"] = new ServiceInfo { Name = "Winmgmt", DisplayName = "Infrastructure WMI", StartType = "Automatique" };   // hors liste : jamais proposé
            var p = ServicesProvider.Standard(src);
            Assert.Equal("services", p.ListId);
            var l = Charger(p);
            Assert.Equal(new[] { "Fax", "Spooler" }, l.Select(e => e.Key).OrderBy(x => x));
            Assert.DoesNotContain(l, e => e.Key == "Winmgmt");
            var sp = l.Single(e => e.Key == "Spooler");
            Assert.Contains("Spouleur d'impression", sp.Titre);
            Assert.Contains("Spooler", sp.Titre);
            Assert.Contains("Automatique", sp.Detail);
            Assert.Equal(RiskLevel.Avance, sp.Risque);
        }

        [Fact]
        public void Les_services_Xbox_ont_leur_propre_liste_et_le_risque_risque()
        {
            var src = new FakeServices();
            src.Donnees["XblGameSave"] = new ServiceInfo { Name = "XblGameSave", DisplayName = "Sauvegarde de jeu Xbox Live", StartType = "Manuel" };
            src.Donnees["Spooler"] = new ServiceInfo { Name = "Spooler", DisplayName = "Spouleur", StartType = "Automatique" };
            var p = ServicesProvider.Xbox(src);
            Assert.Equal("xbox-services", p.ListId);
            var l = Charger(p);
            Assert.Equal(new[] { "XblGameSave" }, l.Select(e => e.Key));
            Assert.Equal(RiskLevel.Risque, l[0].Risque);
        }

        [Fact]
        public void Une_machine_sans_aucun_service_de_la_liste_donne_une_liste_vide()
        {
            Assert.Empty(Charger(ServicesProvider.Xbox(new FakeServices())));
        }

        [Theory]
        [InlineData(2, false, "Automatique")] [InlineData(2, true, "Automatique (début différé)")]
        [InlineData(3, false, "Manuel")] [InlineData(4, false, "Désactivé")] [InlineData(0, false, "Démarrage")] [InlineData(1, false, "Système")]
        [InlineData(9, false, "Inconnu")]
        public void Le_type_de_demarrage_est_affiche_en_francais(int start, bool differe, string attendu)
        {
            Assert.Equal(attendu, ServiceStartLabel.From(start, differe));
        }

        [Fact]
        public void La_lecture_reelle_des_services_ne_renvoie_que_des_candidats()
        {
            foreach (var p in new[] { ServicesProvider.Standard(), ServicesProvider.Xbox() })
            {
                var cand = p.ListId == "services" ? ServicesProvider.Services : ServicesProvider.XboxServices;
                foreach (var e in Charger(p))
                {
                    Assert.Contains(e.Key, cand);
                    Assert.False(string.IsNullOrWhiteSpace(e.Detail));
                }
            }
        }

        [Fact]
        public void Les_listes_de_services_des_scripts_sont_celles_des_fournisseurs()
        {
            var cat = GamingTweaker.Core.Catalogue.LoadEmbedded().Where(i => i.Menu == "Optimisation").ToDictionary(i => i.Id);
            Assert.Equal(ServicesProvider.Services.OrderBy(x => x), NomsDuScript(cat["opt.services"].Apply).OrderBy(x => x));
            Assert.Equal(ServicesProvider.XboxServices.OrderBy(x => x), NomsDuScript(cat["opt.xbox-services"].Apply).OrderBy(x => x));
            // Liste du plan + celles que la couverture ajoute (CscService, StorSvc, SharedAccess), jamais de DiagTrack (relève de tel.diagtrack-off).
            foreach (var n in new[] { "Fax", "RemoteRegistry", "MapsBroker", "PhoneSvc", "RetailDemo", "WpcMonSvc", "lfsvc", "Spooler", "CscService", "StorSvc", "SharedAccess" })
                Assert.Contains(n, ServicesProvider.Services);
            Assert.DoesNotContain("DiagTrack", ServicesProvider.Services);
            Assert.Equal(new[] { "XblAuthManager", "XblGameSave", "XboxGipSvc", "XboxNetApiSvc" }, ServicesProvider.XboxServices);
        }

        private static string[] NomsDuScript(string apply)
        {
            var m = Regex.Match(apply, @"\$allowed\s*=\s*@\(([^)]*)\)");
            Assert.True(m.Success, "liste $allowed introuvable");
            return Regex.Matches(m.Groups[1].Value, @"'([^']+)'").Cast<Match>().Select(x => x.Groups[1].Value).ToArray();
        }

        // ---- démarrage ----------------------------------------------------------------------------

        private sealed class FakeStartup : IStartupSource
        {
            public List<StartupRaw> Donnees = new List<StartupRaw>();
            public IReadOnlyList<StartupRaw> Lire() => Donnees;
        }

        private static readonly byte[] Actif = { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private static readonly byte[] Inactif = { 3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 };

        [Fact]
        public void L_etat_approuve_d_une_entree_de_demarrage_se_lit_dans_le_premier_octet()
        {
            Assert.False(StartupApproved.EstDesactive(null));
            Assert.False(StartupApproved.EstDesactive(new byte[0]));
            Assert.False(StartupApproved.EstDesactive(Actif));
            Assert.False(StartupApproved.EstDesactive(new byte[] { 6, 0, 0, 0 }));
            Assert.True(StartupApproved.EstDesactive(Inactif));
            Assert.True(StartupApproved.EstDesactive(new byte[] { 7, 0, 0, 0 }));
        }

        [Fact]
        public void Les_cles_de_demarrage_se_composent_et_se_decomposent_meme_avec_des_deux_points()
        {
            var k = StartupKey.Make("HKLM", "Run32", "Foo: bar");
            Assert.Equal("HKLM:Run32:Foo: bar", k);
            string scope, kind, name;
            Assert.True(StartupKey.TryParse(k, out scope, out kind, out name));
            Assert.Equal(new[] { "HKLM", "Run32", "Foo: bar" }, new[] { scope, kind, name });
            Assert.False(StartupKey.TryParse("n'importe quoi", out scope, out kind, out name));
            Assert.False(StartupKey.TryParse("HKXX:Run:a", out scope, out kind, out name));
            Assert.False(StartupKey.TryParse("HKCU:Autre:a", out scope, out kind, out name));
            Assert.False(StartupKey.TryParse("HKCU:Run:", out scope, out kind, out name));
        }

        [Fact]
        public void Le_format_de_cle_est_le_meme_en_C_sharp_et_dans_Common_ps1()
        {
            foreach (var t in new[] { new[] { "HKCU", "Run", "Discord" }, new[] { "HKLM", "Run32", "Foo: bar" }, new[] { "HKCU", "StartupFolder", "a b.lnk" } })
                Assert.Equal(StartupKey.Make(t[0], t[1], t[2]),
                    string.Join("\n", PsCommon.Eval("Write-Output (Get-GTStartupKey '" + t[0] + "' '" + t[1] + "' '" + t[2] + "')")));
        }

        [Fact]
        public void Le_fournisseur_de_demarrage_liste_les_entrees_avec_leur_etat()
        {
            var src = new FakeStartup();
            src.Donnees.Add(new StartupRaw { Scope = "HKCU", Kind = "Run", Name = "Discord", Command = "C:\\Discord\\Update.exe --processStart", Approved = null });
            src.Donnees.Add(new StartupRaw { Scope = "HKLM", Kind = "Run", Name = "Steam", Command = "\"C:\\Steam\\steam.exe\" -silent", Approved = Inactif });
            src.Donnees.Add(new StartupRaw { Scope = "HKLM", Kind = "Run32", Name = "Old", Command = "old.exe", Approved = Actif });
            src.Donnees.Add(new StartupRaw { Scope = "HKCU", Kind = "StartupFolder", Name = "Truc.lnk", Command = "C:\\Users\\x\\Startup\\Truc.lnk", Approved = null });
            var p = new StartupProvider(src);
            Assert.Equal("startup", p.ListId);
            var l = Charger(p);
            Assert.Equal(4, l.Count);
            Assert.Equal(new[] { "HKCU:Run:Discord", "HKLM:Run:Steam", "HKLM:Run32:Old", "HKCU:StartupFolder:Truc.lnk" }, l.Select(e => e.Key));
            Assert.All(l, e => Assert.Equal(RiskLevel.Avance, e.Risque));
            var steam = l.Single(e => e.Key == "HKLM:Run:Steam");
            Assert.Contains("désactivé", steam.Detail);
            Assert.Contains("steam.exe", steam.Detail);
            Assert.DoesNotContain("désactivé", l.Single(e => e.Key == "HKCU:Run:Discord").Detail);
            Assert.Contains("dossier", l.Single(e => e.Key == "HKCU:StartupFolder:Truc.lnk").Detail);
            Assert.Equal("Discord", l[0].Titre);
        }

        [Fact]
        public void Les_entrees_de_demarrage_malformees_sont_ignorees()
        {
            var src = new FakeStartup();
            src.Donnees.Add(new StartupRaw { Scope = "HKCU", Kind = "Run", Name = "", Command = "x" });
            src.Donnees.Add(new StartupRaw { Scope = "HKXX", Kind = "Run", Name = "a", Command = "x" });
            src.Donnees.Add(new StartupRaw { Scope = "HKCU", Kind = "Run", Name = "ok", Command = null });
            var l = Charger(new StartupProvider(src));
            Assert.Equal(new[] { "HKCU:Run:ok" }, l.Select(e => e.Key));
        }

        [Fact]
        public void La_lecture_reelle_du_demarrage_ne_renvoie_que_des_cles_valides()
        {
            foreach (var e in Charger(new StartupProvider()))
            {
                string a, b, c;
                Assert.True(StartupKey.TryParse(e.Key, out a, out b, out c), e.Key);
                Assert.False(string.IsNullOrWhiteSpace(e.Titre));
            }
        }

        [Fact]
        public void Le_fournisseur_de_demarrage_exclut_les_entrees_de_securite()
        {
            var src = new FakeStartup();
            src.Donnees.Add(new StartupRaw { Scope = "HKLM", Kind = "Run", Name = "SecurityHealth", Command = "x" });
            src.Donnees.Add(new StartupRaw { Scope = "HKCU", Kind = "Run", Name = "Windows Defender", Command = "x" });
            src.Donnees.Add(new StartupRaw { Scope = "HKCU", Kind = "Run", Name = "Discord", Command = "x" });
            Assert.Equal(new[] { "HKCU:Run:Discord" }, Charger(new StartupProvider(src)).Select(e => e.Key));
        }
    }
}
