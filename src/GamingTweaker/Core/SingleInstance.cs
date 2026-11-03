using System;
using System.Threading;

namespace GamingTweaker.Core
{
    public static class SingleInstance
    {
        private const string Nom = "GamingTweaker.Run";

        /// <summary>Renvoie un objet à libérer en fin d'exécution, ou null si une autre instance tourne.</summary>
        public static IDisposable TryAcquire()
        {
            Mutex m;
            bool created;
            try { m = new Mutex(false, @"Global\" + Nom, out created); }
            catch (UnauthorizedAccessException)
            {
                // Mutex Global créé par un autre compte : on se rabat sur la session locale.
                try { m = new Mutex(false, @"Local\" + Nom, out created); }
                catch (UnauthorizedAccessException) { return null; }
            }
            if (!created) { m.Dispose(); return null; }
            return m;
        }
    }
}
