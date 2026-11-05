Get-TweakPathSize -Path @(
    (Join-Path $env:SystemRoot 'SoftwareDistribution\Download'),
    (Join-Path $env:SystemRoot 'ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache')
)
