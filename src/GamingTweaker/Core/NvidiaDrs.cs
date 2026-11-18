using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace GamingTweaker.Core
{
    /// <summary>Réglages du profil global du pilote NVIDIA (remplaçable par un double dans les tests).</summary>
    public interface INvidiaReglages
    {
        /// <summary>Faux sans pilote NVIDIA (nvapi64.dll absent).</summary>
        bool Disponible { get; }
        /// <summary>{ défini dans le profil global (1/0), valeur effective (-1 si inconnue) }.</summary>
        long[] Lire(uint id);
        void Ecrire(uint id, uint valeur);
        /// <summary>Retire le réglage du profil global (valeur par défaut du pilote).</summary>
        void Retirer(uint id);
    }

    public enum ProfilNvidia { Aucun, Performance, Qualite }

    /// <summary>Profils NVIDIA du Mode Jeu : réglages du profil global appliqués à l'activation, remis à leur valeur d'avant au rétablissement.</summary>
    public static class ProfilsNvidia
    {
        public const uint Alimentation = 0x1057EB71, FiltrageTextures = 0x00CE2691, VSync = 0x00A879CF;
        public const uint LatenceEtat = 0x0005F543, LatenceUltra = 0x10835000, ImagesPreparees = 0x007BA09E;

        /// <summary>
        /// Performance : performances maximales, filtrage hautes performances, V-Sync désactivée, faible latence Ultra.
        /// Qualité : performances maximales, filtrage haute qualité, V-Sync laissée à chaque jeu.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<uint, uint>> Reglages(ProfilNvidia p)
        {
            var l = new List<KeyValuePair<uint, uint>>();
            if (p == ProfilNvidia.Performance)
            {
                l.Add(R(Alimentation, 1)); l.Add(R(FiltrageTextures, 0x14)); l.Add(R(VSync, 0x08416747));
                l.Add(R(LatenceEtat, 2)); l.Add(R(LatenceUltra, 1)); l.Add(R(ImagesPreparees, 1));
            }
            else if (p == ProfilNvidia.Qualite)
            {
                l.Add(R(Alimentation, 1)); l.Add(R(FiltrageTextures, 0xFFFFFFF6)); l.Add(R(VSync, 0x60925292));
            }
            return l;
        }

        private static KeyValuePair<uint, uint> R(uint id, uint v) { return new KeyValuePair<uint, uint>(id, v); }

        public static string Libelle(ProfilNvidia p)
        {
            return p == ProfilNvidia.Performance ? "Performance" : p == ProfilNvidia.Qualite ? "Qualité" : "Aucun (pilote inchangé)";
        }
    }

    /// <summary>
    /// Accès au profil global du pilote par NVAPI DRS (nvapi64.dll, installée avec le pilote). Même implémentation que GTNative.NvDrs
    /// (Common.ps1) : structure NVDRS_SETTING_V1 de 12320 octets manipulée par décalages.
    /// </summary>
    public sealed class NvidiaDrs : INvidiaReglages
    {
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr QueryInterface(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FInit();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FCreate(out IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FSession(IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FProfile(IntPtr session, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FGet(IntPtr session, IntPtr profile, uint id, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FSet(IntPtr session, IntPtr profile, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FDelete(IntPtr session, IntPtr profile, uint id);

        private const int NotFound = -160;
        private const int Taille = 12320;
        private const int Version = Taille | (1 << 16);

        public bool Disponible
        {
            get { return File.Exists(Path.Combine(Environment.SystemDirectory, "nvapi64.dll")); }
        }

        private static T F<T>(uint id) where T : class
        {
            IntPtr p = QueryInterface(id);
            if (p == IntPtr.Zero) throw new InvalidOperationException("Fonction NVAPI absente de ce pilote (0x" + id.ToString("X8") + ").");
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        private static void Ok(int s, string quoi)
        {
            if (s != 0) throw new InvalidOperationException("NVAPI " + quoi + " a échoué (code " + s + ").");
        }

        private static IntPtr Ouvrir(out IntPtr profil)
        {
            Ok(F<FInit>(0x0150E828)(), "Initialize");
            IntPtr h;
            Ok(F<FCreate>(0x0694D52E)(out h), "DRS_CreateSession");
            try
            {
                Ok(F<FSession>(0x375DBD6B)(h), "DRS_LoadSettings");
                Ok(F<FProfile>(0x617BFF9F)(h, out profil), "DRS_GetCurrentGlobalProfile");
            }
            catch { F<FSession>(0xDAD9CFF8)(h); throw; }
            return h;
        }

        private static void Fermer(IntPtr h) { F<FSession>(0xDAD9CFF8)(h); }

        private static IntPtr Tampon()
        {
            IntPtr b = Marshal.AllocHGlobal(Taille);
            Marshal.Copy(new byte[Taille], 0, b, Taille);
            Marshal.WriteInt32(b, 0, Version);
            return b;
        }

        public long[] Lire(uint id)
        {
            IntPtr profil; IntPtr h = Ouvrir(out profil); IntPtr b = Tampon();
            try
            {
                int s = F<FGet>(0x73BF8338)(h, profil, id, b);
                if (s == NotFound) return new long[] { 0, -1 };
                Ok(s, "DRS_GetSetting");
                if (Marshal.ReadInt32(b, 4104) != 0) throw new InvalidOperationException("Réglage NVIDIA 0x" + id.ToString("X8") + " : type inattendu.");
                return new long[] { Marshal.ReadInt32(b, 4108) == 0 ? 1 : 0, (long)(uint)Marshal.ReadInt32(b, 8220) };
            }
            finally { Marshal.FreeHGlobal(b); Fermer(h); }
        }

        public void Ecrire(uint id, uint valeur)
        {
            IntPtr profil; IntPtr h = Ouvrir(out profil); IntPtr b = Tampon();
            try
            {
                Marshal.WriteInt32(b, 4100, unchecked((int)id));
                Marshal.WriteInt32(b, 4104, 0);
                Marshal.WriteInt32(b, 8220, unchecked((int)valeur));
                Ok(F<FSet>(0x577DD202)(h, profil, b), "DRS_SetSetting");
                Ok(F<FSession>(0xFCBC7E14)(h), "DRS_SaveSettings");
            }
            finally { Marshal.FreeHGlobal(b); Fermer(h); }
        }

        public void Retirer(uint id)
        {
            IntPtr profil; IntPtr h = Ouvrir(out profil);
            try
            {
                int s = F<FDelete>(0xE4A26362)(h, profil, id);
                if (s == NotFound) return;
                Ok(s, "DRS_DeleteProfileSetting");
                Ok(F<FSession>(0xFCBC7E14)(h), "DRS_SaveSettings");
            }
            finally { Fermer(h); }
        }
    }
}
