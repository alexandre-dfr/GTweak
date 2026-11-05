Remove-TweakPath -Path @(
    (Join-Path $env:ProgramData 'Microsoft\Windows\WER'),
    (Join-Path $env:LOCALAPPDATA 'CrashDumps'),
    (Join-Path $env:SystemRoot 'Minidump'),
    (Join-Path $env:SystemRoot 'MEMORY.DMP')
) | Out-Null
