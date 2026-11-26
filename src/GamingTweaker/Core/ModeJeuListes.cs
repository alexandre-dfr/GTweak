using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace GamingTweaker.Core
{
    /// <summary>Listes proposées dans l'interface : services en cours d'exécution et processus, sans les éléments protégés.</summary>
    public static class ModeJeuListes
    {
        public static List<ServiceChoix> ListerServices()
        {
            var res = new List<ServiceChoix>();
            try
            {
                foreach (var s in ServiceController.GetServices())
                {
                    using (s)
                        if (s.Status == ServiceControllerStatus.Running && s.CanStop && !ModeJeu.EstServiceProtege(s.ServiceName))
                            res.Add(new ServiceChoix { Nom = s.ServiceName, Affichage = s.DisplayName });
                }
            }
            catch (Exception exc) { TraceAppli.Erreur("mode jeu : liste des services", exc); }
            return res.OrderBy(s => s.Affichage, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static List<ProcessusChoix> ListerProcessus()
        {
            var res = new Dictionary<string, ProcessusChoix>(StringComparer.OrdinalIgnoreCase);
            int session = 0;
            try { session = Process.GetCurrentProcess().SessionId; } catch (Exception) { }
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        if (p.SessionId != session || ModeJeu.EstProcessusProtege(p.ProcessName)) continue;
                        string chemin = null;
                        try { chemin = p.MainModule.FileName; } catch (Exception) { }
                        if (string.IsNullOrEmpty(chemin)) continue; // sans chemin il ne pourrait pas être relancé
                        if (res.ContainsKey(p.ProcessName)) continue;
                        res[p.ProcessName] = new ProcessusChoix { Nom = p.ProcessName, Chemin = chemin, Detail = chemin };
                    }
                    catch (Exception) { }
                }
            }
            return res.Values.OrderBy(x => x.Nom, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
}
