$capabilities = @('Browser.InternetExplorer', 'MathRecognizer', 'Microsoft.Windows.PowerShell.ISE', 'Print.Management.Console', 'App.Support.QuickAssist', 'App.StepsRecorder', 'Print.Fax.Scan')
$n = 0
foreach ($c in $capabilities) { $n += [int](Set-TweakDismCapabilityOff -Prefix $c) }
if ($n -eq 0) { Skip-GTItem 'Aucune de ces fonctionnalités à la demande n''est installée sur cette machine.' }
if ($script:GT_DismReboot) { Set-GTItemNote ($n.ToString() + ' fonctionnalité(s) retirée(s) ; un redémarrage est nécessaire.') } else { Set-GTItemNote ($n.ToString() + ' fonctionnalité(s) retirée(s).') }
