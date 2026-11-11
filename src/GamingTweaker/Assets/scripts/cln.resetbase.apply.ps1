$gtR = Invoke-GTDism -Arguments @('/Online', '/Cleanup-Image', '/StartComponentCleanup', '/ResetBase')
if ($gtR.RebootRequired) { Set-GTItemNote 'Base des composants réinitialisée ; un redémarrage est conseillé.' } else { Set-GTItemNote 'Base des composants réinitialisée.' }
