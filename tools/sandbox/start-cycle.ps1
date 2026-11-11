# Lancé par cycle.wsb dans Windows Sandbox (compte WDAGUtilityAccount, administrateur).
$ErrorActionPreference = 'Continue'
$res = 'C:\GT\resultats'
$exe = 'C:\GT\exe\GamingTweaker.exe'
$sortie = Join-Path $res 'cycle.json'
$journal = Join-Path $res 'cycle.log'
Remove-Item $sortie -ErrorAction SilentlyContinue
try {
    "Démarrage $(Get-Date -Format s)" | Out-File $journal -Encoding utf8
    # Application WinExe : on attend la fin et on redirige la sortie standard vers le journal.
    $p = Start-Process -FilePath $exe -ArgumentList @('--cycle-test', "`"$sortie`"") -Wait -PassThru -RedirectStandardOutput (Join-Path $res 'cycle.out.txt') -RedirectStandardError (Join-Path $res 'cycle.err.txt')
    "Code de sortie : $($p.ExitCode)" | Out-File $journal -Append -Encoding utf8
} catch {
    "Erreur : $_" | Out-File $journal -Append -Encoding utf8
}
"Fin $(Get-Date -Format s)" | Out-File $journal -Append -Encoding utf8
Stop-Computer -Force
