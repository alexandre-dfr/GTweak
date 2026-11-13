using System.Collections.Generic;

namespace GamingTweaker.Core
{
    /// <summary>Faits normalisés sur la machine. WindowsMajor: "10"/"11"; Edition: Home/Pro/Enterprise/Education; vendeurs: Intel/AMD/NVIDIA.</summary>
    public class MachineFacts
    {
        public string WindowsMajor { get; set; } = "";
        public string Edition { get; set; } = "";
        public string CpuVendor { get; set; } = "";
        public List<string> GpuVendors { get; set; } = new List<string>();
        public string MachineName { get; set; } = "";
        public string MachineGuid { get; set; } = "";
        /// <summary>Vrai sur un portable (châssis portable ou batterie présente) : les options d'alimentation coûtent de l'autonomie.</summary>
        public bool EstPortable { get; set; }
    }

    /// <summary>Règle de compatibilité. Une liste vide accepte tout.</summary>
    public class CompatRule
    {
        public List<string> Windows { get; set; } = new List<string>();
        public List<string> Editions { get; set; } = new List<string>();
        public List<string> Cpu { get; set; } = new List<string>();
        public List<string> Gpu { get; set; } = new List<string>();
    }
}
