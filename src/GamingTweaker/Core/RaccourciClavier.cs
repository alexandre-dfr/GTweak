using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace GamingTweaker.Core
{
    /// <summary>
    /// Raccourci clavier global (actif même quand un jeu a le clavier), reçu par une fenêtre invisible réservée aux messages.
    /// <see cref="Enregistre"/> est faux si une autre application utilise déjà la combinaison.
    /// </summary>
    public sealed class RaccourciClavier : IDisposable
    {
        public const uint Ctrl = 0x2, Maj = 0x4, Alt = 0x1;
        private const uint SansRepetition = 0x4000;
        private const int WmHotkey = 0x0312;
        private const int Id = 0x4754;
        private readonly HwndSource fenetre;
        private readonly Action action;

        public bool Enregistre { get; private set; }

        public RaccourciClavier(uint modificateurs, uint touche, Action action)
        {
            this.action = action;
            fenetre = new HwndSource(new HwndSourceParameters("GamingTweaker.Raccourci") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */, Width = 0, Height = 0, WindowStyle = 0 });
            fenetre.AddHook(Recevoir);
            Enregistre = RegisterHotKey(fenetre.Handle, Id, modificateurs | SansRepetition, touche);
            if (!Enregistre) TraceAppli.Ecrire("raccourci clavier non enregistré (combinaison déjà utilisée par une autre application)");
        }

        private IntPtr Recevoir(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool traite)
        {
            if (msg == WmHotkey && wParam.ToInt32() == Id)
            {
                traite = true;
                try { action(); } catch (Exception ex) { TraceAppli.Erreur("raccourci clavier", ex); }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (Enregistre) UnregisterHotKey(fenetre.Handle, Id);
            fenetre.RemoveHook(Recevoir);
            fenetre.Dispose();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
