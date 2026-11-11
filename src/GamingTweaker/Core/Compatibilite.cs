using System;
using System.Collections.Generic;
using System.Linq;

namespace GamingTweaker.Core
{
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
                    || (facts.GpuVendors ?? new List<string>()).Any(g => Matches(rule.Gpu, g)));
        }

        private static bool Matches(List<string> allowed, string value)
        {
            if (allowed == null || allowed.Count == 0) return true;
            return allowed.Any(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase));
        }
    }
}
