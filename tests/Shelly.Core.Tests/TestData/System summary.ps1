# Read-only summary of this computer: Windows, CPU, memory, disks, graphics, network, battery and busiest processes.
# Safe to run: it only reads information (CIM/WMI and the registry) and changes nothing. No administrator rights needed.
# Works in Windows PowerShell 5.1 and PowerShell 7. Every section is independent: if one fails, the others still run.
# -Export also saves the report as .txt and .html (on the Desktop, or next to the script with -ExportTo Script).

param(
    [switch]$Export,
    [ValidateSet('Desktop', 'Script')]
    [string]$ExportTo = 'Desktop'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$report = New-Object System.Collections.Generic.List[string]
$width = 64

# ---------- Output helpers (plain text, no colors, so it reads well anywhere) ----------
function Out-Line([string]$text = '') {
    $report.Add($text)
    Write-Output $text
}

function Out-Pair([string]$name, $value) {
    if ($null -eq $value -or [string]$value -eq '') { $value = '-' }
    Out-Line ('  {0,-18}{1}' -f $name, $value)
}

function Get-Short([string]$text, [int]$max) {
    if (-not $text) { return '' }
    if ($text.Length -le $max) { return $text }
    return $text.Substring(0, $max - 3) + '...'
}

function Format-Size([double]$bytes) {
    if ($bytes -ge 1TB) { return '{0:N1} TB' -f ($bytes / 1TB) }
    if ($bytes -ge 1GB) { return '{0:N1} GB' -f ($bytes / 1GB) }
    if ($bytes -ge 1MB) { return '{0:N0} MB' -f ($bytes / 1MB) }
    return '{0:N0} KB' -f ($bytes / 1KB)
}

# Text bar like [#########...........]
function Format-Bar([double]$percent, [int]$size = 20) {
    $p = [Math]::Max(0, [Math]::Min(100, $percent))
    $n = [int][Math]::Round($p / 100 * $size)
    return '[' + ('#' * $n) + ('.' * ($size - $n)) + ']'
}

function Format-Speed($bitsPerSecond) {
    if (-not $bitsPerSecond) { return '-' }
    $b = [double]$bitsPerSecond
    if ($b -le 0 -or $b -gt 1e12) { return '-' }
    if ($b -ge 1e9) { return '{0:N1} Gbit/s' -f ($b / 1e9) }
    return '{0:N0} Mbit/s' -f ($b / 1e6)
}

function Get-NetFrameworkName([int]$release) {
    if ($release -ge 533320) { return '4.8.1' }
    if ($release -ge 528040) { return '4.8' }
    if ($release -ge 461808) { return '4.7.2' }
    if ($release -ge 461308) { return '4.7.1' }
    if ($release -ge 460798) { return '4.7' }
    if ($release -ge 394802) { return '4.6.2' }
    if ($release -ge 394254) { return '4.6.1' }
    if ($release -ge 393295) { return '4.6' }
    if ($release -ge 378389) { return '4.5.x' }
    return 'not found'
}

# Runs one section; an error is reported in one line and the next section continues
function Invoke-Section([string]$title, [scriptblock]$body) {
    Out-Line ''
    Out-Line $title.ToUpper()
    Out-Line ('-' * $width)
    try { & $body } catch { Out-Line ('  (not available: {0})' -f ($_.Exception.Message -split '\r?\n')[0]) }
}

# ---------- Report ----------
Out-Line ('SYSTEM SUMMARY  |  {0}  |  {1}' -f $env:COMPUTERNAME, (Get-Date).ToString('yyyy-MM-dd HH:mm'))
Out-Line ('=' * $width)

Invoke-Section 'Computer' {
    Out-Pair 'Name' $env:COMPUTERNAME
    Out-Pair 'User' ('{0}\{1}' -f $env:USERDOMAIN, $env:USERNAME)
    $cs = Get-CimInstance -ClassName Win32_ComputerSystem
    Out-Pair 'Model' ((('{0} {1}' -f $cs.Manufacturer, $cs.Model) -replace '\s+', ' ').Trim())
    if ($cs.PartOfDomain) { Out-Pair 'Domain' $cs.Domain } else { Out-Pair 'Workgroup' $cs.Workgroup }
}

Invoke-Section 'Windows' {
    $os = Get-CimInstance -ClassName Win32_OperatingSystem
    $cv = $null
    try { $cv = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' } catch {}
    $build = [string]$os.BuildNumber
    if ($cv -and $cv.UBR) { $build += '.' + $cv.UBR }
    $version = 'build ' + $build
    if ($cv -and $cv.DisplayVersion) { $version = $cv.DisplayVersion + ', ' + $version }
    Out-Pair 'Edition' $os.Caption
    Out-Pair 'Version' $version
    Out-Pair 'Architecture' $os.OSArchitecture
    if ($os.InstallDate) { Out-Pair 'Installed' $os.InstallDate.ToString('yyyy-MM-dd') }
    $boot = $os.LastBootUpTime
    $up = (Get-Date) - $boot
    Out-Pair 'Last boot' $boot.ToString('yyyy-MM-dd HH:mm')
    Out-Pair 'Uptime' ('{0} d {1} h {2} min' -f $up.Days, $up.Hours, $up.Minutes)
}

Invoke-Section 'Processor' {
    $cpus = @(Get-CimInstance -ClassName Win32_Processor)
    foreach ($cpu in $cpus) {
        Out-Pair 'Name' (($cpu.Name -replace '\s+', ' ').Trim())
        Out-Pair 'Cores / threads' ('{0} / {1}' -f $cpu.NumberOfCores, $cpu.NumberOfLogicalProcessors)
        if ($cpu.MaxClockSpeed) { Out-Pair 'Base clock' ('{0:N2} GHz' -f ($cpu.MaxClockSpeed / 1000)) }
    }
    $load = $null
    try { $load = (Get-CimInstance -ClassName Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'").PercentProcessorTime } catch {}
    if ($null -eq $load) { $load = ($cpus | Measure-Object -Property LoadPercentage -Average).Average }
    if ($null -ne $load) { Out-Pair 'Current load' ('{0} {1,3:N0} %' -f (Format-Bar $load), [double]$load) }
}

Invoke-Section 'Memory' {
    $os = Get-CimInstance -ClassName Win32_OperatingSystem
    $total = [double]$os.TotalVisibleMemorySize * 1KB
    $free = [double]$os.FreePhysicalMemory * 1KB
    $used = $total - $free
    $pct = 0
    if ($total -gt 0) { $pct = $used / $total * 100 }
    Out-Pair 'Total' (Format-Size $total)
    Out-Pair 'Used' ('{0}  ({1:N0} %)' -f (Format-Size $used), $pct)
    Out-Pair 'Free' (Format-Size $free)
    Out-Pair 'Usage' ('{0} {1,3:N0} %' -f (Format-Bar $pct), $pct)
    try {
        $modules = @(Get-CimInstance -ClassName Win32_PhysicalMemory)
        $sum = ($modules | Measure-Object -Property Capacity -Sum).Sum
        if ($sum) { Out-Pair 'Installed' ('{0} in {1} module(s)' -f (Format-Size $sum), $modules.Count) }
    } catch {}
}

Invoke-Section 'Disks' {
    $disks = @(Get-CimInstance -ClassName Win32_LogicalDisk -Filter 'DriveType=3')
    if ($disks.Count -eq 0) { Out-Line '  No fixed disks found.' }
    foreach ($d in $disks) {
        $size = [double]$d.Size
        $free = [double]$d.FreeSpace
        $pct = 0
        if ($size -gt 0) { $pct = ($size - $free) / $size * 100 }
        $label = $d.DeviceID
        if ($d.VolumeName) { $label += ' ' + $d.VolumeName }
        Out-Line ('  {0,-18}{1} {2,3:N0} % used' -f (Get-Short $label 17), (Format-Bar $pct), $pct)
        Out-Line ('  {0,-18}{1} free of {2}, {3}' -f '', (Format-Size $free), (Format-Size $size), $d.FileSystem)
    }
}

Invoke-Section 'Graphics' {
    foreach ($g in @(Get-CimInstance -ClassName Win32_VideoController)) {
        Out-Pair 'Adapter' $g.Name
        $extra = @()
        if ($g.CurrentHorizontalResolution) { $extra += ('{0} x {1}' -f $g.CurrentHorizontalResolution, $g.CurrentVerticalResolution) }
        if ($g.CurrentRefreshRate) { $extra += ('{0} Hz' -f $g.CurrentRefreshRate) }
        if ($g.DriverVersion) { $extra += ('driver ' + $g.DriverVersion) }
        if ($extra.Count -gt 0) { Out-Pair '' ($extra -join ', ') }
    }
}

Invoke-Section 'Network (connected adapters)' {
    $adapters = @(Get-CimInstance -ClassName Win32_NetworkAdapter -Filter 'NetConnectionStatus=2')
    $configs = @(Get-CimInstance -ClassName Win32_NetworkAdapterConfiguration -Filter 'IPEnabled=True')
    if ($adapters.Count -eq 0) { Out-Line '  No connected network adapters.' }
    $first = $true
    foreach ($a in $adapters) {
        if (-not $first) { Out-Line '' }
        $first = $false
        $cfg = $configs | Where-Object { $_.Index -eq $a.Index } | Select-Object -First 1
        $ipv4 = @()
        $gateway = @()
        if ($cfg) {
            $ipv4 = @($cfg.IPAddress | Where-Object { $_ -match '^\d{1,3}(\.\d{1,3}){3}$' })
            $gateway = @($cfg.DefaultIPGateway | Where-Object { $_ -match '^\d{1,3}(\.\d{1,3}){3}$' })
        }
        Out-Pair 'Connection' $a.NetConnectionID
        Out-Pair 'Device' (Get-Short $a.Name 44)
        Out-Pair 'IPv4' ($ipv4 -join ', ')
        if ($gateway.Count -gt 0) { Out-Pair 'Gateway' ($gateway -join ', ') }
        Out-Pair 'MAC' $a.MACAddress
        Out-Pair 'Link speed' (Format-Speed $a.Speed)
    }
}

Invoke-Section 'Battery' {
    $batteries = @(Get-CimInstance -ClassName Win32_Battery)
    if ($batteries.Count -eq 0) { Out-Line '  No battery (desktop computer, or not reported by Windows).'; return }
    foreach ($b in $batteries) {
        $state = switch ([int]$b.BatteryStatus) {
            1 { 'on battery' }  2 { 'plugged in' }  3 { 'fully charged' }  4 { 'low' }  5 { 'critical' }
            6 { 'charging' }  7 { 'charging' }  8 { 'charging' }  9 { 'charging' }  11 { 'partially charged' }
            default { 'unknown' }
        }
        $charge = [double]$b.EstimatedChargeRemaining
        Out-Pair 'Charge' ('{0} {1,3:N0} %  ({2})' -f (Format-Bar $charge), $charge, $state)
        $minutes = [double]$b.EstimatedRunTime
        if ([int]$b.BatteryStatus -eq 1 -and $minutes -gt 0 -and $minutes -lt 10000) {
            Out-Pair 'Time left' ('{0} h {1} min' -f [Math]::Floor($minutes / 60), ($minutes % 60))
        }
    }
}

Invoke-Section 'Top 5 processes by CPU (right now)' {
    $cores = [Environment]::ProcessorCount
    try {
        $top = @(Get-CimInstance -ClassName Win32_PerfFormattedData_PerfProc_Process |
            Where-Object { $_.Name -ne '_Total' -and $_.Name -ne 'Idle' } |
            Sort-Object -Property PercentProcessorTime -Descending | Select-Object -First 5)
        foreach ($p in $top) { Out-Line ('  {0,-36}{1,7:N1} %' -f (Get-Short $p.Name 35), ($p.PercentProcessorTime / $cores)) }
    } catch {
        # Performance counters unavailable: fall back to total processor time since start
        Out-Line '  (live load unavailable, showing total CPU time)'
        foreach ($p in @(Get-Process | Sort-Object -Property CPU -Descending | Select-Object -First 5)) {
            Out-Line ('  {0,-36}{1,8:N0} s' -f (Get-Short $p.ProcessName 35), $p.CPU)
        }
    }
}

Invoke-Section 'Top 5 processes by memory' {
    foreach ($p in @(Get-Process | Sort-Object -Property WorkingSet64 -Descending | Select-Object -First 5)) {
        Out-Line ('  {0,-36}{1,10}' -f (Get-Short $p.ProcessName 35), (Format-Size $p.WorkingSet64))
    }
}

Invoke-Section 'PowerShell and .NET' {
    $edition = 'Desktop'
    if ($PSVersionTable.PSEdition) { $edition = $PSVersionTable.PSEdition }
    Out-Pair 'PowerShell' ('{0} ({1})' -f $PSVersionTable.PSVersion, $edition)
    $release = 0
    try { $release = [int](Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release } catch {}
    Out-Pair '.NET Framework' (Get-NetFrameworkName $release)
    Out-Pair 'CLR' ([Environment]::Version.ToString())
    try { Out-Pair 'Runtime' ([System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription) } catch {}
}

Out-Line ''
Out-Line ('=' * $width)

# ---------- Optional export ----------
if ($Export) {
    $dir = $null
    if ($ExportTo -eq 'Script') { $dir = $PSScriptRoot } else { $dir = [Environment]::GetFolderPath('Desktop') }
    if (-not $dir -or -not (Test-Path -LiteralPath $dir)) { $dir = $env:TEMP }
    $base = Join-Path $dir ('System summary {0} {1}' -f $env:COMPUTERNAME, (Get-Date).ToString('yyyy-MM-dd HHmm'))
    try {
        $utf8 = New-Object System.Text.UTF8Encoding $true
        [IO.File]::WriteAllLines($base + '.txt', [string[]]$report.ToArray(), $utf8)
        $body = ($report | ForEach-Object { [System.Net.WebUtility]::HtmlEncode($_) }) -join "`n"
        $html = '<!DOCTYPE html><html><head><meta charset="utf-8"><title>System summary ' + [System.Net.WebUtility]::HtmlEncode($env:COMPUTERNAME) +
                '</title><style>body{background:#f3f3f3;margin:32px;font-family:Segoe UI,sans-serif}' +
                'pre{background:#fff;border:1px solid #e5e5e5;border-radius:8px;padding:20px;font:14px Cascadia Mono,Consolas,monospace}</style>' +
                '</head><body><pre>' + $body + '</pre></body></html>'
        [IO.File]::WriteAllText($base + '.html', $html, $utf8)
        Write-Output ('Saved: ' + $base + '.txt')
        Write-Output ('Saved: ' + $base + '.html')
    } catch {
        Write-Output ('Could not save the report: ' + $_.Exception.Message)
    }
}
