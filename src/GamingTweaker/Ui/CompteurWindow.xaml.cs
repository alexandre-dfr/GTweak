using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using GamingTweaker.Core;

namespace GamingTweaker.Ui
{
    /// <summary>
    /// Surimpression du compteur en jeu : dans un coin de l'écran principal, ou à la position libre choisie à la souris (n'importe quel écran).
    /// Hors placement, transparente aux clics et jamais activée : elle ne prend ni la souris ni le clavier au jeu.
    /// En placement (fenêtre des réglages ouverte), elle se déplace par glisser-déposer.
    /// Remise au premier plan toutes les 2 s (un jeu en plein écran sans bordure peut passer devant une fenêtre « toujours visible »).
    /// Les jeux en plein écran exclusif ne laissent aucune fenêtre s'afficher par-dessus.
    /// </summary>
    public partial class CompteurWindow : Window
    {
        private const double Marge = 12;
        private readonly CompteurViewModel vm;
        private readonly DispatcherTimer premierPlan = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        private bool deplacement;

        public CompteurWindow(CompteurViewModel vm)
        {
            this.vm = vm;
            InitializeComponent();
            DataContext = vm;
            SourceInitialized += (s, e) => StylesEtendus();
            SizeChanged += (s, e) => { if (!deplacement) Placer(); };
            premierPlan.Tick += (s, e) => RemettreAuPremierPlan();
            IsVisibleChanged += (s, e) => { if (IsVisible) premierPlan.Start(); else premierPlan.Stop(); };
            MouseLeftButtonDown += Glisser;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "ModePlacement") StylesEtendus(); };
        }

        /// <summary>Affiche, cache ou replace la surimpression selon les réglages.</summary>
        public void Appliquer()
        {
            if (vm.Visible) { if (!IsVisible) Show(); StylesEtendus(); Placer(); }
            else if (IsVisible) Hide();
        }

        private void Glisser(object sender, MouseButtonEventArgs e)
        {
            if (!vm.ModePlacement || e.ButtonState != MouseButtonState.Pressed) return;
            deplacement = true;
            try { DragMove(); }
            catch (InvalidOperationException) { /* bouton déjà relâché */ }
            finally { deplacement = false; }
            vm.DeplaceA(Left, Top);
            Placer();
        }

        private void Placer()
        {
            double l, t;
            if (vm.Position == CoinEcran.Libre)
            {
                // Position libre, ramenée dans le bureau (un écran débranché ne doit pas cacher le compteur).
                l = Borner(vm.X, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - ActualWidth);
                t = Borner(vm.Y, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - ActualHeight);
            }
            else
            {
                bool droite = vm.Position == CoinEcran.HautDroite || vm.Position == CoinEcran.BasDroite;
                bool bas = vm.Position == CoinEcran.BasGauche || vm.Position == CoinEcran.BasDroite;
                l = droite ? SystemParameters.PrimaryScreenWidth - ActualWidth - Marge : Marge;
                t = bas ? SystemParameters.PrimaryScreenHeight - ActualHeight - Marge : Marge;
            }
            if (Math.Abs(Left - l) > 0.5) Left = l;
            if (Math.Abs(Top - t) > 0.5) Top = t;
        }

        private static double Borner(double v, double min, double max) { return max < min ? min : Math.Max(min, Math.Min(max, v)); }

        /// <summary>Hors placement : transparente aux clics et jamais activée. En placement : cliquable pour être déplacée.</summary>
        private void StylesEtendus()
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            var ex = GetWindowLongPtr(h, GwlExStyle).ToInt64() | WsExLayered | WsExToolWindow;
            if (vm.ModePlacement) { ex &= ~(WsExTransparent | WsExNoActivate); Cursor = Cursors.SizeAll; }
            else { ex |= WsExTransparent | WsExNoActivate; Cursor = null; }
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
