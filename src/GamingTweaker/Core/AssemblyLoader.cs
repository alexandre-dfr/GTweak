using System;
using System.IO;
using System.Reflection;

namespace GamingTweaker.Core
{
    /// <summary>Résout les DLL embarquées en ressources via AppDomain.AssemblyResolve.</summary>
    public static class AssemblyLoader
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            AppDomain.CurrentDomain.AssemblyResolve += OnResolve;
        }

        private static Assembly OnResolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name + ".dll";
            var self = Assembly.GetExecutingAssembly();
            using (var stream = self.GetManifestResourceStream(name))
            {
                if (stream == null) return null;
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    return Assembly.Load(ms.ToArray());
                }
            }
        }
    }
}
