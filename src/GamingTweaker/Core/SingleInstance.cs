using System;
using System.Threading;

namespace GamingTweaker.Core
{
    public static class SingleInstance
    {
        public const string NomParDefaut = @"Global\GamingTweaker.Run";

        /// <summary>Renvoie un objet à libérer en fin d'exécution, ou null si une autre instance tourne.</summary>
        public static IDisposable TryAcquire(string nom = null)
        {
            Mutex m;
            bool created;
            // Accès refusé : le mutex appartient à une instance d'un autre compte, donc une instance tourne déjà.
            try { m = new Mutex(false, nom ?? NomParDefaut, out created); }
            catch (UnauthorizedAccessException) { return null; }
            if (!created) { m.Dispose(); return null; }
            return m;
        }
    }
}
