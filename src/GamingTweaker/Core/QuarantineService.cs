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

    /// <summary>
    /// Fichiers déplacés par Move-TweakQuarantine : liste, restauration, suppression définitive. Rien hors du dossier de quarantaine n'est jamais touché.
    /// Après une restauration ou une suppression, l'entrée du journal est marquée annulée (marqueur ciblé) pour que l'historique et
    /// l'annulation par le journal restent cohérents.
    /// </summary>
    public class QuarantineService
    {
        private class Origine
        {
            public string Avant;
            public string Item;
            public string Journal;
        }

        private readonly string racine;
        private readonly List<string> sessionsDirs;

        /// <param name="sessionsDirs">Dossiers de sessions dont les journaux sont lus (racine unique sous %ProgramFiles%GamingTweaker).</param>
        public QuarantineService(string quarantineDir, params string[] sessionsDirs)
        {
            racine = Path.GetFullPath(quarantineDir);
            this.sessionsDirs = (sessionsDirs ?? new string[0]).Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
                Origine o; origines.TryGetValue(fi.FullName, out o);
                var nom = fi.Name; int u = nom.IndexOf('_');
                if (u == 32) nom = nom.Substring(33);
                res.Add(new QuarantineEntry { Id = fi.Name, Nom = nom, Origine = o == null ? null : o.Avant, Taille = fi.Length, Date = fi.LastWriteTime });
            }
            return res.OrderByDescending(e => e.Date).ToList();
        }

        public void Restore(string id)
        {
            var source = Resoudre(id);
            if (!File.Exists(source)) throw new InvalidOperationException("Fichier introuvable dans la quarantaine : " + id);
            Origine o;
            if (!Origines().TryGetValue(source, out o) || string.IsNullOrWhiteSpace(o.Avant))
                throw new InvalidOperationException("L'emplacement d'origine n'est plus connu (journal absent) : restauration impossible pour " + id + ".");
            if (File.Exists(o.Avant) || Directory.Exists(o.Avant))
                throw new InvalidOperationException("Un fichier existe déjà à l'emplacement d'origine : " + o.Avant);
            Directory.CreateDirectory(Path.GetDirectoryName(o.Avant));
            File.Move(source, o.Avant);
            Marquer(o);
        }

        /// <summary>Suppression définitive d'un fichier de la quarantaine. Tout chemin qui en sort est refusé.</summary>
        public void Purge(string id)
        {
            var chemin = Resoudre(id);
            if (!File.Exists(chemin)) throw new InvalidOperationException("Fichier introuvable dans la quarantaine : " + id);
            Origine o; Origines().TryGetValue(chemin, out o);
            File.Delete(chemin);
            if (o != null) Marquer(o);
        }

        /// <summary>Le fichier a quitté la quarantaine : son entrée n'est plus à annuler (les autres fichiers de l'option le restent).</summary>
        private static void Marquer(Origine o)
        {
            try { Journal.MarquerAnnule(o.Journal, o.Item, o.Avant); }
            catch (Exception ex) { throw new IOException("Le fichier a été traité, mais le journal n'a pas pu être mis à jour (" + o.Journal + ") : " + ex.Message, ex); }
        }

        /// <summary>Chemin dans la quarantaine (« after ») vers l'emplacement d'origine (« before »), l'option et le journal, d'après tous les journaux.</summary>
        private Dictionary<string, Origine> Origines()
        {
            AssemblyLoader.Register();
            return OriginesCore();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private Dictionary<string, Origine> OriginesCore()
        {
            var res = new Dictionary<string, Origine>(StringComparer.OrdinalIgnoreCase);
            var journaux = new List<string>();
            foreach (var dir in sessionsDirs)
            {
                if (!Directory.Exists(dir)) continue;
                journaux.AddRange(Directory.GetDirectories(dir).Select(d => Path.Combine(d, "journal.jsonl")));
            }
            // Ordre chronologique (noms de session datés), toutes racines confondues : la dernière entrée l'emporte.
            foreach (var j in journaux.OrderBy(x => Path.GetFileName(Path.GetDirectoryName(x)), StringComparer.Ordinal))
            {
                if (!File.Exists(j)) continue;
                try
                {
                    foreach (var e in Journal.Read(j).Where(x => x.Kind == "quarantine"))
                    {
                        var apres = e.After == null ? null : e.After.ToString();
                        var avant = e.Before == null ? null : e.Before.ToString();
                        if (string.IsNullOrEmpty(apres) || string.IsNullOrEmpty(avant)) continue;
                        try { res[Path.GetFullPath(apres)] = new Origine { Avant = avant, Item = e.Item, Journal = j }; } catch (Exception) { }
                    }
                }
                catch (InvalidDataException) { }
            }
            return res;
        }
    }
}
