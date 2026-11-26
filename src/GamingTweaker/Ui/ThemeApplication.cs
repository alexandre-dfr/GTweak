using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace GamingTweaker.Ui
{
    public enum ChoixTheme { Systeme, Clair, Sombre }

    /// <summary>
    /// Thème clair ou sombre façon Windows 11. Par défaut, suit le réglage de Windows (« Mode d'application ») et change avec lui ;
    /// l'utilisateur peut forcer Clair ou Sombre (enregistré dans HKCU\Software\GamingTweaker). Le dictionnaire de couleurs est
    /// remplacé à chaud (les vues lisent les couleurs en DynamicResource) et la barre de titre suit (DWMWA_USE_IMMERSIVE_DARK_MODE).
    /// </summary>
    public static class ThemeApplication
    {
        private const string Cle = @"Software\GamingTweaker";
        private static ChoixTheme choix = ChoixTheme.Systeme;
        private static bool abonne;

        public static ChoixTheme Choix { get { return choix; } }
        public static bool Sombre { get; private set; }
        public static event Action Change;

        /// <summary>Décision pure : sombre si forcé, ou si Windows est en mode sombre (AppsUseLightTheme = 0).</summary>
        public static bool EstSombre(ChoixTheme c, int? appsUseLightTheme)
        {
            if (c == ChoixTheme.Sombre) return true;
            if (c == ChoixTheme.Clair) return false;
            return appsUseLightTheme.HasValue && appsUseLightTheme.Value == 0;
        }

        public static ChoixTheme LireChoix()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Cle))
                {
                    var v = k == null ? null : k.GetValue("Theme") as string;
                    ChoixTheme c;
                    if (v != null && Enum.TryParse(v, out c)) return c;
                }
            }
            catch (Exception exc) { Core.TraceAppli.Erreur("thème : lecture du choix", exc); }
            return ChoixTheme.Systeme;
        }

        private static int? ThemeWindows()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int ? (int?)(int)v : null;
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>Applique le choix enregistré (au démarrage, avant la création des fenêtres).</summary>
        public static void Initialiser()
        {
            choix = LireChoix();
            Appliquer();
            if (!abonne)
            {
                abonne = true;
                // Toute fenêtre (principale, aperçu, exécution, dialogues) prend la barre de titre du thème à son chargement.
                EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) => BarreDeTitre((Window)s)));
                SystemEvents.UserPreferenceChanged += (s, e) =>
                {
                    if (e.Category != UserPreferenceCategory.General || choix != ChoixTheme.Systeme) return;
                    var app = Application.Current;
                    if (app != null) app.Dispatcher.BeginInvoke(new Action(Appliquer));
                };
            }
        }

        public static void Definir(ChoixTheme c)
        {
            choix = c;
            try { using (var k = Registry.CurrentUser.CreateSubKey(Cle)) k.SetValue("Theme", c.ToString()); } catch (Exception exc) { Core.TraceAppli.Erreur("thème : enregistrement du choix", exc); }
            Appliquer();
        }

        private static void Appliquer()
        {
            var app = Application.Current;
            if (app == null) return;
            Sombre = EstSombre(choix, ThemeWindows());
            var dico = Couleurs(Sombre);
            var fusion = app.Resources.MergedDictionaries;
            if (fusion.Count > 0) fusion[0] = dico; else fusion.Insert(0, dico);
            foreach (Window w in app.Windows) BarreDeTitre(w);
            var h = Change;
            if (h != null) h();
        }

        /// <summary>Dictionnaire de couleurs du thème (URI pack : utilisable aussi hors de l'application, par les tests).</summary>
        public static ResourceDictionary Couleurs(bool sombre)
        {
            return new ResourceDictionary { Source = new Uri("/GamingTweaker;component/Ui/Couleurs." + (sombre ? "Sombre" : "Clair") + ".xaml", UriKind.Relative) };
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Barre de titre sombre ou claire (Windows 10 2004+ / 11) ; sans effet ailleurs.</summary>
        public static void BarreDeTitre(Window w)
        {
            try
            {
                var h = new WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) { w.SourceInitialized -= SurInit; w.SourceInitialized += SurInit; return; }
                int v = Sombre ? 1 : 0;
                if (DwmSetWindowAttribute(h, 20, ref v, sizeof(int)) != 0) DwmSetWindowAttribute(h, 19, ref v, sizeof(int));
            }
            catch (Exception) { }
        }

        private static void SurInit(object sender, EventArgs e) { var w = sender as Window; if (w != null) BarreDeTitre(w); }
    }
}
