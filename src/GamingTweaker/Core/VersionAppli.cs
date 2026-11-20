using System;
using System.Reflection;

namespace GamingTweaker.Core
{
    /// <summary>Version affichée : « 0.9.1 (build 58) » pour un exe de la CI, « 0.9.1 » pour une compilation locale.</summary>
    public static class VersionAppli
    {
        public static string Texte { get { return Formater(Assembly.GetExecutingAssembly().GetName().Version); } }

        /// <summary>Public pour les tests.</summary>
        public static string Formater(Version v)
        {
            if (v == null) return "?";
            var b = v.ToString(3);
            return v.Revision > 0 ? b + " (build " + v.Revision + ")" : b;
        }
    }
}
