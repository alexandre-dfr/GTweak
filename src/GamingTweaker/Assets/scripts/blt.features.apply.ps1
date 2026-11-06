$features = @('FaxServicesClientPackage', 'ScanManagementConsole', 'Internet-Explorer-Optional-amd64', 'Printing-XPSServices-Features', 'Xps-Foundation-Xps-Viewer', 'WorkFolders-Client', 'Printing-Foundation-InternetPrinting-Client', 'Printing-Foundation-LPDPrintService', 'Printing-Foundation-LPRPortMonitor')
$n = 0
foreach ($f in $features) { if (Set-TweakDismFeatureOff -Name $f) { $n++ } }
if ($n -eq 0) { Skip-GTItem 'Aucune de ces fonctionnalités n''est activée sur cette machine.' }
if ($script:GT_DismReboot) { Set-GTItemNote ($n.ToString() + ' fonctionnalité(s) désactivée(s) ; un redémarrage est nécessaire.') } else { Set-GTItemNote ($n.ToString() + ' fonctionnalité(s) désactivée(s).') }
