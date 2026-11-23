using System;
using System.Collections.Generic;

namespace GamingTweaker.Core
{
    /// <summary>Faits normalisés sur la machine. WindowsMajor: "10"/"11"; Edition: Home/Pro/Enterprise/Education; vendeurs: Intel/AMD/NVIDIA.</summary>
    public class MachineFacts
    {
        public string WindowsMajor { get; set; } = "";
        /// <summary>Numéro de build de Windows (CurrentBuild) ; 0 si inconnu.</summary>
        public int WindowsBuild { get; set; }
        public string Edition { get; set; } = "";
        public string CpuVendor { get; set; } = "";
        public List<string> GpuVendors { get; set; } = new List<string>();
        /// <summary>Mémoire vive installée, en Go arrondis ; 0 si inconnue.</summary>
        public int RamGo { get; set; }
        public string MachineName { get; set; } = "";
        public string MachineGuid { get; set; } = "";
        /// <summary>Vrai sur un portable (châssis portable ou batterie présente) : les options d'alimentation coûtent de l'autonomie.</summary>
        public bool EstPortable { get; set; }
        /// <summary>Vrai si le service Windows existe (null : inconnu, la condition est alors acceptée).</summary>
        public Func<string, bool> ServiceExiste { get; set; }
        /// <summary>Vrai si la clé de registre (« HKLM\… » ou « HKCU\… ») existe (null : inconnu, la condition est alors acceptée).</summary>
        public Func<string, bool> CleExiste { get; set; }
    }

    /// <summary>Règle de compatibilité. Une liste vide (ou une valeur nulle) accepte tout.</summary>
    public class CompatRule
    {
        public List<string> Windows { get; set; } = new List<string>();
        public List<string> Editions { get; set; } = new List<string>();
        public List<string> Cpu { get; set; } = new List<string>();
        public List<string> Gpu { get; set; } = new List<string>();
        /// <summary>Build minimale de Windows (ex. 26100 = Windows 11 24H2).</summary>
        public int? BuildMin { get; set; }
        /// <summary>Mémoire vive minimale, en Go.</summary>
        public int? RamMinGo { get; set; }
        /// <summary>Au moins un de ces services doit exister.</summary>
        public List<string> Services { get; set; } = new List<string>();
        /// <summary>Au moins une de ces clés de registre doit exister (« HKLM\… » ou « HKCU\… ») : logiciel installé.</summary>
        public List<string> Cles { get; set; } = new List<string>();
    }
}
