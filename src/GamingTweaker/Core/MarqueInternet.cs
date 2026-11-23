using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace GamingTweaker.Core
{
    /// <summary>
    /// « Marque Internet » (flux NTFS Zone.Identifier) posée par le navigateur sur un fichier téléchargé : c'est elle qui fait afficher
    /// SmartScreen au lancement. L'utilisateur a déjà accepté de lancer l'exe ; la retirer équivaut au bouton « Débloquer » des
    /// propriétés du fichier et évite l'avertissement aux lancements suivants. Seul l'exe lui-même est concerné.
    /// </summary>
    public static class MarqueInternet
    {
        public const string Flux = ":Zone.Identifier";

        /// <summary>Vrai si le fichier porte la marque Internet.</summary>
        public static bool Presente(string fichier)
        {
            if (string.IsNullOrEmpty(fichier)) return false;
            return GetFileAttributes(fichier + Flux) != InvalidFileAttributes;
        }

        /// <summary>Retire la marque si elle existe. Vrai si elle a été retirée, faux si elle était absente ; exception si Windows refuse.</summary>
        public static bool Retirer(string fichier)
        {
            if (!Presente(fichier)) return false;
            if (!DeleteFile(fichier + Flux)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return true;
        }

        /// <summary>Retire la marque de l'exe en cours sans jamais lever d'exception ; renvoie un message pour le journal de démarrage, ou null.</summary>
        public static string RetirerDeLExe()
        {
            try
            {
                var exe = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return null;
                return Retirer(exe) ? "marque Internet retirée de l'exe (plus d'avertissement SmartScreen aux prochains lancements)" : null;
            }
            catch (Exception ex) { return "marque Internet non retirée : " + ex.Message; }
        }

        private const uint InvalidFileAttributes = 0xFFFFFFFF;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFileAttributes(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteFile(string lpFileName);
    }
}
