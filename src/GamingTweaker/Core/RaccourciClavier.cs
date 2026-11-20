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
        private readonly int id;
        private readonly HwndSource fenetre;
        private readonly Action action;

        public bool Enregistre { get; private set; }

        /// <param name="id">Identifiant du raccourci, distinct pour chaque raccourci de l'application.</param>
        public RaccourciClavier(uint modificateurs, uint touche, Action action, int id = 0x4754)
        {
            this.action = action;
            this.id = id;
            fenetre = new HwndSource(new HwndSourceParameters("GamingTweaker.Raccourci") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */, Width = 0, Height = 0, WindowStyle = 0 });
            fenetre.AddHook(Recevoir);
            Enregistre = RegisterHotKey(fenetre.Handle, id, modificateurs | SansRepetition, touche);
            if (!Enregistre) TraceAppli.Ecrire("raccourci clavier non enregistré (combinaison déjà utilisée par une autre application)");
        }

        private IntPtr Recevoir(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool traite)
        {
            if (msg == WmHotkey && wParam.ToInt32() == id)
            {
                traite = true;
                try { action(); } catch (Exception ex) { TraceAppli.Erreur("raccourci clavier", ex); }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (Enregistre) UnregisterHotKey(fenetre.Handle, id);
            fenetre.RemoveHook(Recevoir);
            fenetre.Dispose();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
