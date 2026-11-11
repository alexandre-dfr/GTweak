# Dossiers de cache uniquement : rien d'autre dans les profils des navigateurs.

$gtRoots = New-Object System.Collections.Generic.List[string]
foreach ($gtUd in @((Join-Path $env:LOCALAPPDATA 'Google\Chrome\User Data'), (Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\User Data'))) {
    if (-not (Test-Path -LiteralPath $gtUd)) { continue }
    foreach ($gtProf in @(Get-ChildItem -LiteralPath $gtUd -Directory | Where-Object { $_.Name -eq 'Default' -or $_.Name -like 'Profile *' })) {
        foreach ($gtCache in @('Cache', 'Code Cache', 'GPUCache')) {
            $gtP = Join-Path $gtProf.FullName $gtCache
            if (Test-Path -LiteralPath $gtP) { $gtRoots.Add($gtP) }
        }
    }
    $gtP = Join-Path $gtUd 'Crashpad\reports'
    if (Test-Path -LiteralPath $gtP) { $gtRoots.Add($gtP) }
}
$gtFx = Join-Path $env:LOCALAPPDATA 'Mozilla\Firefox\Profiles'
if (Test-Path -LiteralPath $gtFx) {
    foreach ($gtProf in @(Get-ChildItem -LiteralPath $gtFx -Directory)) {
        $gtP = Join-Path $gtProf.FullName 'cache2'
        if (Test-Path -LiteralPath $gtP) { $gtRoots.Add($gtP) }
    }
}
$gtP = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\INetCache\IE'
if (Test-Path -LiteralPath $gtP) { $gtRoots.Add($gtP) }
Remove-TweakPath -Path @($gtRoots) | Out-Null
