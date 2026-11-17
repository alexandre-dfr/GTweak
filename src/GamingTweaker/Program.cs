using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using GamingTweaker.Core;
using Microsoft.Win32.SafeHandles;

namespace GamingTweaker
{
    public static class Program
    {
        [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int n);
        [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int n, IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

        [STAThread]
        public static int Main(string[] args)
        {
            // Tout en haut, avant tout type Newtonsoft/LHM : une DLL du même nom qu'une DLL embarquée à côté de l'exe serait chargée à sa place.
            var refus = VerifierDllsAuDemarrage(args);
            if (refus != 0) return refus;
            DemarrageSur.RestreindreRechercheDll();
            AssemblyLoader.Register();
            if (args.Length == 0) return LaunchUi();
            return RunCommandLine(args);
        }

        private static int VerifierDllsAuDemarrage(string[] args)
        {
            string dossier;
            try { dossier = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); } catch (Exception) { dossier = AppDomain.CurrentDomain.BaseDirectory; }
            var conflits = DemarrageSur.DllsEnConflit(dossier, DemarrageSur.DllsEmbarquees());
            if (conflits.Count == 0) return 0;
            var msg = DemarrageSur.MessageDllEnConflit(conflits, dossier);
            if (args.Length == 0)
            {
                try { System.Windows.MessageBox.Show(msg, "GamingTweaker", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error); } catch (Exception) { }
            }
            else { AttachParentConsole(); Console.Error.WriteLine(msg); }
            return 2;
        }

        private static int LaunchUi()
        {
            var app = new App();
            app.InitializeComponent();
            return app.Run();
        }

        private const string Usage =
            "Utilisation : GamingTweaker.exe                      lance l'interface\r\n" +
            "              GamingTweaker.exe --profile <fichier> --silent   applique un profil sans interface\r\n" +
            "              GamingTweaker.exe --uninstall [--confirm]          retire la routine et le dossier de données (aperçu sans --confirm)\r\n" +
            "Codes de sortie : 0 = tout est appliqué ou ignoré, 1 = au moins un échec, 2 = exécution impossible.";

        private static int RunCommandLine(string[] args)
        {
            AttachParentConsole();
            if (string.Equals(args[0], "--cycle-test", StringComparison.OrdinalIgnoreCase))
                return args.Length == 2 ? CycleTest.Run(args[1]) : 2;
            if (string.Equals(args[0], "--uninstall", StringComparison.OrdinalIgnoreCase))
                return RunUninstall(args);
            string profile = null; bool silent = false;
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase)) silent = true;
                else if (string.Equals(a, "--profile", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && !args[i + 1].StartsWith("--")) profile = args[++i];
                else { Console.Error.WriteLine("Argument inconnu ou incomplet : " + a); Console.Error.WriteLine(Usage); return 2; }
            }
            if (!silent || profile == null) { Console.Error.WriteLine(Usage); return 2; }
            try { return RunSilent(profile); }
            catch (Exception ex) { TraceAppli.Erreur("routine : erreur inattendue", ex); Console.Error.WriteLine("Erreur inattendue : " + ex.Message); return 2; }
        }

        private static int RunSilent(string profilePath)
        {
            if (!IsAdministrator()) { Console.Error.WriteLine("Droits administrateur requis pour appliquer un profil."); return 2; }
            // Verrou d'instance d'abord (occupé : code 2, aucun parcours), puis contrôle de %ProgramFiles%\GamingTweaker avant toute lecture
            // (profil copié par la routine) ou écriture : refus (code 2) s'il n'est pas sûr. RunPipeline.ExecuteAsync reprend ensuite le verrou.
            var erreur = DemarrageSur.VerifierDossierProtege(DataPaths.RacineProtegeeParDefaut, IsAdministrator);
            if (erreur != null) { Console.Error.WriteLine(erreur); return 2; }

            var catalogue = Catalogue.LoadEmbedded();
            var load = Profils.Load(profilePath, catalogue.Where(c => !string.IsNullOrEmpty(c.Liste)).Select(c => c.Liste).Distinct());
            if (load.Erreur != null) { Console.Error.WriteLine(load.Erreur); return 2; }

            var facts = SystemInfo.Detect();
            var resolved = Profils.Resolve(load.Profil, catalogue, facts);
            var req = new RunRequest
            {
                Selection = resolved.Selection, Silent = true, CatalogItems = catalogue.ToList(), Facts = facts,
                Paths = DataPaths.Resolve(AppDomain.CurrentDomain.BaseDirectory, facts, DataPaths.RacineProtegeeParDefaut),
            };
            TraceAppli.Configurer(req.Paths.MachineDir);
            TraceAppli.Ecrire("routine : profil « " + Path.GetFileName(profilePath) + " », " + resolved.Selection.OptionIds.Count + " option(s)");
            req.Avertissements.AddRange(load.Avertissements);
            req.Avertissements.AddRange(resolved.Inconnues.Select(id => "Option inconnue ignorée : " + id));
            req.Avertissements.AddRange(resolved.Incompatibles.Select(id => "Option incompatible avec ce PC, ignorée : " + id));

            var report = new RunPipeline(new RestorePoint(), ElevationCheck.SameUserAsDesktop)
                .ExecuteAsync(req, null, CancellationToken.None).GetAwaiter().GetResult();
            if (report.SessionDir != null) Console.Out.WriteLine(Path.Combine(report.SessionDir, "report.txt"));
            else foreach (var w in report.Avertissements) Console.Error.WriteLine(w);
            return report.ExitCode;
        }

        private static int RunUninstall(string[] args)
        {
            bool confirme = false;
            for (int i = 1; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--confirm", StringComparison.OrdinalIgnoreCase)) confirme = true;
                else { Console.Error.WriteLine("Argument inconnu : " + args[i]); Console.Error.WriteLine(Usage); return 2; }
            }
            if (!IsAdministrator()) { Console.Error.WriteLine("Droits administrateur requis pour désinstaller."); return 2; }
            BilanDesinstallation b;
            try { b = Desinstallation.Executer(confirme); }
            catch (Exception ex) { Console.Error.WriteLine("Désinstallation impossible : " + ex.Message); return 2; }
            foreach (var a in b.Actions) Console.Out.WriteLine(a);
            foreach (var w in b.Avertissements) Console.Error.WriteLine("Attention : " + w);
            if (!confirme) Console.Out.WriteLine("Relancez avec --confirm pour exécuter.");
            return b.Code;
        }

        private static bool IsAdministrator()
        {
            using (var id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>Application WinExe : rattache la console du parent pour que stdout et stderr soient visibles.</summary>
        private static void AttachParentConsole()
        {
            try
            {
                if (!AttachConsole(-1)) return;
                // Sans redirection, les poignées standard sont invalides : on les rouvre sur la console.
                foreach (var n in new[] { -11, -12 })
                {
                    var h = GetStdHandle(n);
                    if (h == IntPtr.Zero || h == new IntPtr(-1))
                    {
                        var c = CreateFile("CONOUT$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                        if (c != new IntPtr(-1)) SetStdHandle(n, c);
                    }
                }
                var enc = Console.OutputEncoding; // page de code de la console ; UTF-8 sans BOM
                if (enc.CodePage == 65001) enc = new System.Text.UTF8Encoding(false);
                var o = new StreamWriter(Console.OpenStandardOutput(), enc) { AutoFlush = true };
                var e = new StreamWriter(Console.OpenStandardError(), enc) { AutoFlush = true };
                Console.SetOut(o); Console.SetError(e);
            }
            catch (Exception) { /* sans console : tant pis */ }
        }
    }
}
