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
        /// <summary>Températures à relever (LibreHardwareMonitor n'est chargé que si l'une d'elles est demandée).</summary>
        void ChoisirTemperatures(bool cpu, bool gpu);
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

        private volatile bool tempCpu, tempGpu;
        private Info.TemperaturesCompteur temperatures;
        private bool temperaturesEnErreur;
        public void ChoisirTemperatures(bool cpu, bool gpu) { tempCpu = cpu; tempGpu = gpu; temperaturesEnErreur = false; tickTemperatures = 0; }

        /// <summary>Intervalle de lecture des températures, en relevés de charge (un par seconde).</summary>
        public const int TemperaturesToutesLesNSecondes = 4;
        private int tickTemperatures;
        private double? tempCpuMem, tempGpuMem;
        private string antiTricheMem;

        /// <summary>Anti-triche en cours (nom affiché, null si aucun) ; remplaçable par les tests.</summary>
        public Func<string> DetecterAntiTriche { get; set; } = () => AntiTriche.Actif();

        /// <summary>Vrai au premier relevé puis tous les <see cref="TemperaturesToutesLesNSecondes"/> relevés.</summary>
        public static bool DoitLireTemperatures(int tick) => tick % TemperaturesToutesLesNSecondes == 0;

        /// <summary>Températures demandées (appelé sous le verrou du relevé de charge) ; la bibliothèque est rouverte si le choix change.</summary>
        private void ReleverTemperatures(ChargeSysteme c)
        {
            bool vc = tempCpu, vg = tempGpu;
            // Anti-triche à pilote noyau en cours : la partie processeur de la bibliothèque (pilote PawnIO) est fermée pendant la partie.
            // Les FPS, les charges et la température de la carte graphique (interface du pilote graphique) ne sont pas concernés.
            string ac = null;
            if (vc) { try { ac = DetecterAntiTriche == null ? null : DetecterAntiTriche(); } catch (Exception) { ac = null; } }
            c.AntiTricheCpu = ac;
            if (ac != null) vc = false;
            if ((!vc && !vg) || temperaturesEnErreur)
            {
                if (temperatures != null) { temperatures.Dispose(); temperatures = null; }
                return;
            }
            try
            {
                if (temperatures == null || temperatures.Cpu != vc || temperatures.Gpu != vg)
                {
                    if (temperatures != null) temperatures.Dispose();
                    if (ac != null) TraceAppli.Ecrire("compteur en jeu : " + ac + " actif, température du processeur suspendue");
                    temperatures = new Info.TemperaturesCompteur(vc, vg);
                }
                var t = temperatures.Lire();
                c.TempCpu = t.Item1; c.TempGpu = t.Item2;
            }
            catch (Exception ex)
            {
                // Un échec (bibliothèque, pilote) ne doit pas se répéter chaque seconde : températures coupées jusqu'au prochain choix.
                TraceAppli.Erreur("compteur en jeu : températures", ex);
                temperaturesEnErreur = true;
                if (temperatures != null) { try { temperatures.Dispose(); } catch (Exception) { } temperatures = null; }
            }
        }

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
            if (!ArreterMinuteur(m, 5000)) TraceAppli.Ecrire("compteur en jeu : relevé encore en cours à l'arrêt, terminé en arrière-plan");
            lock (s) { s.Dispose(); if (temperatures != null) { try { temperatures.Dispose(); } catch (Exception) { } temperatures = null; } }
            e.Dispose();
            TraceAppli.Ecrire("compteur en jeu : arrêté");
        }

        /// <summary>
        /// Arrête le minuteur en attendant au plus <paramref name="attenteMs"/> la fin d'un relevé en cours (températures ou GPU lents
        /// sur certains portables). Si l'attente expire, l'événement n'est PAS détruit : le minuteur le signale à la fin du relevé
        /// (détruit trop tôt, ce signal levait ObjectDisposedException hors de tout try et fermait l'appli). Faux si le relevé continue.
        /// </summary>
        public static bool ArreterMinuteur(Timer m, int attenteMs)
        {
            var fin = new ManualResetEvent(false);
            if (!m.Dispose(fin)) { fin.Dispose(); return true; }
            if (!fin.WaitOne(attenteMs)) return false;
            fin.Dispose();
            return true;
        }

        private void ReleverCharge()
        {
            CompteurSysteme s;
            lock (verrou) s = systeme;
            if (s == null) return;
            ChargeSysteme c;
            try
            {
                lock (s)
                {
                    c = s.Relever(cpu, gpu, ram);
                    // Températures (bibliothèque capteurs, lente sur certains portables) : relues toutes les quelques secondes seulement,
                    // la dernière valeur reste affichée entre deux lectures. Une température varie lentement : rien d'utile n'est perdu.
                    if (DoitLireTemperatures(tickTemperatures++)) { ReleverTemperatures(c); tempCpuMem = c.TempCpu; tempGpuMem = c.TempGpu; antiTricheMem = c.AntiTricheCpu; }
                    else { c.TempCpu = tempCpuMem; c.TempGpu = tempGpuMem; c.AntiTricheCpu = antiTricheMem; }
                }
            }
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
