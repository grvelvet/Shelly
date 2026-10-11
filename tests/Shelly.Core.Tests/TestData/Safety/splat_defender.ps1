# expect: Defender/high
$o = @{ DisableRealtimeMonitoring = $true }
Set-MpPreference @o
