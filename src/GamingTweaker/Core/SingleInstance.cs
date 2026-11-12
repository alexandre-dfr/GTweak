using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Verrou d'instance (interface, routine, annulations, Mode Jeu). Le mutex est créé avec une DACL explicite (SYSTEM, Administrateurs
    /// et le compte courant) : un processus d'un autre compte ne peut ni l'ouvrir ni le tenir. Si un processus non élevé a déjà créé un
    /// mutex de ce nom pour bloquer l'outil, l'ouverture échoue avec « accès refusé » et un nom dérivé réservé aux administrateurs est utilisé.
    /// </summary>
    public static class SingleInstance
    {
        public const string NomParDefaut = @"Global\GamingTweaker.Run";

        /// <summary>Suffixe du nom de repli quand le nom principal est tenu par un compte étranger.</summary>
        public const string SuffixeRepli = ".admin";

        /// <summary>Renvoie un objet à libérer en fin d'exécution, ou null si une autre instance tourne.</summary>
        public static IDisposable TryAcquire(string nom = null)
        {
            var name = nom ?? NomParDefaut;
            var sec = Securite();
            Mutex m; bool created;
            try { m = Creer(name, sec, out created); }
            catch (UnauthorizedAccessException)
            {
                // Nom usurpé par un autre compte : le repli ne peut être pris que par SYSTEM, un administrateur ou nous-mêmes.
                try { m = Creer(name + SuffixeRepli, sec, out created); }
                catch (UnauthorizedAccessException) { return null; }
            }
            if (!created) { m.Dispose(); return null; }
            return m;
        }

        private static Mutex Creer(string name, MutexSecurity sec, out bool created)
        {
            return sec == null ? new Mutex(false, name, out created) : new Mutex(false, name, out created, sec);
        }

        /// <summary>DACL du mutex : SYSTEM, Administrateurs et le compte courant en contrôle total ; personne d'autre.</summary>
        internal static MutexSecurity Securite()
        {
            try
            {
                var sec = new MutexSecurity();
                sec.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), MutexRights.FullControl, AccessControlType.Allow));
                sec.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), MutexRights.FullControl, AccessControlType.Allow));
                using (var id = WindowsIdentity.GetCurrent())
                    if (id.User != null) sec.AddAccessRule(new MutexAccessRule(id.User, MutexRights.FullControl, AccessControlType.Allow));
                return sec;
            }
            catch (Exception) { return null; }   // sans DACL explicite : comportement d'origine
        }
    }
}
