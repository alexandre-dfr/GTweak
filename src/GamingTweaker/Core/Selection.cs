using System.Collections.Generic;

namespace GamingTweaker.Core
{
    /// <summary>Choix de l'utilisateur. Listes : clé = identifiant de l'option, valeur = éléments cochés.</summary>
    public class Selection
    {
        public HashSet<string> OptionIds { get; set; } = new HashSet<string>();
        public Dictionary<string, List<string>> Listes { get; set; } = new Dictionary<string, List<string>>();
    }
}
