$gtR = Invoke-GTDism -Arguments @('/Online', '/Cleanup-Image', '/StartComponentCleanup')
if ($gtR.RebootRequired) { Set-GTItemNote 'Nettoyage des composants terminé ; un redémarrage est conseillé.' } else { Set-GTItemNote 'Nettoyage des composants terminé.' }
