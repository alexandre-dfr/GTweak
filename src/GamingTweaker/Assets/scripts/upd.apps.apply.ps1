$exe = Get-GTWingetPath
if (-not $exe) { Skip-GTItem 'winget est introuvable sur ce PC : mises à jour des applications ignorées.' }
$list = Invoke-GTNative $exe @('upgrade', '--accept-source-agreements', '--disable-interactivity')
if (-not (Test-GTWingetListOk $list.ExitCode)) { throw ('winget upgrade a échoué (code ' + $list.ExitCode + ').') }
$available = @(ConvertFrom-GTWingetUpgrade $list.Output)
# Liste autorisée : uniquement ce que winget propose en mise à jour. Un nom explicite qui n'y figure plus (déjà à jour) est ignoré, jamais exécuté.
$r = Resolve-GTListSelection -Selection $Selection -Allowed @($available | ForEach-Object { $_.Id } | Select-Object -Unique)
if ($r.Names.Count -eq 0) { Skip-GTItem 'Aucune mise à jour à installer parmi la sélection (déjà à jour, exclue ou absente).' }
$ok = 0; $errors = @(); $reboot = $false
foreach ($id in $r.Names) {
    $u = Invoke-GTNative $exe @('upgrade', '--id', $id, '--exact', '--silent', '--accept-package-agreements', '--accept-source-agreements', '--disable-interactivity')
    if (Test-GTWingetOk $u.ExitCode) { $ok++; if (Test-GTWingetReboot $u.ExitCode) { $reboot = $true } } else { $errors += ($id + ' (code ' + $u.ExitCode + ')') }
}
if ($errors.Count -gt 0) { throw ($ok.ToString() + ' application(s) mise(s) à jour ; échec pour : ' + ($errors -join ', ')) }
if ($reboot) { Set-GTItemNote ($ok.ToString() + ' application(s) mise(s) à jour ; un redémarrage est nécessaire pour terminer certaines.') }
else { Set-GTItemNote ($ok.ToString() + ' application(s) mise(s) à jour.') }
