if ((Get-MpPreference).DisableRealtimeMonitoring) { Set-MpPreference -DisableRealtimeMonitoring $false }
