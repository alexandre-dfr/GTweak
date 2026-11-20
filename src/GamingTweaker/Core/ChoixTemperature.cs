using System;
using System.Collections.Generic;
using System.Linq;

namespace GamingTweaker.Core
{
    /// <summary>Un capteur de température lu par LibreHardwareMonitor.</summary>
    public class LectureTemperature
    {
        /// <summary>Type de matériel LibreHardwareMonitor : « Cpu », « GpuNvidia », « GpuAmd », « GpuIntel »…</summary>
        public string Materiel { get; set; }
        public string Nom { get; set; }
        public double? Valeur { get; set; }
    }

    /// <summary>
    /// Choix de LA température à afficher parmi les capteurs : celle du boîtier du processeur (Intel « CPU Package », AMD « Core (Tctl/Tdie) »),
    /// et celle du cœur de la carte graphique dédiée (NVIDIA ou AMD avant Intel). Les valeurs hors de 1 à 150 °C sont ignorées.
    /// </summary>
    public static class ChoixTemperature
    {
        private static readonly string[] OrdreCpu = { "CPU Package", "Core (Tctl/Tdie)", "Core (Tdie)", "Core (Tctl)", "Package", "Core Max", "Core Average" };

        private static bool Valide(LectureTemperature l) { return l != null && l.Valeur.HasValue && l.Valeur.Value >= 1 && l.Valeur.Value <= 150; }

        public static double? Cpu(IEnumerable<LectureTemperature> lectures)
        {
            var cpu = (lectures ?? Enumerable.Empty<LectureTemperature>()).Where(l => Valide(l) && string.Equals(l.Materiel, "Cpu", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var nom in OrdreCpu)
            {
                var l = cpu.FirstOrDefault(x => string.Equals(x.Nom, nom, StringComparison.OrdinalIgnoreCase));
                if (l != null) return l.Valeur;
            }
            return cpu.Count > 0 ? cpu.Max(x => x.Valeur) : null;
        }

        public static double? Gpu(IEnumerable<LectureTemperature> lectures)
        {
            var gpu = (lectures ?? Enumerable.Empty<LectureTemperature>()).Where(l => Valide(l) && (l.Materiel ?? "").StartsWith("Gpu", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var type in new[] { "GpuNvidia", "GpuAmd", "GpuIntel" })
            {
                var duType = gpu.Where(x => string.Equals(x.Materiel, type, StringComparison.OrdinalIgnoreCase)).ToList();
                if (duType.Count == 0) continue;
                var coeur = duType.FirstOrDefault(x => string.Equals(x.Nom, "GPU Core", StringComparison.OrdinalIgnoreCase));
                return (coeur ?? duType[0]).Valeur;
            }
            return null;
        }
    }
}
