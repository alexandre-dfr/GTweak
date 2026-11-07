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
            AssemblyLoader.Register();
            if (args.Length == 0) return LaunchUi();
            return RunCommandLine(args);
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
            "Codes de sortie : 0 = tout est appliqué ou ignoré, 1 = au moins un échec, 2 = exécution impossible.";

        private static int RunCommandLine(string[] args)
        {
            AttachParentConsole();
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
            catch (Exception ex) { Console.Error.WriteLine("Erreur inattendue : " + ex.Message); return 2; }
        }

        private static int RunSilent(string profilePath)
        {
            // Le verrou d'instance est pris par RunPipeline.ExecuteAsync (interface et routine).
            if (!IsAdministrator()) { Console.Error.WriteLine("Droits administrateur requis pour appliquer un profil."); return 2; }
            // Avant toute lecture (profil copié par la routine) ou écriture sous C:\ProgramData\GamingTweaker.
            var protection = DossierProtege.SecuriserAuDemarrage(DataPaths.DefaultProgramDataRoot, IsAdministrator);
            if (protection.Erreur != null) { Console.Error.WriteLine(protection.Erreur); return 2; }
            foreach (var w in protection.Avertissements) Console.Error.WriteLine("Avertissement : " + w);

            var catalogue = Catalogue.LoadEmbedded();
            var load = Profils.Load(profilePath, catalogue.Where(c => !string.IsNullOrEmpty(c.Liste)).Select(c => c.Liste).Distinct());
            if (load.Erreur != null) { Console.Error.WriteLine(load.Erreur); return 2; }

            var facts = SystemInfo.Detect();
            var resolved = Profils.Resolve(load.Profil, catalogue, facts);
            var req = new RunRequest
            {
                Selection = resolved.Selection, Silent = true, CatalogItems = catalogue.ToList(), Facts = facts,
                Paths = DataPaths.Resolve(AppDomain.CurrentDomain.BaseDirectory, facts, DataPaths.DefaultProgramDataRoot),
            };
            req.Avertissements.AddRange(protection.Avertissements.Select(w => "Dossier de données corrigé : " + w));
            req.Avertissements.AddRange(load.Avertissements);
            req.Avertissements.AddRange(resolved.Inconnues.Select(id => "Option inconnue ignorée : " + id));
            req.Avertissements.AddRange(resolved.Incompatibles.Select(id => "Option incompatible avec ce PC, ignorée : " + id));

            var report = new RunPipeline(new RestorePoint(), ElevationCheck.SameUserAsDesktop)
                .ExecuteAsync(req, null, CancellationToken.None).GetAwaiter().GetResult();
            if (report.SessionDir != null) Console.Out.WriteLine(Path.Combine(report.SessionDir, "report.txt"));
            else foreach (var w in report.Avertissements) Console.Error.WriteLine(w);
            return report.ExitCode;
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
