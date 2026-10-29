<#
.SYNOPSIS
    Builds, tests, publishes and packs Shelly. Windows PowerShell 5.1 and PowerShell 7.

.DESCRIPTION
    Tasks (several can be given, they run in this order):
      build    dotnet build of Shelly.slnx (Release)
      test     the unit tests (builds first); results in artifacts\test-results\*.trx
      publish  Shelly.exe, single file: artifacts\publish\fd (needs the .NET 10 Desktop Runtime)
               and artifacts\publish\sc (self-contained); -Flavor picks one
      pack     the release zips and SHA256SUMS.txt in artifacts\dist (publishes first), the same names and
               layout as the GitHub release: shelly-<version>.zip, shelly-<version>-selfcontained.zip
      local    publish fd and sc, and copy Shelly.exe and Shelly-selfcontained.exe to the repository root, next to Scripts (what build.bat does)
      clean    delete artifacts\ (and the bin\, obj\, publish\ folders of the old layout)
      all      build, test, publish, pack (the default)

    Version: VersionPrefix in Directory.Build.props. Without -Version a build is "<prefix>-dev" (the zips too).
    -Version 1.3.1 or v1.3.1 (the tag) builds a release; it must match VersionPrefix, a suffix after "-" (v1.4.0-beta.1)
    becomes the prerelease label.

.EXAMPLE
    .\build.ps1                      # everything: build, test, publish, pack
.EXAMPLE
    .\build.ps1 test
.EXAMPLE
    .\build.ps1 local                # Shelly.exe next to Scripts, for trying it out
.EXAMPLE
    .\build.ps1 pack -Version v1.3.1 # what the Release workflow runs for the tag v1.3.1
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [ValidateSet('build', 'test', 'publish', 'pack', 'local', 'clean', 'all')]
    [string[]] $Task = @('all'),

    [string] $Version = '',

    [ValidateSet('fd', 'sc')]
    [string[]] $Flavor = @('fd', 'sc'),

    [string] $Configuration = 'Release'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Root = $PSScriptRoot
$Artifacts = Join-Path $Root 'artifacts'
$Solution = Join-Path $Root 'Shelly.slnx'
$AppProject = Join-Path $Root 'src/Shelly/Shelly.csproj'
$Rid = 'win-x64'
# The files next to Shelly.exe in a release zip (folder Shelly/), in this order. build/.keep is written empty.
# Paths use / (Windows takes it too; the script also runs in PowerShell 7 on Linux and macOS).
$Layout = @('assets/shelly.ico', 'build/.keep', 'docs/USER_GUIDE.md', 'docs/USER_GUIDE_RU.md', 'docs/FEATURES_RU.md',
    'README.md', 'CHANGELOG.md', 'LICENSE')

function Step([string] $text) { Write-Host ''; Write-Host "==> $text" -ForegroundColor Cyan }
function Info([string] $text) { Write-Host "    $text" }
function Fail([string] $text) { throw $text }
function P([string] $relative) { return (Join-Path $Root $relative) }

function Invoke-Dotnet {
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Arguments)
    Write-Host "    dotnet $($Arguments -join ' ')" -ForegroundColor DarkGray
    # Not 'Stop' here: Windows PowerShell 5.1 can turn a native command's stderr into a (fatal) error record;
    # the exit code decides
    $ErrorActionPreference = 'Continue'
    & $script:Dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { Fail "dotnet $($Arguments[0]) failed (exit code $LASTEXITCODE)" }
}

function Write-Summary([string] $markdown) {
    # GitHub Actions: shown on the run's page
    if ($env:GITHUB_STEP_SUMMARY) {
        [System.IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, $markdown + "`n", (New-Object System.Text.UTF8Encoding($false)))
    }
}

function Find-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $cmd) { Fail ".NET SDK not found. Install the .NET 10 SDK: winget install Microsoft.DotNet.SDK.10" }
    return $cmd.Source
}

