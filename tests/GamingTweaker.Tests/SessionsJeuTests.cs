using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GamingTweaker.Core;
using GamingTweaker.Ui;
using Xunit;

namespace GamingTweaker.Tests
{
    public class SessionsJeuTests : IDisposable
    {
        private readonly string tmp = Path.Combine(Path.GetTempPath(), "gt-parties-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { Directory.Delete(tmp, true); } catch { } }

        /// <summary>Instants réguliers à <paramref name="fps"/>, avec une image longue de <paramref name="saccadeMs"/> toutes les 100 images.</summary>
        private static double[] Instants(double debut, double fps, int n, double saccadeMs = 0)
        {
            var l = new List<double>(); var t = debut;
            for (int i = 0; i < n; i++) { l.Add(t); t += (saccadeMs > 0 && i % 100 == 99) ? saccadeMs / 1000 : 1.0 / fps; }
            return l.ToArray();
        }

        private static ReleveCompteur Releve(string jeu, double[] instants, double cpu = 40)
        {
            return new ReleveCompteur { Processus = jeu, Images = new MesureImages { Fps = 100, Source = "DirectX", Instants = instants }, Charge = new ChargeSysteme { Cpu = cpu, Gpu = 90, Ram = 50, TempGpu = 70 } };
        }

        [Fact]
        public void Statistiques_d_une_partie_reguliere_avec_saccades()
        {
            var ecarts = Enumerable.Repeat(0.01, 990).Concat(Enumerable.Repeat(0.05, 10)).ToList();
            var r = SessionsJeu.Calculer(ecarts);
            Assert.Equal(1000, r.Images);
            Assert.Equal(1000 / (9.9 + 0.5), r.FpsMoyen, 3);
            Assert.Equal(100, r.UnPourcentBas, 3);        // 99e centile : encore 10 ms
            Assert.Equal(20, r.UnPourMilleBas, 3);        // 99,9e centile : 50 ms
            Assert.Equal(10, r.TempsImageMedianMs, 3);
            Assert.Equal(50, r.TempsImageMaxMs, 3);
            Assert.Equal(10, r.Saccades);
            Assert.Null(SessionsJeu.Calculer(Enumerable.Repeat(0.01, 50).ToList()));
        }

        [Fact]
        public void L_enregistreur_suit_le_jeu_et_ignore_pauses_et_autres_fenetres()
        {
            var e = new EnregistreurSession(new DateTime(2026, 11, 20, 21, 0, 0));
            var a = Instants(100, 100, 300);
            e.Ajouter(Releve("eldenring", a.Take(150).ToArray()));
            e.Ajouter(Releve("eldenring", a.Take(300).ToArray()));          // fenêtre glissante : les instants déjà vus ne comptent pas deux fois
            Assert.Equal(299, e.Images);
            e.Ajouter(Releve("explorer", Instants(200, 60, 100)));           // bureau au premier plan : rien
            e.Ajouter(Releve("eldenring", Instants(110, 100, 200)));         // retour au jeu : la pause n'est pas une image
            Assert.Equal(299 + 199, e.Images);
            e.Ajouter(Releve("eldenring", new[] { 112.0, 115.0, 115.01 }));  // 112 suit 111,99 ; écart de 3 s : chargement, ignoré
            Assert.Equal(299 + 199 + 2, e.Images);
            Assert.Equal("eldenring", e.Jeu);
            var r = e.Terminer(new DateTime(2026, 11, 20, 21, 10, 0));
            Assert.Equal(600, r.DureeSec);
            Assert.Equal(100, r.FpsMoyen, 0);
            Assert.Equal(40, r.CpuMoyen.Value, 3);
            Assert.Equal(70, r.TempGpuMax.Value, 3);
        }

        [Fact]
        public void Rapport_historique_et_comparaison_avec_la_partie_precedente()
        {
            var avant = SessionsJeu.Calculer(Enumerable.Repeat(1 / 100.0, 1000).ToList());
            avant.Jeu = "cs2"; avant.Debut = new DateTime(2026, 11, 19, 20, 0, 0); avant.DureeSec = 600;
            SessionsJeu.Enregistrer(avant, Enumerable.Repeat(10.0, 1000).ToList(), null, tmp);
            var apres = SessionsJeu.Calculer(Enumerable.Repeat(1 / 110.0, 1000).ToList());
            apres.Jeu = "cs2"; apres.Debut = new DateTime(2026, 11, 20, 20, 0, 0); apres.DureeSec = 600;
            var precedente = SessionsJeu.Precedente(SessionsJeu.Lister(tmp), apres);
            Assert.NotNull(precedente);
            var txt = SessionsJeu.Enregistrer(apres, Enumerable.Repeat(9.09, 1000).ToList(), precedente, tmp);

            Assert.Equal("2026-11-20 20h00 - cs2.txt", Path.GetFileName(txt));
            var rapport = File.ReadAllText(txt);
            Assert.Contains("Comparaison avec la partie précédente", rapport);
            Assert.Contains("+10,0 %", rapport);
            Assert.True(File.Exists(Path.ChangeExtension(txt, ".csv")));
            var hist = File.ReadAllLines(Path.Combine(tmp, "historique.csv"));
            Assert.Equal(3, hist.Length);
            Assert.StartsWith("Date;Jeu;", hist[0].TrimStart('﻿'));
            Assert.StartsWith("2026-11-20 20:00;cs2;10,0;1000;110,0;", hist[2]);
            Assert.Contains("+10,0 % de FPS moyen", SessionsJeu.Synthese(apres, precedente));
            var liste = SessionsJeu.Lister(tmp);
            Assert.Equal(new[] { apres.Debut, avant.Debut }, liste.Select(x => x.Debut).ToArray());
        }

        [Fact]
        public void Nom_de_fichier_sans_caracteres_interdits()
        {
            var r = new ResumeSession { Jeu = "a:b/c*d", Debut = new DateTime(2026, 1, 2, 3, 4, 0) };
            Assert.Equal("2026-01-02 03h04 - a_b_c_d", SessionsJeu.NomFichier(r));
        }

        private class SourceParties : ISourceCompteur
        {
            public int Demarrages, Arrets; public ReleveCompteur Releve;
            public void Demarrer() { Demarrages++; }
            public void Arreter() { Arrets++; }
            public void Choisir(bool cpu, bool gpu, bool ram) { }
            public void ChoisirTemperatures(bool cpu, bool gpu) { }
            public ReleveCompteur Lire() { return Releve; }
            public string Erreur { get { return null; } }
        }

        [Fact]
        public void Enregistrement_depuis_le_compteur_meme_masque()
        {
            var src = new SourceParties();
            var c = new CompteurViewModel(Path.Combine(tmp, "machine"), src, new CompteurConfig()) { DossierSessions = Path.Combine(tmp, "parties") };
            var notifications = new List<string>();
            c.NotificationEnregistrement += (t, x) => notifications.Add(t);
            c.BasculerEnregistrement();
            Assert.True(c.EnregistrementEnCours);
            Assert.Equal(1, src.Demarrages);                  // la mesure tourne même compteur masqué
            c.Rafraichir(Releve("cs2", Instants(10, 144, 600)));
            Assert.StartsWith("● REC", c.TexteEnregistrement);
            var rapport = c.ArreterEnregistrement();
            Assert.False(c.EnregistrementEnCours);
            Assert.Equal(1, src.Arrets);                      // compteur masqué : mesure arrêtée
            Assert.True(File.Exists(rapport));
            Assert.Equal(new[] { "Enregistrement de la partie démarré", "Partie enregistrée : cs2" }, notifications.ToArray());

            c.BasculerEnregistrement();
            c.ArreterEnregistrement();
            Assert.Equal("Partie trop courte", notifications.Last());
            c.Dispose();
        }

        [Fact]
        public void Raccourci_d_enregistrement_distinct_de_celui_du_compteur()
        {
            var cfg = new CompteurConfig().Normaliser();
            Assert.Equal("Ctrl+Maj+F10", CompteurViewModel.TexteCombinaison(cfg.RaccourciEnregistrementModificateurs, cfg.RaccourciEnregistrementTouche));
            var meme = new CompteurConfig { RaccourciTouche = 0x79 }.Normaliser();   // compteur sur Ctrl+Maj+F10 : enregistrement sur F9
            Assert.Equal(0x78u, meme.RaccourciEnregistrementTouche);
            var c = new CompteurViewModel(Path.Combine(tmp, "m2"), new SourceParties(), new CompteurConfig());
            Assert.NotNull(c.DefinirRaccourciEnregistrement(c.RaccourciModificateurs, c.RaccourciTouche));
            Assert.Null(c.DefinirRaccourciEnregistrement(RaccourciClavier.Ctrl | RaccourciClavier.Alt, 0x52));
            Assert.Equal("Ctrl+Alt+R", c.RaccourciEnregistrement);
            c.Dispose();
        }
    }
}
