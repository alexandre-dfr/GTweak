using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace GamingTweaker.Info
{
    public class SensorReading
    {
        public string Materiel { get; set; }
        public string Nom { get; set; }
        public string Type { get; set; }
        public float? Value { get; set; }

        public string Affichage
        {
            get
            {
                if (!Value.HasValue) return "--";
                var c = CultureInfo.GetCultureInfo("fr-FR");
                var v = Value.Value;
                switch (Type)
                {
                    case "Temperature": return v.ToString("0.0", c) + " °C";
                    case "Load": case "Control": case "Level": return v.ToString("0.0", c) + " %";
                    case "Clock": return v.ToString("0", c) + " MHz";
                    case "Power": return v.ToString("0.0", c) + " W";
                    case "Voltage": return v.ToString("0.000", c) + " V";
                    case "Fan": return v.ToString("0", c) + " tr/min";
                    case "Data": case "SmallData": return v.ToString("0.0", c) + (Type == "Data" ? " Go" : " Mo");
                    default: return v.ToString("0.0", c);
                }
            }
        }
    }

    /// <summary>
    /// Lecture des capteurs via LibreHardwareMonitor, chargé seulement à la construction (assembly embarqué résolu à la demande).
    /// Jamais instancié en mode silencieux ni à la collecte d'informations.
    /// </summary>
    public interface ICapteurs : IDisposable { IReadOnlyList<SensorReading> Read(); }

    public sealed class Capteurs : ICapteurs
    {
        private readonly Backend backend;

        public Capteurs() { backend = new Backend(); }

        public IReadOnlyList<SensorReading> Read() { return backend.Read(); }

        public void Dispose() { backend.Dispose(); }

        // Tout ce qui touche LibreHardwareMonitor est isolé ici pour que le type Capteurs lui-même ne le charge pas.
        private sealed class Backend : IDisposable
        {
            private readonly object computer;
            [MethodImpl(MethodImplOptions.NoInlining)]
            public Backend() { computer = Ouvrir(); }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object Ouvrir()
            {
                var c = new LibreHardwareMonitor.Hardware.Computer
                {
                    IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true, IsMemoryEnabled = true,
                    IsStorageEnabled = false, IsNetworkEnabled = false, IsControllerEnabled = false, IsBatteryEnabled = false, IsPsuEnabled = false
                };
                c.Open();
                return c;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public IReadOnlyList<SensorReading> Read()
            {
                var res = new List<SensorReading>();
                var c = (LibreHardwareMonitor.Hardware.Computer)computer;
                foreach (var h in c.Hardware) Parcourir(h, res);
                return res;
            }

            private static void Parcourir(LibreHardwareMonitor.Hardware.IHardware h, List<SensorReading> res)
            {
                h.Update();
                foreach (var s in h.Sensors)
                    res.Add(new SensorReading { Materiel = h.Name, Nom = s.Name, Type = s.SensorType.ToString(), Value = s.Value });
                foreach (var sub in h.SubHardware) Parcourir(sub, res);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public void Dispose() { try { ((LibreHardwareMonitor.Hardware.Computer)computer).Close(); } catch { } }
        }
    }
}
