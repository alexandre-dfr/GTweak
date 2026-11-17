using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Copie de l'exe en cours d'exécution (routine planifiée, lancée ensuite élevée chaque semaine).
    /// L'exe portable est souvent dans un dossier où l'utilisateur peut écrire (Téléchargements) : un exe en cours d'exécution ne peut pas
    /// être modifié, mais il peut être renommé puis remplacé par un autre fichier du même nom. Relire le fichier par son chemin copierait
    /// alors ce remplaçant dans %ProgramFiles%\GamingTweaker\App, d'où il serait exécuté avec les droits administrateur.
    /// Le fichier est donc ouvert sans partage en écriture ni en suppression (plus aucun renommage possible), puis comparé à l'image
    /// réellement chargée en mémoire (nom de l'image mappée et chemin réel de la poignée) ; la copie est faite depuis cette poignée.
    /// </summary>
    public static class ExeEnCours
    {
        public const string MessageRemplace =
            "L'exe en cours d'exécution a été déplacé ou remplacé depuis son lancement : la routine n'a pas été planifiée. Relancez GamingTweaker.";

        [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetMappedFileNameW(IntPtr process, IntPtr address, StringBuilder name, uint size);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle h, StringBuilder buf, uint len, uint flags);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        private const uint VolumeNameNt = 0x2, FileNameOpened = 0x8;

        /// <summary>Décision pure : l'image chargée (chemin NT) est l'un des chemins NT de la poignée ouverte.</summary>
        public static bool MemeFichier(string imageNt, IEnumerable<string> poigneeNt)
        {
            if (string.IsNullOrEmpty(imageNt) || poigneeNt == null) return false;
            return poigneeNt.Any(p => !string.IsNullOrEmpty(p) && string.Equals(p, imageNt, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Copie l'exe en cours vers <paramref name="destination"/> (remplacée), ou InvalidOperationException s'il n'est plus le même fichier.</summary>
        public static void CopierVers(string destination)
        {
            string chemin; IntPtr baseImage;
            using (var p = Process.GetCurrentProcess())
            {
                chemin = p.MainModule.FileName;
                baseImage = p.MainModule.BaseAddress;
            }
            // Partage lecture seule : ni écriture, ni renommage, ni suppression tant que la poignée est ouverte.
            using (var source = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var image = NomImage(baseImage);
                var noms = new[] { NomPoignee(source.SafeFileHandle, VolumeNameNt), NomPoignee(source.SafeFileHandle, VolumeNameNt | FileNameOpened) };
                if (!MemeFichier(image, noms)) throw new InvalidOperationException(MessageRemplace);
                using (var cible = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    source.CopyTo(cible);
                    cible.Flush(true);
                }
            }
        }

        private static string NomImage(IntPtr adresse)
        {
            var sb = new StringBuilder(1024);
            uint n = GetMappedFileNameW(GetCurrentProcess(), adresse, sb, (uint)sb.Capacity);
            return n == 0 ? null : sb.ToString(0, (int)Math.Min(n, (uint)sb.Length));
        }

        private static string NomPoignee(SafeFileHandle h, uint flags)
        {
            var sb = new StringBuilder(1024);
            uint n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, flags);
            if (n == 0) return null;
            if (n >= sb.Capacity)
            {
                sb = new StringBuilder((int)n + 1);
                n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, flags);
                if (n == 0 || n >= sb.Capacity) return null;
            }
            return sb.ToString();
        }
    }
}
