$gtTotal = Get-TweakPathSize -Path @(
    (Join-Path $env:SystemRoot 'Logs\CBS'),
    (Join-Path $env:SystemRoot 'Logs\DISM'),
    (Join-Path $env:SystemRoot 'Logs\WindowsUpdate'),
    (Join-Path $env:SystemRoot 'Traces\WindowsUpdate')
)
$gtEvt = Join-Path $env:SystemRoot 'System32\winevt\Logs'
if (Test-Path -LiteralPath $gtEvt) {
    foreach ($gtF in @(Get-ChildItem -LiteralPath $gtEvt -Filter '*.evtx' -File)) {
        if ($gtF.Name -ine 'Security.evtx') { $gtTotal += $gtF.Length }
    }
}
[long]$gtTotal
