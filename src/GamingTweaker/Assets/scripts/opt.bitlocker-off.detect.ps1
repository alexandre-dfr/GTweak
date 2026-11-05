try {
    $v = Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop
    if (($v.VolumeStatus -eq 'FullyDecrypted') -or ($v.VolumeStatus -eq 'DecryptionInProgress')) { 'Applied' } else { 'NotApplied' }
} catch {
    'Unknown'
}
