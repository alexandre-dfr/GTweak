using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace GamingTweaker.Core
{
    /// <summary>Ce qu'affiche le compteur en jeu à un instant donné.</summary>
    public class ReleveCompteur
    {
        /// <summary>Images de la fenêtre au premier plan (null : elle n'affiche rien, ou par une API que le compteur ne voit pas).</summary>
        public MesureImages Images { get; set; }
        /// <summary>Nom du processus au premier plan (sans « .exe ») ; vide s'il n'est pas lisible.</summary>
        public string Processus { get; set; } = "";
        public ChargeSysteme Charge { get; set; } = new ChargeSysteme();
    }

    /// <summary>
    /// Compteur en jeu : FPS de la fenêtre au premier plan (<see cref="CompteurEtw"/>) et charge du système (<see cref="CompteurSysteme"/>,
    /// relevée chaque seconde sur un fil à part). Démarré quand le compteur est affiché, arrêté sinon : rien ne tourne en arrière-plan inutilement.
    /// </summary>
    /// <summary>Source des relevés du compteur (remplacée dans les tests).</summary>
    public interface ISourceCompteur
    {
        void Demarrer();
        void Arreter();
        void Choisir(bool cpu, bool gpu, bool ram);
        ReleveCompteur Lire();
        string Erreur { get; }
    }

    public sealed class CompteurService : ISourceCompteur, IDisposable
    {
        private readonly object verrou = new object();
        private CompteurEtw etw;
        private CompteurSysteme systeme;
        private Timer minuteur;
        private ChargeSysteme derniereCharge = new ChargeSysteme();
        private volatile bool cpu = true, gpu = true, ram = true;
        private int pidPremierPlan;
        private string nomPremierPlan = "";

        public bool Actif { get { lock (verrou) return etw != null; } }

        /// <summary>Erreur de lecture des images (droits, session ETW refusée) ; null si tout va bien.</summary>
        public string Erreur { get { lock (verrou) return etw == null ? null : etw.Erreur; } }

        /// <summary>Mesures de charge à relever (les autres ne sont pas calculées).</summary>
        public void Choisir(bool cpu, bool gpu, bool ram) { this.cpu = cpu; this.gpu = gpu; this.ram = ram; }

        public void Demarrer()
        {
            lock (verrou)
            {
                if (etw != null) return;
                etw = new CompteurEtw();
                etw.Demarrer();
                systeme = new CompteurSysteme();
                minuteur = new Timer(_ => ReleverCharge(), null, 0, 1000);
            }
            TraceAppli.Ecrire("compteur en jeu : démarré" + (Erreur == null ? "" : " (" + Erreur + ")"));
        }

        public void Arreter()
        {
            CompteurEtw e; CompteurSysteme s; Timer m;
            lock (verrou)
            {
                if (etw == null) return;
                e = etw; s = systeme; m = minuteur;
                etw = null; systeme = null; minuteur = null;
                derniereCharge = new ChargeSysteme();
            }
            using (var fin = new ManualResetEvent(false)) { if (m.Dispose(fin)) fin.WaitOne(2000); }
            lock (s) s.Dispose();
            e.Dispose();
            TraceAppli.Ecrire("compteur en jeu : arrêté");
        }

        private void ReleverCharge()
        {
            CompteurSysteme s;
            lock (verrou) s = systeme;
            if (s == null) return;
            ChargeSysteme c;
            try { lock (s) c = s.Relever(cpu, gpu, ram); }
            catch (Exception ex) { TraceAppli.Erreur("compteur en jeu : relevé de charge", ex); return; }
            lock (verrou) if (systeme == s) derniereCharge = c;
        }

        /// <summary>Relevé courant (appelé par l'affichage, quelques fois par seconde).</summary>
        public ReleveCompteur Lire()
        {
            CompteurEtw e; ChargeSysteme c;
            lock (verrou) { e = etw; c = derniereCharge; }
            var r = new ReleveCompteur { Charge = c };
            if (e == null) return r;
            var pid = PidPremierPlan();
            if (pid <= 0) return r;
            r.Processus = NomProcessus(pid);
            r.Images = e.Mesurer(pid);
            return r;
        }

        private string NomProcessus(int pid)
        {
            if (pid == pidPremierPlan) return nomPremierPlan;
            pidPremierPlan = pid;
            try { using (var p = Process.GetProcessById(pid)) nomPremierPlan = p.ProcessName; }
            catch (Exception) { nomPremierPlan = ""; }
            return nomPremierPlan;
        }

        private static int PidPremierPlan()
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return 0;
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            return (int)pid;
        }

        public void Dispose() { Arreter(); }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