function Get-VersionPrefix {
    [xml] $props = Get-Content -Raw -Path (P 'Directory.Build.props')
    $node = $props.SelectSingleNode('//VersionPrefix')
    if (-not $node -or -not $node.InnerText.Trim()) { Fail 'No <VersionPrefix> in Directory.Build.props' }
    return $node.InnerText.Trim()
}

# The version label of the zips and the MSBuild properties that go with it
function Resolve-Version {
    $prefix = Get-VersionPrefix
    if (-not $Version) {
        return @{ Label = "$prefix-dev"; Properties = @(); Release = $false }
    }
    $v = $Version.Trim()
    if ($v.StartsWith('v')) { $v = $v.Substring(1) }
    if ($v -notmatch '^(\d+\.\d+\.\d+)(-([0-9A-Za-z][0-9A-Za-z.-]*))?$') { Fail "Version '$Version' is not X.Y.Z or X.Y.Z-label" }
    $number = $Matches[1]
    $suffix = ''
    if ($Matches[3]) { $suffix = $Matches[3] }
    if ($number -ne $prefix) {
        Fail "Version $v does not match VersionPrefix $prefix in Directory.Build.props (tag v$prefix expected)"
    }
    $properties = @('-p:ShellyRelease=true')
    if ($suffix) { $properties += "-p:VersionSuffix=$suffix" }
    return @{ Label = $v; Properties = $properties; Release = $true }
}

function Invoke-Git {
    # null when git is missing or fails. Windows PowerShell 5.1 turns a native command's stderr into an error record,
    # which $ErrorActionPreference = 'Stop' would make fatal: so not 'Stop' here.
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Arguments)
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { return $null }
    $ErrorActionPreference = 'Continue'
    $out = @(& git -C $Root -c core.quotepath=off @Arguments 2>$null)
    if ($LASTEXITCODE -ne 0) { return $null }
    return $out
}

function Get-CommitTime {
    # Zip entry times: the commit time (the same zip for the same commit), else now
    if ($env:SOURCE_DATE_EPOCH) { return [DateTimeOffset]::FromUnixTimeSeconds([long] $env:SOURCE_DATE_EPOCH) }
    $t = Invoke-Git log -1 --format=%ct
    if ($t) { return [DateTimeOffset]::FromUnixTimeSeconds([long] [string] (@($t)[0])) }
    return [DateTimeOffset]::UtcNow
}

function Get-TrackedScripts {
    # A Scripts folder is packed only with the files git tracks: never someone's own scripts from a working copy
    if (-not (Test-Path (P 'Scripts'))) { return @() }
    $files = Invoke-Git ls-files -- Scripts
    if (-not $files) { return @() }
    return @($files | Where-Object { $_ })
}

# ---------------------------------------------------------------------------------------------------------------

$script:Built = $false
$script:Published = @{}

function Invoke-Build {
    if ($script:Built) { return }
    Step "build ($($script:Ver.Label))"
    $a = @('build', $Solution, '-c', $Configuration, '-nologo') + $script:Ver.Properties
    Invoke-Dotnet @a
    $script:Built = $true
}

function Invoke-Test {
    Invoke-Build
    Step 'test'
    $results = Join-Path $Artifacts 'test-results'
    if (Test-Path $results) { Remove-Item -Recurse -Force $results }
    $a = @('test', $Solution, '-c', $Configuration, '--no-build', '-nologo',
        '--logger', 'trx;LogFileName=Shelly.Core.Tests.trx', '--logger', 'console;verbosity=minimal',
        '--results-directory', $results) + $script:Ver.Properties
    $failed = $null
    try { Invoke-Dotnet @a } catch { $failed = $_ }
    $trx = Get-ChildItem -Path $results -Filter *.trx -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($trx) {
        [xml] $x = Get-Content -Raw -Path $trx.FullName
        $c = $x.TestRun.ResultSummary.Counters
        $line = "Tests: $($c.passed) passed, $($c.failed) failed, $($c.total) total ($($x.TestRun.ResultSummary.outcome))"
        Info $line
        Write-Summary "### Shelly tests`n`n$line`n"
    }
    if ($failed) { throw $failed }
}

