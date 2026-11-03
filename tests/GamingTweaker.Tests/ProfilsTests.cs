using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GamingTweaker.Core;
using Xunit;

namespace GamingTweaker.Tests
{
    public class ProfilsTests
    {
        private static readonly MachineFacts Pro11 = new MachineFacts { WindowsMajor = "11", Edition = "Pro", CpuVendor = "Intel" };
        private static readonly MachineFacts Pro10 = new MachineFacts { WindowsMajor = "10", Edition = "Pro", CpuVendor = "Intel" };

        private static readonly List<CatalogItem> Catalogue = new List<CatalogItem>
        {
            new CatalogItem { Id = "opt.a", Risque = RiskLevel.Sur },
            new CatalogItem { Id = "opt.spectre-off", Risque = RiskLevel.Risque },
            new CatalogItem { Id = "win11.only", Compatibilite = new CompatRule { Windows = { "11" } } },
            new CatalogItem { Id = "apps.winget", Liste = "winget" },
            new CatalogItem { Id = "apps.bloat", Liste = "bloatware" },
            new CatalogItem { Id = "gpu.pilote", Liste = "pilote" },
            new CatalogItem { Id = "maj.wu", Liste = "windowsUpdate" },
        };

        private static string Tmp(string name = null)
        {
            var d = Path.Combine(Path.GetTempPath(), "GT_Profils_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return Path.Combine(d, name ?? "p.json");
        }
        private static string Ecrire(string json) { var p = Tmp(); File.WriteAllText(p, json); return p; }
        private static Profil P(params string[] ids) => new Profil { Nom = "t", Options = ids.ToList() };
        private static ResolvedProfil ResolveSur(MachineFacts f, Profil p) => Profils.Resolve(p, Catalogue, f);

        [Fact] public void Aller_retour_conserve_le_profil()
        {
            var p = P("opt.a", "apps.winget");
            p.Listes.Winget = new ListeWinget { Mode = "tout-sauf", Exclusions = { "Vendor.Package" } };
            p.Listes.Pilote = new ListePilote { Branche = "Studio" };
            var path = Tmp(); Profils.Save(p, path);
            var l = Profils.Load(path);
            Assert.Null(l.Erreur);
            Assert.Equal(1, l.Profil.Version);
            Assert.Equal("t", l.Profil.Nom);
            Assert.Equal(p.Options, l.Profil.Options);
            Assert.Equal("tout-sauf", l.Profil.Listes.Winget.Mode);
            Assert.Equal(new[] { "Vendor.Package" }, l.Profil.Listes.Winget.Exclusions);
            Assert.Equal("Studio", l.Profil.Listes.Pilote.Branche);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path), "*.tmp"));
        }

        [Fact] public void Save_ecrase_un_profil_existant()
        {
            var path = Tmp(); Profils.Save(P("opt.a"), path); Profils.Save(P("opt.spectre-off"), path);
            Assert.Equal(new[] { "opt.spectre-off" }, Profils.Load(path).Profil.Options);
        }

        [Fact] public void Id_inconnu_est_signale()
        {
            var r = ResolveSur(Pro11, P("x.y", "opt.a"));
            Assert.Contains("x.y", r.Inconnues);
            Assert.Equal(new[] { "opt.a" }, r.Selection.OptionIds);
        }

        [Fact] public void Option_incompatible_est_signalee()
        {
            var r = ResolveSur(Pro10, P("win11.only", "opt.a"));
            Assert.Contains("win11.only", r.Incompatibles);
            Assert.DoesNotContain("win11.only", r.Selection.OptionIds);
            Assert.Contains("opt.a", r.Selection.OptionIds);
        }

        [Theory, InlineData(""), InlineData("{"), InlineData("{\"version\":999}"), InlineData("{\"version\":0}"), InlineData("{}"), InlineData("[]")]
        public void Profil_invalide_renvoie_une_erreur(string json)
        {
            var l = Profils.Load(Ecrire(json));
            Assert.NotNull(l.Erreur);
            Assert.Null(l.Profil);
        }

        [Fact] public void Version_plus_recente_est_nommee() =>
            Assert.Contains("version plus récente", Profils.Load(Ecrire("{\"version\":999}")).Erreur);

        [Fact] public void Fichier_absent_renvoie_une_erreur_sans_exception() =>
            Assert.NotNull(Profils.Load(Path.Combine(Path.GetTempPath(), "GT_absent_" + Guid.NewGuid().ToString("N") + ".json")).Erreur);

        [Fact] public void Erreur_nomme_le_fichier() =>
            Assert.Contains("mon.json", Profils.Load(WriteNamed("mon.json", "")).Erreur);
        private static string WriteNamed(string n, string c) { var p = Tmp(n); File.WriteAllText(p, c); return p; }

        [Fact] public void Risque_detecte() => Assert.True(Profils.ContientRisque(P("opt.spectre-off"), Catalogue));
        [Fact] public void Pas_de_risque_si_sur() => Assert.False(Profils.ContientRisque(P("opt.a", "x.y"), Catalogue));

        [Fact] public void Listes_converties_en_selection()
        {
            var p = P("apps.winget", "apps.bloat", "gpu.pilote", "maj.wu");
            p.Listes.Winget = new ListeWinget { Mode = "tout-sauf", Exclusions = { "A.B", "C.D" } };
            p.Listes.Bloatware = new List<string> { "Microsoft.BingNews" };
            p.Listes.Pilote = new ListePilote { Branche = "GameReady" };
            p.Listes.WindowsUpdate = new ListeWindowsUpdate { ExclusionsKb = { "KB1" } };
            var l = ResolveSur(Pro11, p).Selection.Listes;
            Assert.Equal(new[] { "*", "!A.B", "!C.D" }, l["apps.winget"]);
            Assert.Equal(new[] { "Microsoft.BingNews" }, l["apps.bloat"]);
            Assert.Equal(new[] { "GameReady" }, l["gpu.pilote"]);
            Assert.Equal(new[] { "*", "!KB1" }, l["maj.wu"]);
        }

        [Fact] public void Winget_mode_liste_est_explicite()
        {
            var p = P("apps.winget");
            p.Listes.Winget = new ListeWinget { Mode = "liste", Elements = { "X.Y" } };
            Assert.Equal(new[] { "X.Y" }, ResolveSur(Pro11, p).Selection.Listes["apps.winget"]);
        }

        [Fact] public void Liste_sans_option_selectionnee_est_ignoree()
        {
            var p = P("opt.a");
            p.Listes.Bloatware = new List<string> { "X" };
            Assert.Empty(ResolveSur(Pro11, p).Selection.Listes);
        }

        [Fact] public void Cle_de_liste_inconnue_est_signalee_sans_echec()
        {
            var l = Profils.Load(Ecrire("{\"version\":1,\"nom\":\"n\",\"options\":[\"opt.a\"],\"listes\":{\"truc\":[1]}}"));
            Assert.Null(l.Erreur);
            Assert.Contains(l.Avertissements, a => a.Contains("truc"));
        }

        [Fact] public void Profil_minimal_charge()
        {
            var l = Profils.Load(Ecrire("{\"version\":1,\"nom\":\"n\",\"options\":[\"opt.a\"]}"));
            Assert.Null(l.Erreur);
            Assert.NotNull(l.Profil.Listes);
        }
    }
}
