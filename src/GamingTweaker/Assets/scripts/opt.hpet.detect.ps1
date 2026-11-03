try {
    $a = Get-GTBcdCurrent 'useplatformclock'
    $b = Get-GTBcdCurrent 'disabledynamictick'
    if (($null -eq $a) -and ($b -eq 'yes')) { 'Applied' } else { 'NotApplied' }
} catch {
    'Unknown'
}
