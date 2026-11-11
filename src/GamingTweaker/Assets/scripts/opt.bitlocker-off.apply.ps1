$v = $null
try { $v = Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop } catch { Skip-GTItem ('BitLocker est indisponible (édition de Windows) ou illisible : ' + $_.Exception.Message) }
if ($v.VolumeStatus -eq 'DecryptionInProgress') { Skip-GTItem 'Le déchiffrement du disque système est déjà en cours.' }
if ($v.VolumeStatus -eq 'FullyDecrypted') { Skip-GTItem 'Le disque système n''est pas chiffré par BitLocker : rien à faire.' }
Disable-BitLocker -MountPoint $env:SystemDrive | Out-Null
Set-GTItemNote 'Déchiffrement du disque système lancé : il continue en arrière-plan et peut durer longtemps. Suivez-le dans « Gérer BitLocker » ; il n''est pas annulable depuis GamingTweaker.'
