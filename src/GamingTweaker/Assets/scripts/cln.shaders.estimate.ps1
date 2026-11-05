Get-TweakPathSize -Path @(
    (Join-Path $env:LOCALAPPDATA 'D3DSCache'),
    (Join-Path $env:LOCALAPPDATA 'NVIDIA\DXCache'),
    (Join-Path $env:LOCALAPPDATA 'NVIDIA\GLCache'),
    (Join-Path $env:LOCALAPPDATA 'AMD\DxCache')
)
