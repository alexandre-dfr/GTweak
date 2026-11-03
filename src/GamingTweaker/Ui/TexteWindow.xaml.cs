using System.Windows;

namespace GamingTweaker.Ui
{
    public partial class TexteWindow : Window
    {
        public TexteWindow(string titre, string libelle, string valeur)
        {
            InitializeComponent();
            Title = titre; Libelle.Text = libelle; Saisie.Text = valeur;
            Loaded += (s, e) => { Saisie.Focus(); Saisie.SelectAll(); };
        }

        public string Valeur { get { return Saisie.Text; } }

        private void Ok_Click(object sender, RoutedEventArgs e) { DialogResult = true; }
    }
}
