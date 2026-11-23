using System;
using System.Collections.Generic;
using System.Linq;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Décide si une option s'applique à cette machine. Une option incompatible n'est pas affichée.
    /// Un fait inconnu (build 0, RAM 0, test d'existence absent) n'écarte jamais une option.
    /// </summary>
    public static class Compatibilite
    {
        public static bool IsCompatible(CompatRule rule, MachineFacts facts)
        {
            if (rule == null) return true;
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            return Matches(rule.Windows, facts.WindowsMajor)
                && Matches(rule.Editions, facts.Edition)
                && Matches(rule.Cpu, facts.CpuVendor)
                && (rule.Gpu == null || rule.Gpu.Count == 0
                    || (facts.GpuVendors ?? new List<string>()).Any(g => Matches(rule.Gpu, g)))
                && (rule.BuildMin == null || facts.WindowsBuild <= 0 || facts.WindowsBuild >= rule.BuildMin.Value)
                && (rule.RamMinGo == null || facts.RamGo <= 0 || facts.RamGo >= rule.RamMinGo.Value)
                && AuMoinsUn(rule.Services, facts.ServiceExiste)
                && AuMoinsUn(rule.Cles, facts.CleExiste);
        }

        /// <summary>Copie des faits sans les tests de pertinence (RAM, services, clés) : seules les conditions de version et de matériel restent.</summary>
        public static MachineFacts SansPertinence(MachineFacts f)
        {
            if (f == null) throw new ArgumentNullException(nameof(f));
            return new MachineFacts
            {
                WindowsMajor = f.WindowsMajor, WindowsBuild = f.WindowsBuild, Edition = f.Edition, CpuVendor = f.CpuVendor,
                GpuVendors = new List<string>(f.GpuVendors ?? new List<string>()), MachineName = f.MachineName, MachineGuid = f.MachineGuid,
                EstPortable = f.EstPortable, RamGo = 0, ServiceExiste = null, CleExiste = null,
            };
        }

        private static bool Matches(List<string> allowed, string value)
        {
            if (allowed == null || allowed.Count == 0) return true;
            return allowed.Any(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase));
        }

        private static bool AuMoinsUn(List<string> noms, Func<string, bool> existe)
        {
            if (noms == null || noms.Count == 0 || existe == null) return true;
            return noms.Any(n => { try { return existe(n); } catch (Exception) { return true; } });
        }
    }
}
