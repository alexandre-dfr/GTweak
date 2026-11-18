using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GamingTweaker.Core
{
    public enum NiveauDiag { Attention, Info, Ok }

    /// <summary>Un point du diagnostic. Action : « ms-settings:… » (page des Paramètres), « option:&lt;id&gt; » (cocher l'option) ou « menu:&lt;nom&gt; ».</summary>
    public class PointDiag
    {
        public NiveauDiag Niveau { get; set; }
        public string Titre { get; set; }
        public string Detail { get; set; }
        public string ActionTexte { get; set; }
        public string Action { get; set; }
    }

    public class EcranDiag { public string Nom; public int Largeur, Hauteur, HzActuel, HzMax; }
    public class BarretteDiag { public int Vitesse, Configuree; public string Reference; }

    /// <summary>Mesures brutes (null : non mesurable). Remplies par Mesurer, données à la main par les tests.</summary>
    public class MesuresDiag
    {
        public List<EcranDiag> Ecrans { get; } = new List<EcranDiag>();
        public List<BarretteDiag> Barrettes { get; } = new List<BarretteDiag>();
        public bool? ModeJeuWindows;
        public int? Hags;
        public string Plan;
        public long? LibreSysteme, TotalSysteme;
        public DateTime? DatePiloteGpu;
        public string NomGpu;
        public bool? SurBatterie;
        public bool EstPortable;
        public bool? CaptureArrierePlan;
    }

    /// <summary>
    /// Diagnostic jeu : repère ce qui bride réellement les performances (écran sous sa fréquence maximale, mémoire sous sa vitesse
    /// nominale ou en simple canal, Mode Jeu de Windows coupé, disque système plein, pilote graphique ancien, batterie, capture en
    /// arrière-plan). Lecture seule : rien n'est modifié ; chaque point propose la page des Paramètres ou l'option qui le corrige.
    /// </summary>
    public static class DiagnosticJeu
    {
        public const string PlanEquilibre = "381b4222-f694-41f0-9685-ff5bb260df2e";

        /// <summary>Vitesse nominale lue dans la référence du module (F4-3200C16, CMK16GX4M2B3200C16, KF432C16 → 3200), ou 0.</summary>
        public static int VitesseReference(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return 0;
            var r = reference.ToUpperInvariant();
            var m = Regex.Match(r, @"(?<![0-9])(2133|2400|2666|2933|3000|3200|3466|3600|3733|3866|4000|4133|4266|4400|4600|4800|5200|5600|6000|6200|6400|6600|6800|7200|7600|8000)(?![0-9])");
            if (m.Success) return int.Parse(m.Value, CultureInfo.InvariantCulture);
            var k = Regex.Match(r, @"^KF[0-9]?(?<v>2[4-9]|3[0-9]|4[0-9]|5[0-9]|6[0-9])C");   // Kingston Fury : KF432C16 = 3200, KF560C40 = 6000
            if (k.Success) { var v = int.Parse(k.Groups["v"].Value, CultureInfo.InvariantCulture); return v * 100; }
            return 0;
        }

        public static List<PointDiag> Analyser(MesuresDiag m, DateTime maintenant)
        {
            var l = new List<PointDiag>();

            // 1. Écrans
            foreach (var e in m.Ecrans)
            {
                if (e.HzMax > e.HzActuel + 1)
                    l.Add(new PointDiag { Niveau = NiveauDiag.Attention, Titre = "Écran à " + e.HzActuel + " Hz au lieu de " + e.HzMax + " Hz",
                        Detail = e.Nom + " (" + e.Largeur + "x" + e.Hauteur + ") accepte " + e.HzMax + " Hz mais Windows l'utilise à " + e.HzActuel + " Hz : les images au-delà ne sont jamais affichées. Choisissez la fréquence maximale dans « Affichage avancé ».",
                        ActionTexte = "Ouvrir l'affichage avancé", Action = "ms-settings:display-advanced" });
                else if (e.HzActuel > 0)
                    l.Add(new PointDiag { Niveau = NiveauDiag.Ok, Titre = "Écran à sa fréquence maximale (" + e.HzActuel + " Hz)", Detail = e.Nom + " (" + e.Largeur + "x" + e.Hauteur + ")." });
            }

            // 2. Mémoire
            if (m.Barrettes.Count > 0)
            {
                var conf = m.Barrettes.Where(b => b.Configuree > 0).Select(b => b.Configuree).DefaultIfEmpty(0).Min();
                var nominale = m.Barrettes.Select(b => Math.Max(b.Vitesse, VitesseReference(b.Reference))).DefaultIfEmpty(0).Min();
                if (conf > 0 && nominale > conf * 1.08)
                    l.Add(new PointDiag { Niveau = NiveauDiag.Attention, Titre = "Mémoire à " + conf + " MT/s au lieu de " + nominale,
                        Detail = "Vos modules sont prévus pour " + nominale + " MT/s mais tournent à " + conf + " MT/s : le profil XMP (Intel) ou EXPO (AMD) est probablement désactivé. Il s'active dans le BIOS de la carte mère ; gain sensible dans les jeux limités par le processeur." });
                else if (conf > 0)
                    l.Add(new PointDiag { Niveau = NiveauDiag.Ok, Titre = "Mémoire à sa vitesse (" + conf + " MT/s)", Detail = m.Barrettes.Count + " module(s)." });
                if (m.Barrettes.Count == 1)
                    l.Add(new PointDiag { Niveau = m.EstPortable ? NiveauDiag.Info : NiveauDiag.Attention, Titre = "Un seul module de mémoire",
                        Detail = m.EstPortable ? "Sur un portable, une partie de la mémoire peut être soudée : vérifiez la fiche du fabricant. Un seul module signifie souvent un fonctionnement en simple canal, plus lent dans les jeux."
                                               : "Un seul module fonctionne en simple canal : la bande passante mémoire est divisée par deux, ce qui coûte des images par seconde dans beaucoup de jeux. Deux modules identiques (double canal) sont recommandés." });
            }

            // 3. Mode Jeu de Windows
            if (m.ModeJeuWindows == false)
                l.Add(new PointDiag { Niveau = NiveauDiag.Attention, Titre = "Mode Jeu de Windows désactivé",
                    Detail = "Le Mode Jeu de Windows donne la priorité au jeu au premier plan et suspend les mises à jour pendant la partie.", ActionTexte = "Cocher l'option", Action = "option:opt.gamemode-on" });
            else if (m.ModeJeuWindows == true)
                l.Add(new PointDiag { Niveau = NiveauDiag.Ok, Titre = "Mode Jeu de Windows activé", Detail = "" });

            // 4. Planification GPU accélérée par le matériel
            if (m.Hags == 1)
                l.Add(new PointDiag { Niveau = NiveauDiag.Info, Titre = "Planification GPU accélérée désactivée",
                    Detail = "Peut réduire la latence et libérer du processeur selon le jeu (nécessaire pour la génération d'images DLSS 3). Redémarrage nécessaire.", ActionTexte = "Cocher l'option", Action = "option:opt.hags" });

            // 5. Alimentation
            if (m.SurBatterie == true)
                l.Add(new PointDiag { Niveau = NiveauDiag.Attention, Titre = "Sur batterie",
                    Detail = "Sur batterie, Windows et la carte graphique réduisent fortement les performances. Branchez le chargeur pour jouer.", ActionTexte = "Options d'alimentation", Action = "ms-settings:powersleep" });
            if (!m.EstPortable && string.Equals(m.Plan, PlanEquilibre, StringComparison.OrdinalIgnoreCase))
                l.Add(new PointDiag { Niveau = NiveauDiag.Info, Titre = "Plan d'alimentation Équilibré",
                    Detail = "Sur un PC fixe, le plan Performances élevées garde le processeur à haute fréquence et évite des baisses ponctuelles d'images par seconde.", ActionTexte = "Cocher l'option", Action = "option:opt.plan-high" });

            // 6. Disque système
            if (m.LibreSysteme.HasValue && m.TotalSysteme.HasValue && m.TotalSysteme.Value > 0)
            {
                var go = m.LibreSysteme.Value / 1073741824.0;
                var part = (double)m.LibreSysteme.Value / m.TotalSysteme.Value;
                if (go < 20 || part < 0.10)
                    l.Add(new PointDiag { Niveau = NiveauDiag.Attention, Titre = "Disque système presque plein (" + go.ToString("0", CultureInfo.GetCultureInfo("fr-FR")) + " Go libres)",
                        Detail = "Windows, le cache des shaders et les mises à jour des jeux ont besoin de place : en dessous de 10 % libres, les chargements et les mises à jour ralentissent.", ActionTexte = "Ouvrir le Nettoyage", Action = "menu:Nettoyage" });
                else
                    l.Add(new PointDiag { Niveau = NiveauDiag.Ok, Titre = "Espace disque suffisant (" + go.ToString("0", CultureInfo.GetCultureInfo("fr-FR")) + " Go libres)", Detail = "" });
            }

            // 7. Pilote graphique
            if (m.DatePiloteGpu.HasValue)
            {
                var jours = (maintenant - m.DatePiloteGpu.Value).TotalDays;
                var date = m.DatePiloteGpu.Value.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR"));
                if (jours > 180)
                    l.Add(new PointDiag { Niveau = NiveauDiag.Attention, Titre = "Pilote graphique ancien (" + date + ")",
                        Detail = (string.IsNullOrEmpty(m.NomGpu) ? "" : m.NomGpu + " : ") + "les pilotes récents apportent des optimisations pour les jeux sortis depuis et des corrections de stabilité.", ActionTexte = "Ouvrir les Mises à jour", Action = "menu:Mises à jour" });
                else
                    l.Add(new PointDiag { Niveau = NiveauDiag.Ok, Titre = "Pilote graphique récent (" + date + ")", Detail = m.NomGpu ?? "" });
            }

            // 8. Capture en arrière-plan
            if (m.CaptureArrierePlan == true)
                l.Add(new PointDiag { Niveau = NiveauDiag.Info, Titre = "Enregistrement en arrière-plan de la Xbox Game Bar actif",
                    Detail = "L'enregistrement continu des dernières minutes de jeu occupe le GPU et le disque en permanence.", ActionTexte = "Cocher l'option", Action = "option:opt.gamedvr-off" });

            return l.OrderBy(p => p.Niveau).ToList();
        }

        // ---------------- Mesures (lecture seule) ----------------

        public static MesuresDiag Mesurer(MachineFacts facts = null)
        {
            var m = new MesuresDiag { EstPortable = facts != null && facts.EstPortable };
            Essayer(() => MesurerEcrans(m));
            Essayer(() =>
            {
                using (var s = new ManagementObjectSearcher("SELECT Speed,ConfiguredClockSpeed,PartNumber FROM Win32_PhysicalMemory"))
                using (var col = s.Get())
                    foreach (ManagementBaseObject o in col)
                    {
                        m.Barrettes.Add(new BarretteDiag { Vitesse = Entier(o["Speed"]), Configuree = Entier(o["ConfiguredClockSpeed"]), Reference = Convert.ToString(o["PartNumber"]) });
                        o.Dispose();
                    }
            });
            Essayer(() =>
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar"))
                {
                    var v = k == null ? null : k.GetValue("AutoGameModeEnabled");
                    m.ModeJeuWindows = v == null ? true : Entier(v) != 0;   // absent : activé par défaut
                }
            });
            Essayer(() =>
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
                {
                    var v = k == null ? null : k.GetValue("HwSchMode");
                    if (v != null) m.Hags = Entier(v);
                }
            });
            Essayer(() =>
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR"))
                {
                    var v = k == null ? null : k.GetValue("HistoricalCaptureEnabled");
                    if (v != null) m.CaptureArrierePlan = Entier(v) == 1;
                }
            });
            Essayer(() =>
            {
                SYSTEM_POWER_STATUS ps;
                if (GetSystemPowerStatus(out ps) && ps.BatteryFlag != 128 && ps.ACLineStatus != 255) m.SurBatterie = ps.ACLineStatus == 0;
            });
            Essayer(() =>
            {
                IntPtr g;
                if (PowerGetActiveScheme(IntPtr.Zero, out g) == 0)
                {
                    try { m.Plan = ((Guid)Marshal.PtrToStructure(g, typeof(Guid))).ToString(); } finally { LocalFree(g); }
                }
            });
            Essayer(() =>
            {
                var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                m.LibreSysteme = d.AvailableFreeSpace; m.TotalSysteme = d.TotalSize;
            });
            Essayer(() =>
            {
                using (var s = new ManagementObjectSearcher("SELECT Name,DriverDate FROM Win32_VideoController"))
                using (var col = s.Get())
                    foreach (ManagementBaseObject o in col)
                    {
                        var nom = Convert.ToString(o["Name"]) ?? "";
                        var date = Convert.ToString(o["DriverDate"]);
                        o.Dispose();
                        if (nom.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0 || string.IsNullOrEmpty(date)) continue;
                        // Carte dédiée de préférence (NVIDIA/AMD Radeon) plutôt que l'iGPU.
                        if (m.NomGpu == null || nom.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 || nom.IndexOf("Radeon RX", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            m.NomGpu = nom;
                            try { m.DatePiloteGpu = ManagementDateTimeConverter.ToDateTime(date); } catch (Exception) { }
                        }
                    }
            });
            return m;
        }

        private static void Essayer(Action a)
        {
            try { a(); } catch (Exception ex) { TraceAppli.Erreur("diagnostic jeu : mesure impossible", ex); }
        }

        private static int Entier(object o)
        {
            try { return o == null ? 0 : Convert.ToInt32(o, CultureInfo.InvariantCulture); } catch (Exception) { return 0; }
        }

        private static void MesurerEcrans(MesuresDiag m)
        {
            for (int i = 0; i < 16; i++)
            {
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
                if ((dd.StateFlags & 1) == 0) continue;   // ATTACHED_TO_DESKTOP
                var cur = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                if (!EnumDisplaySettings(dd.DeviceName, -1, ref cur)) continue;
                int max = cur.dmDisplayFrequency;
                for (int n = 0; n < 2000; n++)
                {
                    var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                    if (!EnumDisplaySettings(dd.DeviceName, n, ref dm)) break;
                    // Même définition que l'actuelle : seule la fréquence compte.
                    if (dm.dmPelsWidth == cur.dmPelsWidth && dm.dmPelsHeight == cur.dmPelsHeight && dm.dmDisplayFrequency > max) max = dm.dmDisplayFrequency;
                }
                m.Ecrans.Add(new EcranDiag { Nom = string.IsNullOrWhiteSpace(dd.DeviceString) ? dd.DeviceName : dd.DeviceString.Trim(), Largeur = cur.dmPelsWidth, Hauteur = cur.dmPelsHeight, HzActuel = cur.dmDisplayFrequency, HzMax = max });
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }

        [DllImport("user32.dll", CharSet = CharSet.Ansi)] private static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Ansi)] private static extern bool EnumDisplayDevices(string device, int index, ref DISPLAY_DEVICE dd, int flags);
        [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);
        [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr rootKey, out IntPtr guid);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr p);
    }
}