function Invoke-Publish([string[]] $flavors) {
    foreach ($f in $flavors) {
        if ($script:Published.ContainsKey($f)) { continue }
        $selfContained = 'false'
        $title = 'framework-dependent'
        if ($f -eq 'sc') { $selfContained = 'true'; $title = 'self-contained' }
        Step "publish $f ($title, $($script:Ver.Label))"
        $out = Join-Path $Artifacts "publish/$f"
        if (Test-Path $out) { Remove-Item -Recurse -Force $out }
        $a = @('publish', $AppProject, '-c', $Configuration, '-r', $Rid, "-p:SelfContained=$selfContained", '-o', $out,
            '-nologo') + $script:Ver.Properties
        Invoke-Dotnet @a
        $exe = Join-Path $out 'Shelly.exe'
        if (-not (Test-Path $exe)) { Fail "No Shelly.exe in $out" }
        $size = (Get-Item $exe).Length
        Info ('Shelly.exe ({0}): {1:N0} bytes' -f $f, $size)
        Write-Summary ('- Shelly.exe ({0}, {1}): {2:N0} bytes' -f $title, $script:Ver.Label, $size)
        $script:Published[$f] = $exe
    }
}

function New-Zip([string] $zipPath, [string] $exe, [string[]] $files, [DateTimeOffset] $time) {
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $entries = @(@{ Name = 'Shelly.exe'; Path = $exe })
        foreach ($rel in $files) {
            $src = $null
            if ($rel -ne 'build/.keep') { $src = P $rel }
            $entries += @{ Name = $rel; Path = $src }
        }
        foreach ($e in $entries) {
            # Forward slashes in entry names: the zip standard, and what Explorer and every unzip expect
            $entry = $zip.CreateEntry('Shelly/' + $e.Name, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $time
            $to = $entry.Open()
            try {
                if ($e.Path) {
                    $from = [System.IO.File]::OpenRead($e.Path)
                    try { $from.CopyTo($to) } finally { $from.Dispose() }
                }
            } finally { $to.Dispose() }
        }
    } finally { $zip.Dispose() }
}

