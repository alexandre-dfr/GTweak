using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Security.Principal;

namespace GamingTweaker.Core
{
    public static class ElevationCheck
    {
        /// <summary>Vrai si le processus tourne avec un jeton administrateur élevé.</summary>
        public static bool EstAdministrateur()
        {
            try { using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
            catch (Exception) { return false; }
        }

        /// <summary>Vrai si le processus tourne sous le compte qui possède le bureau de SA session. Non confirmable : faux.</summary>
        public static bool SameUserAsDesktop()
        {
            try
            {
                var moi = WindowsIdentity.GetCurrent().User;
                if (moi == null) return false;
                int session = Process.GetCurrentProcess().SessionId;
                return Decide(moi.Value, session, ExplorerOwners());
            }
            catch (Exception) { return false; }
        }

        /// <summary>SID du propriétaire de l'explorateur de la session courante (utilisateur du bureau), ou null.</summary>
        public static string SidDuBureau()
        {
            int session = Process.GetCurrentProcess().SessionId;
            return ExplorerOwners().Where(o => o.Item1 == session).Select(o => o.Item2).FirstOrDefault();
        }

        /// <summary>Décision pure : seuls les explorer.exe de la session courante comptent ; aucun = faux.</summary>
        public static bool Decide(string currentSid, int currentSession, IEnumerable<Tuple<int, string>> explorerOwners)
        {
            var dansSession = explorerOwners.Where(o => o.Item1 == currentSession).ToList();
            if (dansSession.Count == 0) return false;
            return dansSession.Any(o => string.Equals(o.Item2, currentSid, StringComparison.OrdinalIgnoreCase));
        }

        private static List<Tuple<int, string>> ExplorerOwners()
        {
            var res = new List<Tuple<int, string>>();
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Process WHERE Name = 'explorer.exe'"))
            using (var procs = searcher.Get())
            {
                foreach (ManagementObject p in procs)
                {
                    using (p)
                    {
                        var sid = p.InvokeMethod("GetOwnerSid", null, null)?["Sid"] as string;
                        if (string.IsNullOrEmpty(sid)) continue;
                        res.Add(Tuple.Create((int)Convert.ToUInt32(p["SessionId"]), sid));
                    }
                }
            }
            return res;
        }
    }
}
