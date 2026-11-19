using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Compte les images affichées par chaque processus en lisant, en temps réel, les événements « Present » publiés par Windows
    /// (ETW : fournisseurs Microsoft-Windows-DXGI pour DirectX 10/11/12 et Microsoft-Windows-D3D9 pour DirectX 9), comme PresentMon.
    /// Rien n'est injecté dans le jeu : compatible avec les anti-triche. Les jeux Vulkan ou OpenGL qui ne passent pas par DXGI
    /// ne publient pas ces événements : leurs FPS restent inconnus. Droits administrateur requis (session ETW).
    /// </summary>
    public sealed class CompteurEtw : IDisposable
    {
        public const string NomSession = "GamingTweaker-Compteur";

        private static readonly Guid Dxgi = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        private static readonly Guid D3d9 = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");
        private const ushort DxgiPresentStart = 42;
        private const ushort D3d9PresentStart = 1;

        private readonly object verrou = new object();
        private readonly Dictionary<int, CompteurImages> parProcessus = new Dictionary<int, CompteurImages>();
        private readonly int monPid = Process.GetCurrentProcess().Id;
        private readonly double frequence = Stopwatch.Frequency;
        private readonly long dxgiA, dxgiB, d3d9A, d3d9B;

        private ulong session;
        private ulong lecture = InvalidHandle;
        private Thread fil;
        private Natif.EventRecordCallback rappel; // gardé vivant tant que la session lit
        private IntPtr nomSession = IntPtr.Zero;
        private IntPtr logfile = IntPtr.Zero;
        private bool demarre;

        public CompteurEtw()
        {
            var a = Dxgi.ToByteArray(); dxgiA = BitConverter.ToInt64(a, 0); dxgiB = BitConverter.ToInt64(a, 8);
            var b = D3d9.ToByteArray(); d3d9A = BitConverter.ToInt64(b, 0); d3d9B = BitConverter.ToInt64(b, 8);
        }

        /// <summary>Dernière erreur de démarrage (null si la session lit les événements).</summary>
        public string Erreur { get; private set; }

        public bool Demarre { get { return demarre; } }

        /// <summary>Instant courant sur la même horloge que les événements (QueryPerformanceCounter), en secondes.</summary>
        public double Maintenant { get { return Stopwatch.GetTimestamp() / frequence; } }

        public bool Demarrer()
        {
            if (demarre) return true;
            try
            {
                // Une session laissée par un arrêt brutal porterait le même nom : arrêtée d'abord.
                Arreter(NomSession);
                var props = Proprietes(NomSession);
                try
                {
                    int r = Natif.StartTraceW(out session, NomSession, props);
                    if (r != 0) throw new Win32Exception(r);
                }
                finally { Marshal.FreeHGlobal(props); }

                foreach (var g in new[] { Dxgi, D3d9 })
                {
                    var guid = g;
                    // Tous niveaux, tous mots-clés (le niveau exact de Present_Start varie selon les versions de Windows) :
                    // seuls les événements Present sont retenus dans le rappel.
                    int r = Natif.EnableTraceEx2(session, ref guid, 1 /* ENABLE */, 5, 0, 0, 0, IntPtr.Zero);
                    if (r != 0) throw new Win32Exception(r);
                }

                rappel = SurEvenement;
                nomSession = Marshal.StringToHGlobalUni(NomSession);
                logfile = Marshal.AllocHGlobal(Natif.TailleLogfile);
                for (int i = 0; i < Natif.TailleLogfile; i += 8) Marshal.WriteInt64(logfile, i, 0);
                Marshal.WriteIntPtr(logfile, Natif.OffsetLoggerName, nomSession);
                Marshal.WriteInt32(logfile, Natif.OffsetProcessTraceMode, Natif.ProcessTraceModeRealTime | Natif.ProcessTraceModeEventRecord);
                Marshal.WriteIntPtr(logfile, Natif.OffsetEventRecordCallback, Marshal.GetFunctionPointerForDelegate(rappel));
                lecture = Natif.OpenTraceW(logfile);
                if (lecture == InvalidHandle) throw new Win32Exception(Marshal.GetLastWin32Error());

                var h = lecture;
                fil = new Thread(() =>
                {
                    try { Natif.ProcessTrace(new[] { h }, 1, IntPtr.Zero, IntPtr.Zero); }
                    catch (Exception ex) { TraceAppli.Erreur("compteur en jeu : lecture ETW interrompue", ex); }
                }) { IsBackground = true, Name = "Compteur ETW" };
                fil.Start();
                demarre = true;
                Erreur = null;
                return true;
            }
            catch (Exception ex)
            {
                Erreur = "Lecture des images impossible : " + ex.Message;
                TraceAppli.Erreur("compteur en jeu : session ETW", ex);
                Liberer();
                return false;
            }
        }

        private const ulong InvalidHandle = ulong.MaxValue;

        private void SurEvenement(IntPtr e)
        {
            try
            {
                var a = Marshal.ReadInt64(e, 24); var b = Marshal.ReadInt64(e, 32);
                var id = (ushort)Marshal.ReadInt16(e, 40);
                bool present = (a == dxgiA && b == dxgiB && id == DxgiPresentStart) || (a == d3d9A && b == d3d9B && id == D3d9PresentStart);
                if (!present) return;
                var pid = Marshal.ReadInt32(e, 12);
                if (pid == monPid) return; // l'interface WPF et le compteur lui-même passent par Direct3D 9
                var t = Marshal.ReadInt64(e, 16) / frequence;
                lock (verrou)
                {
                    CompteurImages c;
                    if (!parProcessus.TryGetValue(pid, out c)) parProcessus[pid] = c = new CompteurImages();
                    c.Ajouter(t);
                }
            }
            catch (Exception) { /* jamais d'exception vers le code natif */ }
        }

        /// <summary>Mesure du processus <paramref name="pid"/> (null : il n'affiche rien, ou ses images ne sont pas visibles par ETW).</summary>
        public MesureImages Mesurer(int pid)
        {
            var maintenant = Maintenant;
            lock (verrou)
            {
                // Processus silencieux depuis la fenêtre complète : oubliés.
                foreach (var k in parProcessus.Where(kv => maintenant - kv.Value.Dernier > CompteurImages.FenetreSecondes).Select(kv => kv.Key).ToList())
                    parProcessus.Remove(k);
                CompteurImages c;
                return parProcessus.TryGetValue(pid, out c) ? c.Mesurer(maintenant) : null;
            }
        }

        public void Dispose() { Liberer(); }

        private void Liberer()
        {
            demarre = false;
            if (session != 0) { Arreter(NomSession); session = 0; }
            if (lecture != InvalidHandle) { try { Natif.CloseTrace(lecture); } catch (Exception) { } lecture = InvalidHandle; }
            if (fil != null) { fil.Join(2000); fil = null; }
            if (logfile != IntPtr.Zero) { Marshal.FreeHGlobal(logfile); logfile = IntPtr.Zero; }
            if (nomSession != IntPtr.Zero) { Marshal.FreeHGlobal(nomSession); nomSession = IntPtr.Zero; }
            lock (verrou) parProcessus.Clear();
        }

        private static void Arreter(string nom)
        {
            var props = Proprietes(nom);
            try { Natif.ControlTraceW(0, nom, props, 1 /* STOP */); }
            catch (Exception) { }
            finally { Marshal.FreeHGlobal(props); }
        }

        /// <summary>EVENT_TRACE_PROPERTIES (x64, 120 octets) suivie du nom de la session : temps réel, horodatage QueryPerformanceCounter.</summary>
        private static IntPtr Proprietes(string nom)
        {
            int taille = Natif.TailleProprietes + (nom.Length + 1) * 2;
            var p = Marshal.AllocHGlobal(taille);
            for (int i = 0; i < taille; i++) Marshal.WriteByte(p, i, 0);
            Marshal.WriteInt32(p, 0, taille);                         // Wnode.BufferSize
            var g = Guid.NewGuid().ToByteArray();
            Marshal.Copy(g, 0, p + 24, 16);                           // Wnode.Guid
            Marshal.WriteInt32(p, 40, 1);                             // Wnode.ClientContext : QueryPerformanceCounter
            Marshal.WriteInt32(p, 44, 0x00020000);                    // Wnode.Flags : WNODE_FLAG_TRACED_GUID
            Marshal.WriteInt32(p, 48, 64);                            // BufferSize (Ko)
            Marshal.WriteInt32(p, 52, 4);                             // MinimumBuffers
            Marshal.WriteInt32(p, 56, 32);                            // MaximumBuffers
            Marshal.WriteInt32(p, 64, 0x00000100);                    // LogFileMode : EVENT_TRACE_REAL_TIME_MODE
            Marshal.WriteInt32(p, 68, 1);                             // FlushTimer : 1 s
            Marshal.WriteInt32(p, 116, Natif.TailleProprietes);       // LoggerNameOffset
            return p;
        }

        private static class Natif
        {
            public const int TailleProprietes = 120;
            public const int TailleLogfile = 448;
            public const int OffsetLoggerName = 8;
            public const int OffsetProcessTraceMode = 28;
            public const int OffsetEventRecordCallback = 424;
            public const int ProcessTraceModeRealTime = 0x00000100;
            public const int ProcessTraceModeEventRecord = 0x10000000;

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate void EventRecordCallback(IntPtr eventRecord);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
            public static extern int StartTraceW(out ulong sessionHandle, string sessionName, IntPtr properties);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
            public static extern int ControlTraceW(ulong sessionHandle, string sessionName, IntPtr properties, uint controlCode);

            [DllImport("advapi32.dll")]
            public static extern int EnableTraceEx2(ulong traceHandle, ref Guid providerId, uint controlCode, byte level,
                ulong matchAnyKeyword, ulong matchAllKeyword, uint timeout, IntPtr enableParameters);

            [DllImport("advapi32.dll", SetLastError = true)]
            public static extern ulong OpenTraceW(IntPtr logfile);

            [DllImport("advapi32.dll")]
            public static extern int ProcessTrace(ulong[] handleArray, uint handleCount, IntPtr startTime, IntPtr endTime);

            [DllImport("advapi32.dll")]
            public static extern int CloseTrace(ulong traceHandle);
        }
    }
}
