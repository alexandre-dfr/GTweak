using System;
using System.Diagnostics;

namespace GamingTweaker.Ui
{
    /// <summary>
    /// Icône dans la zone de notification (près de l'horloge) : ouvrir la fenêtre, activer ou désactiver le Mode Jeu automatique, quitter.
    /// Les notifications du Mode Jeu automatique passent par ses bulles (affichées en notifications par Windows 10 et 11).
    /// </summary>
    public sealed class ZoneNotification : IDisposable
    {
        private readonly System.Windows.Forms.NotifyIcon icone;
        private readonly System.Windows.Forms.ToolStripMenuItem auto;
        private readonly Func<bool> autoActif;

        public ZoneNotification(Action ouvrir, Action quitter, Func<bool> autoActif, Action<bool> definirAuto)
        {
            this.autoActif = autoActif;
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Ouvrir GamingTweaker", null, (s, e) => ouvrir());
            auto = new System.Windows.Forms.ToolStripMenuItem("Mode Jeu automatique") { CheckOnClick = false };
            auto.Click += (s, e) => definirAuto(!autoActif());
            menu.Items.Add(auto);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Quitter", null, (s, e) => quitter());
            menu.Opening += (s, e) => auto.Checked = autoActif();

            icone = new System.Windows.Forms.NotifyIcon { Text = "GamingTweaker", ContextMenuStrip = menu, Icon = IconeApplication() };
            icone.DoubleClick += (s, e) => ouvrir();
        }

        private static System.Drawing.Icon IconeApplication()
        {
            try
            {
                string exe;
                using (var p = Process.GetCurrentProcess()) exe = p.MainModule.FileName;
                return System.Drawing.Icon.ExtractAssociatedIcon(exe) ?? System.Drawing.SystemIcons.Application;
            }
            catch (Exception) { return System.Drawing.SystemIcons.Application; }
        }

        public bool Visible { get { return icone.Visible; } set { icone.Visible = value; } }

        public void Notifier(string titre, string texte)
        {
            if (!icone.Visible) return;
            icone.ShowBalloonTip(5000, titre, texte, System.Windows.Forms.ToolTipIcon.Info);
        }

        public void Dispose()
        {
            icone.Visible = false;
            icone.Dispose();
        }
    }
}
