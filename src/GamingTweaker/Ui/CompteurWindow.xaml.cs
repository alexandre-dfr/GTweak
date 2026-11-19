using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using GamingTweaker.Core;

namespace GamingTweaker.Ui
{
    /// <summary>
    /// Surimpression du compteur en jeu, dans un coin de l'écran principal. Transparente aux clics et jamais activée :
    /// elle ne prend ni la souris ni le clavier au jeu. Remise au premier plan toutes les 2 s (un jeu en plein écran sans bordure
    /// peut passer devant une fenêtre « toujours visible »). Les jeux en plein écran exclusif ne laissent aucune fenêtre s'afficher par-dessus.
    /// </summary>
    public partial class CompteurWindow : Window
    {
        private const double Marge = 12;
        private readonly CompteurViewModel vm;
        private readonly DispatcherTimer premierPlan = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };

        public CompteurWindow(CompteurViewModel vm)
        {
            this.vm = vm;
            InitializeComponent();
            DataContext = vm;
            SourceInitialized += (s, e) => StylesEtendus();
            SizeChanged += (s, e) => Placer();
            premierPlan.Tick += (s, e) => RemettreAuPremierPlan();
            IsVisibleChanged += (s, e) => { if (IsVisible) premierPlan.Start(); else premierPlan.Stop(); };
        }

        /// <summary>Affiche, cache ou replace la surimpression selon les réglages.</summary>
        public void Appliquer()
        {
            if (vm.Actif) { if (!IsVisible) Show(); Placer(); }
            else if (IsVisible) Hide();
        }

        private void Placer()
        {
            var largeur = SystemParameters.PrimaryScreenWidth;
            var hauteur = SystemParameters.PrimaryScreenHeight;
            bool droite = vm.Position == CoinEcran.HautDroite || vm.Position == CoinEcran.BasDroite;
            bool bas = vm.Position == CoinEcran.BasGauche || vm.Position == CoinEcran.BasDroite;
            Left = droite ? largeur - ActualWidth - Marge : Marge;
            Top = bas ? hauteur - ActualHeight - Marge : Marge;
        }

        private void StylesEtendus()
        {
            var h = new WindowInteropHelper(this).Handle;
            var ex = GetWindowLongPtr(h, GwlExStyle).ToInt64();
            ex |= WsExTransparent | WsExLayered | WsExToolWindow | WsExNoActivate;
            SetWindowLongPtr(h, GwlExStyle, new IntPtr(ex));
        }

        private void RemettreAuPremierPlan()
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero) SetWindowPos(h, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
        }

        private const int GwlExStyle = -20;
        private const long WsExTransparent = 0x20, WsExLayered = 0x80000, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10, SwpNoOwnerZOrder = 0x200;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    }
}
