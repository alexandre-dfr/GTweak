using System;
using System.Management;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GamingTweaker.Core
{
    public class RestorePointResult
    {
        public bool Created { get; set; }
        public string Message { get; set; } = "";
    }

    public interface IRestorePoint
    {
        RestorePointResult TryCreate(string description);
    }

    /// <summary>
    /// Point de restauration créé directement par WMI (root\default:SystemRestore.CreateRestorePoint, ce qu'appelle Checkpoint-Computer),
    /// sans lancer powershell.exe. Le cliché VSS peut prendre plusieurs minutes (disque chargé, premier point, mise à jour en cours) :
    /// l'attente est longue et, si elle est dépassée, la création n'est pas annulée (le service la poursuit) ; le dernier numéro de point
    /// est relu avant et après pour ne jamais annoncer un point qui n'existe pas.
    /// La limite de fréquence de Windows (1 point / 24 h) est levée le temps de l'appel et la valeur d'origine est toujours rétablie
    /// (elle l'est par ce processus, plus par un script qui pouvait être tué avant son bloc finally).
    /// </summary>
    public class RestorePoint : IRestorePoint
    {
        /// <summary>Attente maximale de la création (le cliché VSS d'un gros disque dépasse couramment 3 min).</summary>
        public static TimeSpan Delai = TimeSpan.FromMinutes(10);

        private const string CleFrequence = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
        private const string NomFrequence = "SystemRestorePointCreationFrequency";
        private const int ModifySettings = 12;      // RestorePointType MODIFY_SETTINGS
        private const int BeginSystemChange = 100;  // EventType BEGIN_SYSTEM_CHANGE

        public RestorePointResult TryCreate(string description)
        {
            try
            {
                long avant = DernierNumero();
                uint code;
                bool fini;
                object ancienne = null; bool existait = false, leve = false;
                try
                {
                    leve = LeverLimite(out existait, out ancienne);
                    var creation = Task.Run(() => Creer(description ?? "GamingTweaker"));
                    // WaitOne plutôt que Wait : l'exception d'origine (ManagementException) remonte sans AggregateException.
                    fini = ((IAsyncResult)creation).AsyncWaitHandle.WaitOne(Delai);
                    code = fini ? creation.GetAwaiter().GetResult() : 0;
                    if (!fini) creation.ContinueWith(t => { var _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                }
                finally
                {
                    // Windows lit la limite au début de la création : la rétablir ici ne gêne pas un cliché encore en cours.
                    if (leve) RetablirLimite(existait, ancienne);
                }

                long apres = DernierNumero();
                if (!fini)
                {
                    if (apres > avant) return new RestorePointResult { Created = true };
                    TraceAppli.Ecrire("point de restauration : délai de " + (int)Delai.TotalMinutes + " min dépassé, création poursuivie par Windows");
                    return new RestorePointResult { Message = "La création du point de restauration dépasse " + (int)Delai.TotalMinutes + " minutes (disque très sollicité ou service de cliché instantané bloqué). Windows la poursuit en arrière-plan, mais elle n'est pas confirmée." };
                }
                return Interpreter(code, avant, apres);
            }
            catch (ManagementException ex)
            {
                TraceAppli.Erreur("point de restauration", ex);
                return new RestorePointResult { Message = "Point de restauration impossible (la protection du système est peut-être désactivée) : " + ex.Message };
            }
            catch (Exception ex)
            {
                TraceAppli.Erreur("point de restauration", ex);
                return new RestorePointResult { Message = "Point de restauration impossible : " + ex.Message };
            }
        }

        /// <summary>Décision pure (testée) : code de retour de CreateRestorePoint et numéros de point relus avant / après.</summary>
        public static RestorePointResult Interpreter(uint code, long avant, long apres)
        {
            if (code == 0)
            {
                if (apres > avant) return new RestorePointResult { Created = true };
                return new RestorePointResult { Message = "Windows limite les points de restauration (un par 24 h par défaut) : aucun nouveau point n'a été créé." };
            }
            // 1058 (ERROR_SERVICE_DISABLED) / 0x80070422 : protection du système ou service désactivé.
            if (code == 1058 || code == 0x80070422)
                return new RestorePointResult { Message = "Point de restauration impossible : la protection du système est désactivée (Panneau de configuration > Système > Protection du système)." };
            // 1460 (ERROR_TIMEOUT) / 0x800705B4 : délai interne du service de cliché instantané.
            if (code == 1460 || code == 0x800705B4)
                return new RestorePointResult { Message = "Point de restauration impossible : le service de cliché instantané (VSS) n'a pas répondu à temps." };
            return new RestorePointResult { Message = "Point de restauration impossible (code " + code + ", la protection du système est peut-être désactivée)." };
        }

        private static uint Creer(string description)
        {
            using (var classe = new ManagementClass(new ManagementScope(@"\\.\root\default"), new ManagementPath("SystemRestore"), null))
            using (var entree = classe.GetMethodParameters("CreateRestorePoint"))
            {
                entree["Description"] = description;
                entree["RestorePointType"] = ModifySettings;
                entree["EventType"] = BeginSystemChange;
                using (var sortie = classe.InvokeMethod("CreateRestorePoint", entree, null))
                    return sortie == null ? 0u : Convert.ToUInt32(sortie["ReturnValue"]);
            }
        }

        /// <summary>Numéro du dernier point existant (0 si aucun ou illisible).</summary>
        private static long DernierNumero()
        {
            try
            {
                long max = 0;
                using (var s = new ManagementObjectSearcher(@"root\default", "SELECT SequenceNumber FROM SystemRestore"))
                using (var res = s.Get())
                    foreach (ManagementBaseObject o in res)
                        using (o) { var n = Convert.ToInt64(o["SequenceNumber"]); if (n > max) max = n; }
                return max;
            }
            catch (Exception) { return 0; }
        }

        private static bool LeverLimite(out bool existait, out object ancienne)
        {
            existait = false; ancienne = null;
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = hklm.OpenSubKey(CleFrequence, true))
                {
                    if (k == null) return false;
                    ancienne = k.GetValue(NomFrequence, null);
                    existait = ancienne != null;
                    k.SetValue(NomFrequence, 0, RegistryValueKind.DWord);
                    return true;
                }
            }
            catch (Exception ex) { TraceAppli.Erreur("point de restauration (limite de fréquence)", ex); return false; }
        }

        private static void RetablirLimite(bool existait, object ancienne)
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = hklm.OpenSubKey(CleFrequence, true))
                {
                    if (k == null) return;
                    if (existait) k.SetValue(NomFrequence, Convert.ToInt32(ancienne), RegistryValueKind.DWord);
                    else k.DeleteValue(NomFrequence, false);
                }
            }
            catch (Exception ex) { TraceAppli.Erreur("point de restauration (limite de fréquence)", ex); }
        }
    }
}
