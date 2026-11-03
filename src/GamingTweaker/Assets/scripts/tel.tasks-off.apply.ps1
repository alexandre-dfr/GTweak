$k = 'HKLM:\SOFTWARE\Policies\Microsoft\SQMClient\Windows'
Set-TweakRegistryIfNeeded -Path $k -Name 'CEIPEnable' -Type DWord -Value 0
$k = 'HKLM:\SOFTWARE\Microsoft\SQMClient\Windows'
Set-TweakRegistryIfNeeded -Path $k -Name 'CEIPEnable' -Type DWord -Value 0
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser Exp' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Application Experience\ProgramDataUpdater' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Autochk\Proxy' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Customer Experience Improvement Program\Consolidator' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Customer Experience Improvement Program\KernelCeipTask' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Customer Experience Improvement Program\BthSQM' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Customer Experience Improvement Program\Uploader' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Feedback\Siuf\DmClient' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Device Information\Device' -Enabled $false)
[void](Set-TweakTaskIfNeeded -Path '\Microsoft\Windows\Device Information\Device User' -Enabled $false)
