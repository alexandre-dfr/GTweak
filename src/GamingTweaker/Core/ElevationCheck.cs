using System;
using System.Management;
using System.Security.Principal;

namespace GamingTweaker.Core
{
    public static class ElevationCheck
    {
        /// <summary>Vrai si le processus tourne sous le compte qui possède le bureau (explorer.exe). En cas de doute : vrai.</summary>
        public static bool SameUserAsDesktop()
        {
            try
            {
                var moi = WindowsIdentity.GetCurrent().User;
                if (moi == null) return true;
                var sid = ExplorerOwnerSid();
                return sid == null || string.Equals(sid, moi.Value, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return true; }
        }

        private static string ExplorerOwnerSid()
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Process WHERE Name = 'explorer.exe'"))
            using (var procs = searcher.Get())
            {
                foreach (ManagementObject p in procs)
                {
                    using (p)
                    {
                        var res = p.InvokeMethod("GetOwnerSid", null, null);
                        var sid = res == null ? null : res["Sid"] as string;
                        if (!string.IsNullOrEmpty(sid)) return sid;
                    }
                }
            }
            return null;
        }
    }
}
