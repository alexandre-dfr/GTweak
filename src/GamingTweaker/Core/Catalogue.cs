using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace GamingTweaker.Core
{
    public enum RiskLevel { Sur, Avance, Risque }

    public class CatalogItem
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("menu")] public string Menu { get; set; }
        [JsonProperty("groupe")] public string Groupe { get; set; }
        [JsonProperty("titre")] public string Titre { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("risque")] public RiskLevel? Risque { get; set; }
        [JsonProperty("reversible")] public bool Reversible { get; set; }
        [JsonProperty("redemarrage")] public bool Redemarrage { get; set; }
        [JsonProperty("testable")] public bool Testable { get; set; } = true;
        [JsonProperty("hkcu")] public bool Hkcu { get; set; } = false;
        [JsonProperty("delaiSec")] public int DelaiSec { get; set; } = 300;
        /// <summary>Identifiant optionnel d'un fournisseur de liste.</summary>
        [JsonProperty("liste")] public string Liste { get; set; }
        [JsonProperty("compatibilite")] public CompatRule Compatibilite { get; set; } = new CompatRule();

        [JsonIgnore] public string Detect { get; set; }
        [JsonIgnore] public string Apply { get; set; }
        [JsonIgnore] public string Revert { get; set; }
        /// <summary>Script optionnel ; renvoie un nombre d'octets.</summary>
        [JsonIgnore] public string Estimate { get; set; }
    }

    public static class Catalogue
    {
        public static IReadOnlyList<CatalogItem> LoadEmbedded()
        {
            // Register() avant tout type Newtonsoft : la méthode qui l'utilise est compilée à part.
            AssemblyLoader.Register();
            return LoadCore();
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static IReadOnlyList<CatalogItem> LoadCore()
        {
            var asm = typeof(Catalogue).Assembly;
            var items = new List<CatalogItem>();
            foreach (var name in asm.GetManifestResourceNames()
                .Where(n => n.StartsWith("catalog/", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.Ordinal))
            {
                items.AddRange(Parse(name, ReadText(asm, name)));
            }
            foreach (var it in items)
            {
                if (string.IsNullOrEmpty(it.Id)) continue;
                it.Detect = ReadText(asm, "scripts/" + it.Id + ".detect.ps1", true);
                it.Apply = ReadText(asm, "scripts/" + it.Id + ".apply.ps1", true);
                it.Revert = ReadText(asm, "scripts/" + it.Id + ".revert.ps1", true);
                it.Estimate = ReadText(asm, "scripts/" + it.Id + ".estimate.ps1", true);
            }
            return items;
        }

        /// <summary>Analyse un fichier de catalogue ; toute erreur est signalée en français avec le nom du fichier.</summary>
        public static List<CatalogItem> Parse(string fileName, string jsonText)
        {
            List<CatalogItem> list;
            try
            {
                list = JsonConvert.DeserializeObject<List<CatalogItem>>(jsonText);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(fileName + " : JSON invalide (" + ex.Message + "). Valeurs de risque admises : Sur, Avance, Risque.", ex);
            }
            if (list == null) throw new InvalidDataException(fileName + " : le fichier doit contenir une liste d'options.");
            for (var i = 0; i < list.Count; i++)
                if (list[i] == null) throw new InvalidDataException(fileName + " : l'élément n° " + (i + 1) + " est vide (null).");
            return list;
        }

        private static string ReadText(Assembly asm, string resource, bool optional = false)
        {
            using (var s = asm.GetManifestResourceStream(resource))
            {
                if (s == null)
                {
                    if (optional) return null;
                    throw new InvalidOperationException("Ressource introuvable : " + resource);
                }
                using (var r = new StreamReader(s, new UTF8Encoding(false), true)) return r.ReadToEnd();
            }
        }

        public static List<string> Validate(IEnumerable<CatalogItem> items)
        {
            var errors = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var it in items)
            {
                if (it == null) { errors.Add("Élément de catalogue vide (null)."); continue; }
                var id = string.IsNullOrWhiteSpace(it.Id) ? "(sans id)" : it.Id;
                if (string.IsNullOrWhiteSpace(it.Id)) errors.Add("Option sans identifiant (titre : " + (it.Titre ?? "?") + ").");
                else if (!seen.Add(it.Id)) errors.Add("Identifiant en double : " + it.Id + ".");
                if (!string.IsNullOrWhiteSpace(it.Id) && !System.Text.RegularExpressions.Regex.IsMatch(it.Id, @"^[a-z0-9][a-z0-9.\-]*$"))
                    errors.Add("Identifiant invalide : " + it.Id + " (caractères admis : a-z, 0-9, point, tiret).");
                if (string.IsNullOrWhiteSpace(it.Menu)) errors.Add(id + " : menu manquant.");
                if (string.IsNullOrWhiteSpace(it.Groupe)) errors.Add(id + " : groupe manquant.");
                if (string.IsNullOrWhiteSpace(it.Titre)) errors.Add(id + " : titre manquant.");
                if (it.Risque == null) errors.Add(id + " : risque manquant.");
                if (string.IsNullOrWhiteSpace(it.Detect)) errors.Add(id + " : script detect manquant.");
                if (string.IsNullOrWhiteSpace(it.Apply)) errors.Add(id + " : script apply manquant.");
                if (it.Reversible && string.IsNullOrWhiteSpace(it.Revert)) errors.Add(id + " : option réversible sans script revert.");
                if (!it.Reversible && !string.IsNullOrWhiteSpace(it.Revert)) errors.Add(id + " : option non réversible avec un script revert.");
                if (it.DelaiSec <= 0) errors.Add(id + " : delaiSec doit être supérieur à 0.");
            }
            return errors;
        }
    }
}
