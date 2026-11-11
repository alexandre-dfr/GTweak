$gtRoots = New-Object System.Collections.Generic.List[string]
$gtSteam = Get-GTSteamDir
if ($gtSteam) {
    foreach ($gtSub in @('appcache\httpcache', 'dumps')) {
        $gtP = Join-Path $gtSteam $gtSub
        if (Test-Path -LiteralPath $gtP) { $gtRoots.Add($gtP) }
    }
}
$gtEpic = Join-Path $env:LOCALAPPDATA 'EpicGamesLauncher\Saved'
if (Test-Path -LiteralPath $gtEpic) {
    foreach ($gtD in @(Get-ChildItem -LiteralPath $gtEpic -Directory -Filter 'webcache*')) { $gtRoots.Add($gtD.FullName) }
}
Get-TweakPathSize -Path @($gtRoots)
