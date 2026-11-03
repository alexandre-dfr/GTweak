using System.Windows;
using GamingTweaker.Core;

namespace GamingTweaker
{
    public partial class App : Application
    {
        public App()
        {
            // Doit être appelé avant toute résolution d'assembly embarqué.
            AssemblyLoader.Register();
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
            new MainWindow().Show();
        }
    }
}
