using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using GamingTweaker.Core;

namespace GamingTweaker.Ui
{
    public partial class RunWindow : Window
    {
        private readonly MainViewModel vm;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private bool termine;
        private string sessionDir;

        public RunWindow(MainViewModel vm)
        {
            this.vm = vm;
            InitializeComponent();
            Loaded += async (s, e) => await DemarrerAsync();
            Closing += (s, e) =>
            {
                if (termine) return; // exécution finie : fermeture normale
                e.Cancel = true; // pas de fermeture en plein milieu : on demande l'arrêt proprement
                Annuler_Click(null, null);
            };
        }

        private async System.Threading.Tasks.Task DemarrerAsync()
        {
            var log = new Progress<string>(l =>
            {
                var txt = vm.FormaterLigne(l);
                if (txt == null) return;
                Journal.AppendText(txt + "\r\n"); Journal.ScrollToEnd();
            });
            SessionReport rapport;
            try { rapport = await vm.LancerAsync(log, cts.Token); }
            catch (Exception ex)
            {
                rapport = new SessionReport { ExitCode = 2 };
                rapport.Avertissements.Add("L'exécution s'est arrêtée sur une erreur : " + ex.Message);
            }
            termine = true;
            Barre.IsIndeterminate = false; Barre.Value = Barre.Maximum;
            Etat.Text = rapport.SessionDir == null ? "Rien n'a été exécuté"
                      : rapport.ExitCode == 0 ? "Terminé" : "Terminé avec des erreurs";
            // Les lignes du journal déjà postées sur ce thread passent avant ce code : le rapport vient après le journal.
            Journal.AppendText((Journal.Text.Length > 0 ? "\r\n---\r\n" : "") + MainViewModel.RapportTexte(rapport, vm.Catalogue));
            Journal.ScrollToEnd();
            Annuler.Visibility = Visibility.Collapsed;
            Fermer.Visibility = Visibility.Visible;
            if (rapport.SessionDir != null) { sessionDir = rapport.SessionDir; Dossier.Visibility = Visibility.Visible; }
        }

        private void Annuler_Click(object sender, RoutedEventArgs e)
        {
            if (termine) return;
            Etat.Text = "Arrêt en cours…"; Annuler.IsEnabled = false;
            cts.Cancel();
        }

        private void Fermer_Click(object sender, RoutedEventArgs e) { Close(); }

        private void Dossier_Click(object sender, RoutedEventArgs e)
        {
            try { if (sessionDir != null && Directory.Exists(sessionDir)) Core.SystemExe.OuvrirAvecExplorateur(sessionDir); }
            catch (Exception) { }
        }
    }
}
