using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GamingTweaker.Core;

namespace GamingTweaker.Info
{
    /// <summary>
    /// Températures du compteur en jeu (LibreHardwareMonitor, chargé seulement si une température est demandée).
    /// Carte graphique : lue par l'interface du pilote (NVIDIA, AMD), sans rien installer. Processeur : demande le pilote PawnIO
    /// (menu Informations) ; sans lui, la valeur reste inconnue. À appeler hors du thread de l'interface.
    /// </summary>
    public sealed class TemperaturesCompteur : IDisposable
    {
        private readonly Backend backend;
        public bool Cpu { get; private set; }
        public bool Gpu { get; private set; }

        public TemperaturesCompteur(bool cpu, bool gpu)
        {
            AssemblyLoader.Register();
            Cpu = cpu; Gpu = gpu;
            backend = new Backend(cpu, gpu);
        }

        /// <summary>(processeur, carte graphique) en °C ; null si inconnue.</summary>
        public Tuple<double?, double?> Lire()
        {
            var l = backend.Lire();
            return Tuple.Create(Cpu ? ChoixTemperature.Cpu(l) : null, Gpu ? ChoixTemperature.Gpu(l) : null);
        }

        public void Dispose() { backend.Dispose(); }

        // Tout ce qui touche LibreHardwareMonitor est isolé ici : le type TemperaturesCompteur lui-même ne le charge pas.
        private sealed class Backend : IDisposable
        {
            private readonly object computer;
            [MethodImpl(MethodImplOptions.NoInlining)]
            public Backend(bool cpu, bool gpu) { computer = Ouvrir(cpu, gpu); }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object Ouvrir(bool cpu, bool gpu)
            {
                var c = new LibreHardwareMonitor.Hardware.Computer
                {
                    IsCpuEnabled = cpu, IsGpuEnabled = gpu, IsMotherboardEnabled = false, IsMemoryEnabled = false,
                    IsStorageEnabled = false, IsNetworkEnabled = false, IsControllerEnabled = false, IsBatteryEnabled = false, IsPsuEnabled = false
                };
                c.Open();
                return c;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public List<LectureTemperature> Lire()
            {
                var res = new List<LectureTemperature>();
                foreach (var h in ((LibreHardwareMonitor.Hardware.Computer)computer).Hardware) Parcourir(h, h.HardwareType.ToString(), res);
                return res;
            }

            private static void Parcourir(LibreHardwareMonitor.Hardware.IHardware h, string type, List<LectureTemperature> res)
            {
                h.Update();
                foreach (var s in h.Sensors)
                    if (s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature)
                        res.Add(new LectureTemperature { Materiel = type, Nom = s.Name, Valeur = s.Value });
                foreach (var sub in h.SubHardware) Parcourir(sub, type, res);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public void Dispose() { try { ((LibreHardwareMonitor.Hardware.Computer)computer).Close(); } catch { } }
        }
    }
}
