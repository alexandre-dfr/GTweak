using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace GamingTweaker.Core
{
    public class QuarantineEntry
    {
        /// <summary>Nom du fichier dans le dossier de quarantaine (« guid_nom »).</summary>
        public string Id { get; set; }
        public string Nom { get; set; }
        /// <summary>Emplacement d'origine d'après les journaux ; null s'il n'est plus connu.</summary>
        public string Origine { get; set; }
        public long Taille { get; set; }
        public DateTime Date { get; set; }
    }

    /// <summary>Fichiers déplacés par Move-TweakQuarantine : liste, restauration, suppression définitive. Rien hors du dossier de quarantaine n'est jamais touché.</summary>
    public class QuarantineService
    {
        private readonly string racine;
        private readonly string sessionsDir;

        public QuarantineService(string quarantineDir, string sessionsDir)
        {
            racine = Path.GetFullPath(quarantineDir);
            this.sessionsDir = sessionsDir;
        }

        /// <summary>Chemin complet de l'élément s'il est un enfant direct de la racine ; sinon InvalidOperationException.</summary>
        private string Resoudre(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id == "." || id == "..")
                throw new InvalidOperationException("Identifiant de quarantaine invalide : « " + id + " ». Rien n'a été supprimé.");
            var chemin = Path.GetFullPath(Path.Combine(racine, id));
            var parent = Path.GetDirectoryName(chemin);
            if (!string.Equals(parent, racine.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Chemin hors de la quarantaine refusé : " + id);
            return chemin;
        }

        public List<QuarantineEntry> List()
        {
            var res = new List<QuarantineEntry>();
            if (!Directory.Exists(racine)) return res;
            var origines = Origines();
            foreach (var f in Directory.GetFiles(racine))
            {
                var fi = new FileInfo(f);
                string origine; origines.TryGetValue(fi.FullName, out origine);
                var nom = fi.Name; int u = nom.IndexOf('_');
                if (u == 32) nom = nom.Substring(33);
                res.Add(new QuarantineEntry { Id = fi.Name, Nom = nom, Origine = origine, Taille = fi.Length, Date = fi.LastWriteTime });
            }
            return res.OrderByDescending(e => e.Date).ToList();
        }

        public void Restore(string id)
        {
            var source = Resoudre(id);
            if (!File.Exists(source)) throw new InvalidOperationException("Fichier introuvable dans la quarantaine : " + id);
            string origine;
            if (!Origines().TryGetValue(source, out origine) || string.IsNullOrWhiteSpace(origine))
                throw new InvalidOperationException("L'emplacement d'origine n'est plus connu (journal absent) : restauration impossible pour " + id + ".");
            if (File.Exists(origine) || Directory.Exists(origine))
                throw new InvalidOperationException("Un fichier existe déjà à l'emplacement d'origine : " + origine);
            Directory.CreateDirectory(Path.GetDirectoryName(origine));
            File.Move(source, origine);
        }

        /// <summary>Suppression définitive d'un fichier de la quarantaine. Tout chemin qui en sort est refusé.</summary>
        public void Purge(string id)
        {
            var chemin = Resoudre(id);
            if (!File.Exists(chemin)) throw new InvalidOperationException("Fichier introuvable dans la quarantaine : " + id);
            File.Delete(chemin);
        }

        /// <summary>Chemin dans la quarantaine (« after ») vers l'emplacement d'origine (« before »), d'après tous les journaux.</summary>
        private Dictionary<string, string> Origines()
        {
            AssemblyLoader.Register();
            return OriginesCore();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private Dictionary<string, string> OriginesCore()
        {
            var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(sessionsDir) || !Directory.Exists(sessionsDir)) return res;
            foreach (var d in Directory.GetDirectories(sessionsDir).OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal))
            {
                var j = Path.Combine(d, "journal.jsonl");
                if (!File.Exists(j)) continue;
                try
                {
                    foreach (var e in Journal.Read(j).Where(x => x.Kind == "quarantine"))
                    {
                        var apres = e.After == null ? null : e.After.ToString();
                        var avant = e.Before == null ? null : e.Before.ToString();
                        if (string.IsNullOrEmpty(apres) || string.IsNullOrEmpty(avant)) continue;
                        try { res[Path.GetFullPath(apres)] = avant; } catch (Exception) { }
                    }
                }
                catch (InvalidDataException) { }
            }
            return res;
        }
    }
}