function Get-Sha256([string] $path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($path)
    try { $bytes = $sha.ComputeHash($stream) } finally { $stream.Dispose(); $sha.Dispose() }
    return (($bytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Write-ReleaseNotes([string] $label) {
    # The CHANGELOG section of this version ("## [1.3.1] - date" up to the next "## ["): the body of the GitHub release
    $notes = Join-Path $Artifacts 'release-notes.md'
    if (Test-Path $notes) { Remove-Item -Force $notes }
    $number = ($label -split '-', 2)[0]
    $lines = Get-Content -Path (P 'CHANGELOG.md') -Encoding UTF8
    $found = $false
    $body = New-Object System.Collections.Generic.List[string]
    foreach ($l in $lines) {
        if ($l -match '^## \[') {
            if ($found) { break }
            if ($l -match ('^## \[' + [regex]::Escape($label) + '\]') -or $l -match ('^## \[' + [regex]::Escape($number) + '\]')) { $found = $true }
            continue
        }
        if ($found) { $body.Add($l) }
    }
    $text = ($body -join "`n").Trim()
    if (-not $found -or -not $text) { Info "CHANGELOG.md has no section [$number]: the release gets GitHub's generated notes only"; return }
    [System.IO.File]::WriteAllText($notes, $text + "`n", (New-Object System.Text.UTF8Encoding($false)))
    Info "Release notes: artifacts/release-notes.md ($($body.Count) lines from CHANGELOG.md)"
}

function Invoke-Pack {
    Invoke-Publish @('fd', 'sc')
    Step "pack ($($script:Ver.Label))"
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($rel in $Layout) {
        if ($rel -ne 'build/.keep' -and -not (Test-Path (P $rel))) { Fail "Missing $rel for the release zip" }
    }
    $files = @(Get-TrackedScripts) + $Layout
    $dist = Join-Path $Artifacts 'dist'
    if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $time = Get-CommitTime
    $label = $script:Ver.Label
    $zips = @(
        @{ Name = "shelly-$label.zip"; Exe = $script:Published['fd'] },
        @{ Name = "shelly-$label-selfcontained.zip"; Exe = $script:Published['sc'] }
    )
    foreach ($z in $zips) {
        $path = Join-Path $dist $z.Name
        New-Zip $path $z.Exe $files $time
        Info ('{0}: {1:N0} bytes, {2} files' -f $z.Name, (Get-Item $path).Length, ($files.Count + 1))
    }
    # "<sha256>  <name>" per zip, sorted by name, CRLF, ASCII: as Set-Content wrote it before
    $sums = Get-ChildItem -Path $dist -Filter *.zip | Sort-Object Name | ForEach-Object { '{0}  {1}' -f (Get-Sha256 $_.FullName), $_.Name }
    [System.IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'), (($sums -join "`r`n") + "`r`n"), [System.Text.Encoding]::ASCII)
    Info 'SHA256SUMS.txt'
    Write-ReleaseNotes $label
}

function Invoke-Local {
    Invoke-Publish @('fd', 'sc')
    Step 'local'
    $destFd = P 'Shelly.exe'
    $destSc = P 'Shelly-selfcontained.exe'
    try {
        Copy-Item -Force $script:Published['sc'] $destSc
        Copy-Item -Force $script:Published['fd'] $destFd
    }
    catch { Fail "Could not replace Shelly.exe / Shelly-selfcontained.exe (is Shelly running? close it, also in the tray): $($_.Exception.Message)" }
    Info 'Shelly.exe and Shelly-selfcontained.exe updated next to Scripts.'
}

function Invoke-Clean {
    Step 'clean'
    $dirs = @($Artifacts, (P 'publish'))
    foreach ($p in @('src/Shelly', 'src/Shelly.Core', 'tests/Shelly.Core.Tests')) { $dirs += (P "$p/bin"); $dirs += (P "$p/obj") }
    foreach ($d in $dirs) {
        if (Test-Path $d) { Remove-Item -Recurse -Force $d; Info "deleted $($d.Substring($Root.Length + 1))" }
    }
}

# ---------------------------------------------------------------------------------------------------------------

try {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $order = @('clean', 'build', 'test', 'publish', 'pack', 'local')
    $wanted = @()
    foreach ($t in $Task) {
        if ($t -eq 'all') { $wanted += @('build', 'test', 'publish', 'pack') } else { $wanted += $t }
    }
    $run = @($order | Where-Object { $wanted -contains $_ })
    $needsDotnet = @($run | Where-Object { $_ -ne 'clean' }).Count -gt 0
    if ($needsDotnet) {
        $script:Dotnet = Find-Dotnet
        $ErrorActionPreference = 'Continue'
        $sdk = & $script:Dotnet --version
        $ErrorActionPreference = 'Stop'
        if ($LASTEXITCODE -ne 0) { Fail 'dotnet --version failed: no SDK that global.json allows (10.0.x). Install the .NET 10 SDK.' }
        $script:Ver = Resolve-Version
        Write-Host "Shelly $($script:Ver.Label), .NET SDK $sdk, tasks: $($run -join ', ')"
    }
    foreach ($t in $run) {
        switch ($t) {
            'clean' { Invoke-Clean }
            'build' { Invoke-Build }
            'test' { Invoke-Test }
            'publish' { Invoke-Publish $Flavor }
            'pack' { Invoke-Pack }
            'local' { Invoke-Local }
        }
    }
    Write-Host ''
    Write-Host ('Done in {0:N0} s.' -f $watch.Elapsed.TotalSeconds) -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ''
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
