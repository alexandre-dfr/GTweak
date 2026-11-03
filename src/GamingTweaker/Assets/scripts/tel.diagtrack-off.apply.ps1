$found = $false
foreach ($n in 'DiagTrack', 'dmwappushservice') {
    if (Set-TweakServiceIfNeeded -Name $n -StartType 'Disabled') {
        $found = $true
        Stop-Service -Name $n -Force -ErrorAction SilentlyContinue
    }
}
if (-not $found) { Skip-GTItem 'Les services DiagTrack et dmwappushservice sont absents de cette machine.' }
