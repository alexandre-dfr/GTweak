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

        public ZoneNotification(Action ouvrir, Action quitter, Func<bool> autoActif, Action<bool> definirAuto, Func<bool> modeJeuActif = null, Action retablir = null)
        {
            this.autoActif = autoActif;
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Ouvrir GamingTweaker", null, (s, e) => ouvrir());
            var retablirItem = new System.Windows.Forms.ToolStripMenuItem("Rétablir le Mode Jeu");
            retablirItem.Click += (s, e) => { if (retablir != null) retablir(); };
            menu.Items.Add(retablirItem);
            auto = new System.Windows.Forms.ToolStripMenuItem("Mode Jeu automatique") { CheckOnClick = false };
            auto.Click += (s, e) => definirAuto(!autoActif());
            menu.Items.Add(auto);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Quitter", null, (s, e) => quitter());
            menu.Opening += (s, e) =>
            {
                auto.Checked = autoActif();
                retablirItem.Visible = retablir != null && modeJeuActif != null && modeJeuActif();
            };

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

        private bool demande, temporaire;
        private System.Windows.Threading.DispatcherTimer finTemporaire;
        /// <summary>Icône demandée (mode automatique, démarrage avec Windows) ; une notification ponctuelle peut l'afficher quelques secondes en plus.</summary>
        public bool Visible { get { return demande; } set { demande = value; icone.Visible = demande || temporaire; } }

        /// <summary>Notification même quand l'icône est masquée : elle s'affiche le temps de la bulle (enregistrement d'une partie).</summary>
        public void NotifierToujours(string titre, string texte)
        {
            if (!icone.Visible)
            {
                temporaire = true; icone.Visible = true;
                if (finTemporaire == null)
                {
                    finTemporaire = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                    finTemporaire.Tick += (s, e) => { finTemporaire.Stop(); temporaire = false; icone.Visible = demande; };
                }
                finTemporaire.Stop(); finTemporaire.Start();
            }
            icone.ShowBalloonTip(5000, titre, texte, System.Windows.Forms.ToolTipIcon.Info);
        }

        /// <summary>Infobulle de l'icône (63 caractères au plus) : état du Mode Jeu.</summary>
        public void Etat(bool modeJeuActif, bool autoActif)
        {
            var t = "GamingTweaker" + (modeJeuActif ? " · Mode Jeu actif" : autoActif ? " · surveillance des jeux" : "");
            icone.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        }

        public void Notifier(string titre, string texte)
        {
            if (!icone.Visible) return;
            icone.ShowBalloonTip(5000, titre, texte, System.Windows.Forms.ToolTipIcon.Info);
        }

        public void Dispose()
        {
            if (finTemporaire != null) finTemporaire.Stop();
            icone.Visible = false;
            icone.Dispose();
        }
    }
}
