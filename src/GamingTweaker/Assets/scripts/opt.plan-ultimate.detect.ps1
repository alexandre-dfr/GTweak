try {
    $a = Get-GTActiveScheme
    if ($a -ieq 'e9a42b02-d5df-448d-aa00-03f14749eb61' -or $a -ieq 'd8f3a1c2-5e7b-4c9a-9f10-67616d696e67') { 'Applied' } else { 'NotApplied' }
} catch {
    'Unknown'
}
