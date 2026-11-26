using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace GamingTweaker.Ui
{
    /// <summary>Assistant après installation de Windows : une question par écran ; « Voir l'aperçu » ferme la fenêtre et ouvre l'aperçu habituel.</summary>
    public partial class AssistantWindow : Window
    {
        private readonly AssistantViewModel vm;

        public AssistantWindow(MainViewModel main)
        {
            InitializeComponent();
            vm = new AssistantViewModel(main);
            DataContext = vm;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == "Courante") Defilement.ScrollToTop(); };
            Closed += (s, e) => vm.Terminer();
        }

        private void Etape_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListeEtapes.SelectedIndex >= 0 && ListeEtapes.SelectedIndex != vm.Index) vm.Aller(ListeEtapes.SelectedIndex);
        }

        private void Apercu_Click(object sender, RoutedEventArgs e) { DialogResult = true; }
    }

    public class InverseBoolVersVisibilite : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool && (bool)value ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }
}
