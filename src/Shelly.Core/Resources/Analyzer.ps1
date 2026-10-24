# Shelly script analyzer: runs in Windows PowerShell 5.1 or PowerShell 7 and reports, for each script,
# its description, parameters, #Requires, parse errors, engine hints and safety findings as JSON.
# Keep this file Windows PowerShell 5.1 syntax.
param([string]$In, [string]$Out)
$ErrorActionPreference = 'SilentlyContinue'
$script:moduleCache = @{}
# Built-in aliases of Windows PowerShell 5.1 and PowerShell 7 (both lists merged; on Windows the running engine's own
# Get-Alias is added on top). 'sc', 'curl' and 'wget' are aliases only in 5.1; in 7 they are the native programs.
$script:dzAliases = @{
    '?' = 'where-object'; '%' = 'foreach-object'; ac = 'add-content'; asnp = 'add-pssnapin'; cat = 'get-content'; cd = 'set-location'
    cfs = 'convertfrom-string'; chdir = 'set-location'; clc = 'clear-content'; clear = 'clear-host'; clhy = 'clear-history'
    cli = 'clear-item'; clp = 'clear-itemproperty'; cls = 'clear-host'; clv = 'clear-variable'; cnsn = 'connect-pssession'
    compare = 'compare-object'; copy = 'copy-item'; cp = 'copy-item'; cpi = 'copy-item'; cpp = 'copy-itemproperty'
    curl = 'invoke-webrequest'; cvpa = 'convert-path'; dbp = 'disable-psbreakpoint'; del = 'remove-item'; diff = 'compare-object'
    dir = 'get-childitem'; dnsn = 'disconnect-pssession'; ebp = 'enable-psbreakpoint'; echo = 'write-output'; epal = 'export-alias'
    epcsv = 'export-csv'; epsn = 'export-pssession'; erase = 'remove-item'; etsn = 'enter-pssession'; exsn = 'exit-pssession'
    fc = 'format-custom'; fhx = 'format-hex'; fl = 'format-list'; foreach = 'foreach-object'; ft = 'format-table'; fw = 'format-wide'
    gal = 'get-alias'; gbp = 'get-psbreakpoint'; gc = 'get-content'; gcb = 'get-clipboard'; gci = 'get-childitem'; gcm = 'get-command'
    gcs = 'get-pscallstack'; gdr = 'get-psdrive'; gerr = 'get-error'; ghy = 'get-history'; gi = 'get-item'; gin = 'get-computerinfo'
    gjb = 'get-job'; gl = 'get-location'; gm = 'get-member'; gmo = 'get-module'; gp = 'get-itemproperty'; gps = 'get-process'
    gpv = 'get-itempropertyvalue'; group = 'group-object'; gsn = 'get-pssession'; gsnp = 'get-pssnapin'; gsv = 'get-service'
    gtz = 'get-timezone'; gu = 'get-unique'; gv = 'get-variable'; gwmi = 'get-wmiobject'; h = 'get-history'; history = 'get-history'
    icm = 'invoke-command'; iex = 'invoke-expression'; ihy = 'invoke-history'; ii = 'invoke-item'; ipal = 'import-alias'
    ipcsv = 'import-csv'; ipmo = 'import-module'; ipsn = 'import-pssession'; irm = 'invoke-restmethod'; ise = 'powershell_ise'
    iwmi = 'invoke-wmimethod'; iwr = 'invoke-webrequest'; kill = 'stop-process'; lp = 'out-printer'; ls = 'get-childitem'
    man = 'help'; md = 'mkdir'; measure = 'measure-object'; mi = 'move-item'; mount = 'new-psdrive'; move = 'move-item'
    mp = 'move-itemproperty'; mv = 'move-item'; nal = 'new-alias'; ndr = 'new-psdrive'; ni = 'new-item'; nmo = 'new-module'
    npssc = 'new-pssessionconfigurationfile'; nsn = 'new-pssession'; nv = 'new-variable'; ogv = 'out-gridview'; oh = 'out-host'
    popd = 'pop-location'; ps = 'get-process'; pushd = 'push-location'; pwd = 'get-location'; r = 'invoke-history'
    rbp = 'remove-psbreakpoint'; rcjb = 'receive-job'; rcsn = 'receive-pssession'; rd = 'remove-item'; rdr = 'remove-psdrive'
    ren = 'rename-item'; ri = 'remove-item'; rjb = 'remove-job'; rm = 'remove-item'; rmdir = 'remove-item'; rmo = 'remove-module'
    rni = 'rename-item'; rnp = 'rename-itemproperty'; rp = 'remove-itemproperty'; rsn = 'remove-pssession'; rsnp = 'remove-pssnapin'
    rujb = 'resume-job'; rv = 'remove-variable'; rvpa = 'resolve-path'; rwmi = 'remove-wmiobject'; sajb = 'start-job'
    sal = 'set-alias'; saps = 'start-process'; sasv = 'start-service'; sbp = 'set-psbreakpoint'; scb = 'set-clipboard'
    select = 'select-object'; set = 'set-variable'; shcm = 'show-command'; si = 'set-item'; sl = 'set-location'; sleep = 'start-sleep'
    sls = 'select-string'; sort = 'sort-object'; sp = 'set-itemproperty'; spjb = 'stop-job'; spps = 'stop-process'
    spsv = 'stop-service'; start = 'start-process'; stz = 'set-timezone'; sujb = 'suspend-job'; sv = 'set-variable'
    swmi = 'set-wmiinstance'; tee = 'tee-object'; trcm = 'trace-command'; type = 'get-content'; wget = 'invoke-webrequest'
    where = 'where-object'; wjb = 'wait-job'; write = 'write-output'
}
try { foreach ($a in @(Get-Alias -ErrorAction SilentlyContinue)) { $k = $a.Name.ToLower(); if (-not $script:dzAliases.ContainsKey($k) -and $a.Definition) { $script:dzAliases[$k] = ([string]$a.Definition).ToLower() } } } catch {}
$script:dzSecServices = @('windefend', 'wdnissvc', 'wdfilter', 'sense', 'securityhealthservice', 'wscsvc', 'mpssvc', 'bfe',
                          'eventlog', 'wuauserv', 'usosvc', 'waasmedicsvc', 'appidsvc', 'vss', 'srservice', 'swprv')
$script:dzCoreServices = @('rpcss', 'dcomlaunch', 'winmgmt', 'cryptsvc', 'dhcp', 'dnscache', 'nsi', 'lanmanworkstation',
                           'lanmanserver', 'schedule', 'samss', 'lsm', 'gpsvc', 'trustedinstaller', 'bits', 'profsvc',
                           'power', 'plugplay', 'eventsystem', 'netlogon', 'w32time', 'mpssvc', 'termservice', 'wlansvc')
$script:dzSystemPath = '(?i)^[a-z]:[\\/]?(\*(\.\*)?)?$|^[\\/](\*(\.\*)?)?$|^(\$env:systemdrive|\$\{env:systemdrive\})[\\/]?(\*(\.\*)?)?$' +
    '|^[a-z]:[\\/]windows([\\/]|$)|system32|syswow64|\$\{?env:(windir|systemroot|programfiles|programdata|commonprogramfiles)' +
    '|program files|^[a-z]:[\\/]programdata|^[a-z]:[\\/]users[\\/]?(\*(\.\*)?)?$|^(\$env:userprofile|\$\{env:userprofile\}|\$home|~)[\\/]?(\*(\.\*)?)?$' +
    '|^hklm:|hkey_local_machine|^[a-z]:[\\/]boot([\\/]|$)'
$script:ps7TokenText = @{
    QuestionQuestion = '??'; QuestionQuestionEquals = '??='; QuestionDot = '?.'; QuestionLBracket = '?[]'
    AndAnd = '&&'; OrOr = '||'; QuestionMark = '? :'
}
$script:ps7Cmdlets = @('get-error', 'join-string', 'test-json', 'get-uptime', 'convertfrom-markdown', 'show-markdown',
    'get-markdownoption', 'set-markdownoption', 'remove-alias', 'remove-service', 'get-securerandom', 'get-pssubsystem',
    'enable-experimentalfeature', 'disable-experimentalfeature', 'get-experimentalfeature', 'convertto-clixml', 'convertfrom-clixml')
$script:ps7Params = @{
    'foreach-object' = @('parallel', 'throttlelimit', 'asjob'); '%' = @('parallel'); 'foreach' = @('parallel')
    'convertfrom-json' = @('ashashtable', 'noenumerate', 'depth'); 'convertto-json' = @('enumsasstrings', 'asarray', 'escapehandling')
    'invoke-webrequest' = @('skipcertificatecheck', 'authentication', 'token', 'statuscodevariable', 'responseheadersvariable', 'skiphttperrorcheck', 'resume')
    'invoke-restmethod' = @('skipcertificatecheck', 'authentication', 'token', 'statuscodevariable', 'responseheadersvariable', 'skiphttperrorcheck', 'resume')
    'iwr' = @('skipcertificatecheck'); 'irm' = @('skipcertificatecheck')
    'get-content' = @('asbytestream'); 'set-content' = @('asbytestream'); 'add-content' = @('asbytestream')
    'select-string' = @('raw', 'noemphasis'); 'test-connection' = @('targetname', 'tcpport', 'mtusize', 'traceroute')
}
$script:winPsCmdlets = @('get-wmiobject', 'gwmi', 'set-wmiinstance', 'swmi', 'invoke-wmimethod', 'iwmi', 'remove-wmiobject', 'rwmi',
    'register-wmievent', 'get-eventlog', 'clear-eventlog', 'write-eventlog', 'limit-eventlog', 'show-eventlog', 'new-eventlog',
    'remove-eventlog', 'add-pssnapin', 'get-pssnapin', 'remove-pssnapin', 'export-console', 'new-webserviceproxy',
    'get-transaction', 'start-transaction', 'complete-transaction', 'undo-transaction', 'use-transaction',
    'export-counter', 'import-counter', 'get-controlpanelitem', 'show-controlpanelitem', 'add-computer', 'remove-computer',
    'checkpoint-computer', 'restore-computer', 'get-computerrestorepoint', 'enable-computerrestore', 'disable-computerrestore',
    'reset-computermachinepassword', 'test-computersecurechannel', 'convert-string', 'convertfrom-string', 'cfs',
    'suspend-job', 'resume-job', 'new-psworkflowsession', 'new-psworkflowexecutionoption')
$script:winPsTypes = @{
    'Windows\.Forms\.DataVisualization' = 'System.Windows.Forms.DataVisualization'
    'System\.Web\.Script\.Serialization|System\.Web\.Extensions' = 'System.Web.Extensions'
    'System\.EnterpriseServices' = 'System.EnterpriseServices'
}
function Get-ParamsFromAst($ast) {
    $result = New-Object System.Collections.ArrayList
    if (-not $ast -or -not $ast.ParamBlock) { return }
    foreach ($p in $ast.ParamBlock.Parameters) {
        try {
            $type = ''; $mand = $false; $help = ''; $set = ''; $setVals = @()
            foreach ($a in $p.Attributes) {
                if ($a -is [System.Management.Automation.Language.TypeConstraintAst]) {
                    if (-not $type) { $type = $a.TypeName.FullName }
                } elseif ($a -is [System.Management.Automation.Language.AttributeAst]) {
                    $an = $a.TypeName.Name
                    if ($an -match '^(System\.Management\.Automation\.)?Parameter(Attribute)?$') {
                        foreach ($na in $a.NamedArguments) {
                            if ($na.ArgumentName -eq 'Mandatory') {
                                if ($na.ExpressionOmitted) { $mand = $true }
                                else { try { $mand = [bool]$na.Argument.SafeGetValue() } catch {} }
                            } elseif ($na.ArgumentName -eq 'HelpMessage') {
                                try { $help = [string]$na.Argument.SafeGetValue() } catch {}
                            }
                        }
                    } elseif ($an -match '^(System\.Management\.Automation\.)?ValidateSet(Attribute)?$') {
                        $vals = @()
                        foreach ($pa in $a.PositionalArguments) { try { $vals += [string]$pa.SafeGetValue() } catch {} }
                        $set = $vals -join ', '
                        $setVals = $vals
                    }
                }
            }
            $t = $type.ToLower()
            $kind = 'text'
            if ($t -eq 'switch' -or $t -eq 'system.management.automation.switchparameter') { $kind = 'switch' }
            elseif ($t -eq 'bool' -or $t -eq 'system.boolean') { $kind = 'bool' }
            elseif ($t.EndsWith('[]')) { $kind = 'array' }

            $default = $null; $defaultText = ''; $defaultBool = $false
            if ($p.DefaultValue) {
                $ok = $true; $v = $null
                try { $v = $p.DefaultValue.SafeGetValue() } catch { $ok = $false }
                if ($ok) {
                    if ($kind -eq 'switch' -or $kind -eq 'bool') { try { $defaultBool = [bool]$v } catch {} }
                    elseif ($null -ne $v) {
                        if ($v -is [array]) { $default = (@($v) | ForEach-Object { [string]$_ }) -join ', ' } else { $default = [string]$v }
                    }
                }
                $defaultText = $p.DefaultValue.Extent.Text
            }
            [void]$result.Add([pscustomobject]@{
                Name = $p.Name.VariablePath.UserPath; Type = $type; Kind = $kind; Mandatory = $mand
                Default = $default; DefaultText = $defaultText; DefaultBool = $defaultBool; Help = $help; Set = $set; SetValues = @($setVals)
            })
        } catch {}
    }
    $result
}

function Test-ModuleAvailable([string]$name) {
    if (-not $name) { return $true }
    if (-not $script:moduleCache.ContainsKey($name)) {
        $script:moduleCache[$name] = [bool](Get-Module -ListAvailable -Name $name -ErrorAction SilentlyContinue | Select-Object -First 1)
    }
    return $script:moduleCache[$name]
}

# Description: a one-line '# comment' among the first 8 lines (not #Requires, #! or a '# Shelly-...:' directive;
# a 'Description:' prefix is dropped), or the .SYNOPSIS of a leading <# #> help block, whichever comes first.
# The same rules as FallbackParser.Description.
function Get-Synopsis([string[]]$block) {
    $in = $false
    $parts = @()
    foreach ($raw in $block) {
        $t = $raw.Trim()
        if (-not $in) { if ($t -match '^(?i)\.SYNOPSIS\s*$') { $in = $true }; continue }
        if ($t -match '^\.[A-Za-z]+(\s|$)') { break }
        if ($t -eq '') { if ($parts.Count -gt 0) { break }; continue }
        $parts += $t
    }
    $s = ($parts -join ' ').Trim()
    if ($s) { return $s }
    return $null
}

function Get-Description([string]$path) {
    try {
        $lines = @(Get-Content -LiteralPath $path -TotalCount 60 -Encoding UTF8)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $t = ([string]$lines[$i]).Trim().TrimStart([char]0xFEFF)
            if ($t -eq '') { continue }
            if ($t.StartsWith('<#')) {
                $block = @()
                $cur = $t.Substring(2)
                $j = $i
                $closed = $false
                while ($true) {
                    $end = $cur.IndexOf('#>')
                    if ($end -ge 0) { $block += $cur.Substring(0, $end); $closed = $true; break }
                    $block += $cur
                    $j++
                    if ($j -ge $lines.Count) { break }
                    $cur = ([string]$lines[$j]).Trim()
                }
                $syn = Get-Synopsis $block
                if ($syn) { return $syn }
                if (-not $closed) { return $null }
                $i = $j
                continue
            }
            if ($i -ge 8) { break }
            if ($t.StartsWith('#') -and -not $t.StartsWith('#Requires') -and -not $t.StartsWith('#!') -and $t -notmatch '^#\s*Shelly-\w+\s*:') {
                $d = $t.TrimStart('#').Trim()
                if ($d.StartsWith('<')) { $d = $d.TrimStart('<').Trim() }
                $d = ($d -replace '^(?i)(description|описание)\s*:\s*', '').Trim()
                if ($d) { return $d }
            }
            if (-not $t.StartsWith('#')) { break }
        }
    } catch {}
    return $null
}

# ---------- Safety scan ----------
# A static guardrail on PowerShell's AST: commands are resolved through aliases (built-in and Set-Alias/New-Alias in the
# script), module-qualified names, splatting and constant variables; code built at run time is reported as such, since
# what it runs cannot be known before it runs. Severities: high (danger), medium (warning), low (info).
$script:dzDlCmds = @('invoke-webrequest', 'invoke-restmethod', 'start-bitstransfer', 'curl', 'wget', 'bitsadmin', 'certutil')
$script:dzDlMembers = @('downloadstring', 'downloaddata', 'downloadfile', 'downloadstringtaskasync', 'downloaddatataskasync',
                        'downloadfiletaskasync', 'openread', 'getstringasync', 'getbytearrayasync', 'getstreamasync')
$script:dzRunners = @('start-process', 'invoke-item', 'msiexec', 'cmd', 'powershell', 'pwsh', 'wscript', 'cscript', 'mshta',
                      'rundll32', 'regsvr32', 'start-job', 'invoke-command')
$script:dzExecExt = '(?i)\.(ps1|psm1|exe|com|bat|cmd|msi|msix|appx|vbs|vbe|js|jse|wsf|hta|scr|cpl)$'
$script:dzTempPath = '(?i)^(\$env:(temp|tmp)|\$\{env:(temp|tmp)\}|\$\(\[(system\.)?io\.path\]::gettemppath\(\)\)|[a-z]:[\\/]users[\\/][^\\/]+[\\/]appdata[\\/]local[\\/]temp|\$env:localappdata[\\/]temp)[\\/]+[^\\/*\s]'
$script:dzSwitches = @('recurse', 'force', 'passthru', 'ignorewarnings', 'usebasicparsing', 'wait', 'nonewwindow', 'whatif',
                       'confirm', 'verbose', 'debug', 'asjob', 'noprofile', 'noninteractive', 'nologo', 'quiet', 'raw')

function Get-DzLeaf([string]$name) {
    $n = ([string]$name).Trim().Trim([char]34, [char]39).ToLower()
    $i = $n.LastIndexOfAny([char[]]@([char]92, [char]47))
    if ($i -ge 0) { $n = $n.Substring($i + 1) }
    return $n
}

# The assignment of a variable that is in effect at a place in the script: the last one before it in source order
# (in code scanned from a string: the last one of the script)
function Get-DzAssign([string]$name, [int]$off) {
    $list = $script:dzAssign[$name.ToLower()]
    if (-not $list) { return $null }
    if ($script:dzNested -gt 0) { $off = [int]::MaxValue }
    $best = $null
    foreach ($a in $list) { if ($a.Off -lt $off) { $best = $a } }
    return $best
}

function Get-DzVar($v) {
    $up = [string]$v.VariablePath.UserPath
    $n = $up.ToLower()
    $a = Get-DzAssign $n $v.Extent.StartOffset
    if ($a -and $a.Kind -eq 'const') { return $a.Value }
    if ($n.StartsWith('env:')) { return '$' + $up }
    if ($n -eq 'psscriptroot' -or $n -eq 'pwd' -or $n -eq 'home') { return '$' + $up }
    return $null
}

# The value of an expression when it is made of constants and known variables ('a'+'b', '{0}{1}' -f ..., -join, "$x\y")
function Get-DzValue($e, [int]$d = 0) {
    if ($null -eq $e -or $d -gt 10) { return $null }
    if ($e -is [System.Management.Automation.Language.StringConstantExpressionAst]) { return [string]$e.Value }
    if ($e -is [System.Management.Automation.Language.ConstantExpressionAst]) { return [string]$e.Value }
    if ($e -is [System.Management.Automation.Language.ExpandableStringExpressionAst]) {
        $s = [string]$e.Value
        foreach ($n in @($e.NestedExpressions)) {
            if ($n -is [System.Management.Automation.Language.VariableExpressionAst]) {
                $vv = Get-DzVar $n
                if ($null -ne $vv) { $s = $s.Replace([string]$n.Extent.Text, [string]$vv) }
            }
        }
        return $s
    }
    if ($e -is [System.Management.Automation.Language.VariableExpressionAst]) { return (Get-DzVar $e) }
    if ($e -is [System.Management.Automation.Language.ParenExpressionAst]) { return (Get-DzValue $e.Pipeline ($d + 1)) }
    if ($e -is [System.Management.Automation.Language.PipelineAst]) {
        if ($e.PipelineElements.Count -eq 1) { return (Get-DzValue $e.PipelineElements[0] ($d + 1)) }
        return $null
    }
    if ($e -is [System.Management.Automation.Language.CommandExpressionAst]) { return (Get-DzValue $e.Expression ($d + 1)) }
    if ($e -is [System.Management.Automation.Language.AttributedExpressionAst]) { return (Get-DzValue $e.Child ($d + 1)) }
    if ($e -is [System.Management.Automation.Language.BinaryExpressionAst]) {
        $op = [string]$e.Operator
        if ($op -eq 'Plus') {
            $a = Get-DzValue $e.Left ($d + 1); $b = Get-DzValue $e.Right ($d + 1)
            if ($null -ne $a -and $null -ne $b) { return ([string]$a + [string]$b) }
        } elseif ($op -eq 'Format') {
            $f = Get-DzValue $e.Left ($d + 1); $vals = Get-DzValues $e.Right ($d + 1)
            if ($null -ne $f -and $null -ne $vals) { try { return ([string]$f -f [object[]]$vals) } catch {} }
        } elseif ($op -eq 'Join') {
            $vals = Get-DzValues $e.Left ($d + 1); $sep = Get-DzValue $e.Right ($d + 1)
            if ($null -ne $vals -and $null -ne $sep) { return ($vals -join [string]$sep) }
        } elseif ($op -match '^[ic]?replace$') {
            $l = Get-DzValue $e.Left ($d + 1); $vals = Get-DzValues $e.Right ($d + 1)
            if ($null -ne $l -and $null -ne $vals -and $vals.Count -ge 1) {
                $rep = ''; if ($vals.Count -ge 2) { $rep = $vals[1] }
                # The pattern comes from the script: it runs with a time limit, so a catastrophic one cannot hang the analysis
                # (the value is just not folded)
                if ($script:dzRxTimeouts -lt 3) {
                    try {
                        $ro = [System.Text.RegularExpressions.RegexOptions]::CultureInvariant
                        if ($op -notmatch '^c') { $ro = $ro -bor [System.Text.RegularExpressions.RegexOptions]::IgnoreCase }
                        $rx = [regex]::new([string]$vals[0], $ro, [TimeSpan]::FromMilliseconds(100))
                        return $rx.Replace([string]$l, [string]$rep)
                    } catch [System.Text.RegularExpressions.RegexMatchTimeoutException] { $script:dzRxTimeouts++ } catch {}
                }
            }
        }
        return $null
    }
    if ($e -is [System.Management.Automation.Language.UnaryExpressionAst] -and [string]$e.TokenKind -eq 'Join') {
        $vals = Get-DzValues $e.Child ($d + 1)
        if ($null -ne $vals) { return ($vals -join '') }
    }
    return $null
}

function Get-DzValues($e, [int]$d = 0) {
    if ($e -is [System.Management.Automation.Language.ParenExpressionAst]) {
        $p = $e.Pipeline
        if ($p -is [System.Management.Automation.Language.PipelineAst] -and $p.PipelineElements.Count -eq 1 -and $p.PipelineElements[0] -is [System.Management.Automation.Language.CommandExpressionAst]) {
            return (Get-DzValues $p.PipelineElements[0].Expression ($d + 1))
        }
        return $null
    }
    if ($e -is [System.Management.Automation.Language.ArrayLiteralAst]) {
        $r = New-Object System.Collections.ArrayList
        foreach ($x in $e.Elements) { $v = Get-DzValue $x ($d + 1); if ($null -eq $v) { return $null }; [void]$r.Add([string]$v) }
        return , ([string[]]$r.ToArray())
    }
    $v = Get-DzValue $e ($d + 1)
    if ($null -eq $v) { return $null }
    return , ([string[]]@([string]$v))
}

function Get-DzArgText($e) {
    $v = Get-DzValue $e
    if ($null -ne $v) { return [string]$v }
    return [string]$e.Extent.Text
}

# Command name -> canonical lower-case name: path and module prefix dropped, script aliases (chains) and built-in aliases
# resolved. A name that ends in .exe/.com is the native program even when an alias of the same name exists.
function Resolve-DzName([string]$name, [bool]$native, $argList) {
    $leaf = Get-DzLeaf $name
    $isExe = $leaf -match '\.(exe|com)$'
    $leaf = $leaf -replace '\.(exe|com|cmd|bat)$', ''
    if ($isExe) { return $leaf }
    if ($native) {
        # Text run by cmd.exe or Start-Process: cmd's own del/erase/rd/rmdir delete files too
        if (@('del', 'erase', 'rd', 'rmdir') -contains $leaf) { return 'remove-item' }
        return $leaf
    }
    for ($k = 0; $k -lt 10 -and $script:dzUserAliases.ContainsKey($leaf); $k++) {
        $leaf = (Get-DzLeaf $script:dzUserAliases[$leaf]) -replace '\.(exe|com|cmd|bat)$', ''
    }
    if ($leaf -eq 'sc') {
        # 'sc' is Set-Content in 5.1 and sc.exe in 7: a service-control subcommand decides
        $first = ''
        if (@($argList).Count -gt 0) { $first = ([string]@($argList)[0]).ToLower() }
        if (@('delete', 'stop', 'config', 'create', 'pause', 'failure', 'sdset', 'start', 'query', 'qc') -contains $first) { return 'sc' }
        return 'set-content'
    }
    if ($script:dzAliases.ContainsKey($leaf)) { return $script:dzAliases[$leaf] }
    return $leaf
}

# Seq: the arguments in their original order, parameters as '-name' (Params and Args lose the order)
function New-DzCommand([string]$name, $params, $argList, $pos, [string]$text, [bool]$native, [bool]$unknownSplat, $seq = $null) {
    return [pscustomobject]@{
        Name = (Resolve-DzName $name $native $argList); Display = ([string]$name).Trim(); Params = $params; Args = @($argList)
        Pos = @($pos); Text = $text; UnknownSplat = $unknownSplat; Seq = @($seq)
    }
}

function Split-CommandAst($cmd, [string]$nameOverride) {
    $name = $nameOverride
    if (-not $name) { $name = $cmd.GetCommandName() }
    if (-not $name) { return $null }
    $els = $cmd.CommandElements
    $params = @{}
    $argList = New-Object System.Collections.ArrayList
    $pos = New-Object System.Collections.ArrayList
    $seq = New-Object System.Collections.ArrayList
    $unknownSplat = $false
    $afterParam = $false
    for ($i = 1; $i -lt $els.Count; $i++) {
        $e = $els[$i]
        if ($e -is [System.Management.Automation.Language.CommandParameterAst]) {
            # Without type information a switch cannot be told from a parameter with a value: the next element is taken
            # as the value and kept among the arguments as well
            $pn = $e.ParameterName.ToLower()
            $val = '$true'
            [void]$seq.Add('-' + $e.ParameterName)
            if ($e.Argument) { $val = Get-DzArgText $e.Argument; $afterParam = $false; [void]$seq.Add($val) }
            else {
                if ($i + 1 -lt $els.Count -and -not ($els[$i + 1] -is [System.Management.Automation.Language.CommandParameterAst])) { $val = Get-DzArgText $els[$i + 1] }
                $afterParam = -not ($script:dzSwitches -contains $pn)
            }
            $params[$pn] = $val
        } elseif ($e -is [System.Management.Automation.Language.VariableExpressionAst] -and $e.Splatted) {
            $vn = $e.VariablePath.UserPath.ToLower()
            $ha = Get-DzAssign $vn $e.Extent.StartOffset
            if ($ha -and $ha.Kind -eq 'hash') {
                foreach ($k in $ha.Value.Keys) { $params[$k] = $ha.Value[$k] }
            } else { $unknownSplat = $true }
            $afterParam = $false
        } else {
            $t = Get-DzArgText $e
            [void]$argList.Add($t)
            [void]$seq.Add($t)
            if (-not $afterParam) { [void]$pos.Add($t) }
            $afterParam = $false
        }
    }
    return (New-DzCommand $name $params $argList $pos $cmd.Extent.Text $false $unknownSplat $seq)
}

function Split-DzText([string]$text) {
    if (-not $text) { return @() }
    @([regex]::Matches($text, '"[^"]*"|''[^'']*''|[^\s"'',]+') | ForEach-Object { $_.Value.Trim([char]34, [char]39) } | Where-Object { $_ })
}

function New-DzFromTokens($tokens, [string]$text) {
    $tokens = @($tokens)
    if ($tokens.Count -eq 0) { return $null }
    $params = @{}
    $argList = New-Object System.Collections.ArrayList
    for ($i = 1; $i -lt $tokens.Count; $i++) {
        $tk = [string]$tokens[$i]
        if ($tk.StartsWith('-') -and $tk.Length -gt 1) {
            $val = '$true'
            if ($i + 1 -lt $tokens.Count -and -not ([string]$tokens[$i + 1]).StartsWith('-')) { $val = [string]$tokens[$i + 1] }
            $params[$tk.Substring(1).TrimEnd(':').ToLower()] = $val
        } else { [void]$argList.Add($tk) }
    }
    return (New-DzCommand ([string]$tokens[0]) $params $argList $argList $text $true $false @($tokens | Select-Object -Skip 1))
}

# A download command; certutil only with -urlcache / -verifyctl (it is mostly a certificate and hash tool)
function Test-DzDl($c) {
    if (-not ($script:dzDlCmds -contains $c.Name)) { return $false }
    if ($c.Name -ne 'certutil') { return $true }
    return ($c.Params.ContainsKey('urlcache') -or $c.Params.ContainsKey('verifyctl') -or @($c.Args | Where-Object { ([string]$_) -match '^[-/](urlcache|verifyctl)$' }).Count -gt 0)
}

function Get-DzParam($c, [string]$full, [int]$min = 1) {
    foreach ($k in $c.Params.Keys) { if ($k.Length -ge $min -and $full.StartsWith($k)) { return $c.Params[$k] } }
    return $null
}

function Test-DzOn($v) { return ($null -ne $v -and ([string]$v) -notmatch '^\$?(false|0)$') }

function Get-DzTokens($c) {
    $t = @($c.Args | ForEach-Object { ([string]$_).ToLower() })
    foreach ($k in $c.Params.Keys) { $t += ('/' + $k) }
    return $t
}

function Get-DzServiceSev([string]$names) {
    $sev = ''
    foreach ($m in [regex]::Matches($names.ToLower(), '[a-z0-9_.-]+')) {
        if ($script:dzSecServices -contains $m.Value) { return 'high' }
        if ($script:dzCoreServices -contains $m.Value) { $sev = 'medium' }
    }
    return $sev
}

function Add-Danger([string]$rule, [string]$sev, [int]$line, [string]$detail) {
    foreach ($f in $script:dzCur) {
        if ($f.Rule -eq $rule -and $f.Line -eq $line) {
            # One finding per rule and line: the more severe one stays
            if ($sev -eq 'high' -and $f.Sev -ne 'high') { $f.Sev = 'high'; $f.Detail = $detail }
            elseif ($sev -eq 'medium' -and $f.Sev -eq 'low') { $f.Sev = 'medium'; $f.Detail = $detail }
            return
        }
    }
    $code = ''
    if ($line -ge 1 -and $line -le $script:dzLines.Count) { $code = ([string]$script:dzLines[$line - 1]).Trim() }
    if ($code.Length -gt 160) { $code = $code.Substring(0, 157) + '...' }
    if ($detail.Length -gt 120) { $detail = $detail.Substring(0, 117) + '...' }
    [void]$script:dzCur.Add([pscustomobject]@{ Rule = $rule; Sev = $sev; Line = $line; Code = $code; Detail = $detail })
}

function Test-DzSystemPath($paths) {
    foreach ($p in @($paths)) { $t = ([string]$p).Trim().Trim([char]34, [char]39); if ($t -and $t -ne '$true' -and $t -match $script:dzSystemPath) { return $t } }
    return $null
}

function Test-DzTempPath([string]$p) { return ([string]$p).Trim().Trim([char]34, [char]39) -match $script:dzTempPath }

# ---------- Downloads ----------
function Get-DzUrlOf($c) {
    $u = Get-DzParam $c 'uri'
    if (-not $u -or $u -eq '$true') { $u = Get-DzParam $c 'source' 2 }
    if (-not $u -or $u -eq '$true') { $u = @($c.Args | Where-Object { ([string]$_) -match '^(?i)(https?|ftp)://' }) | Select-Object -First 1 }
    if (-not $u -or $u -eq '$true') { $u = @($c.Pos | Where-Object { ([string]$_) -notmatch '^/' }) | Select-Object -First 1 }
    if (-not $u) { return '?' }
    return ([string]$u).Trim().Trim([char]34, [char]39)
}

# The URL when the code under $ast downloads something (a download command or call, or a variable holding a download)
function Get-DzDownloadUrl($ast) {
    if (-not $ast) { return $null }
    foreach ($x in @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.CommandAst] -or $a -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -or $a -is [System.Management.Automation.Language.VariableExpressionAst] }, $true))) {
        if ($x -is [System.Management.Automation.Language.CommandAst]) {
            $c = $null
            try { $c = Split-CommandAst $x '' } catch {}
            if ($c -and (Test-DzDl $c)) { return (Get-DzUrlOf $c) }
        } elseif ($x -is [System.Management.Automation.Language.InvokeMemberExpressionAst]) {
            $mn = ''
            try { $mn = ([string]$x.Member.Value).ToLower() } catch {}
            if ($script:dzDlMembers -contains $mn) {
                if ($x.Arguments -and $x.Arguments.Count -gt 0) { return (Get-DzArgText $x.Arguments[0]).Trim([char]34, [char]39) }
                return '?'
            }
        } else {
            $vn = ([string]$x.VariablePath.UserPath).ToLower()
            if ($script:dzDlVars.ContainsKey($vn)) { return $script:dzDlVars[$vn] }
        }
    }
    return $null
}

function Test-DzDynamicOrigin($ast) {
    if (-not $ast) { return $false }
    foreach ($m in @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.InvokeMemberExpressionAst] }, $true))) {
        $mn = ''
        try { $mn = ([string]$m.Member.Value).ToLower() } catch {}
        if ($mn -eq 'create' -and $m.Expression.Extent.Text -match '(?i)scriptblock\]$') { return $true }
        if (@('newscriptblock', 'getmethod', 'getmethods', 'getmember', 'getdelegateforfunctionpointer', 'createdelegate') -contains $mn) { return $true }
    }
    return $false
}

# Variables, aliases, splatting tables and downloaded files of the whole script (flow-insensitive, in source order)
function Initialize-DzContext($ast) {
    $script:dzAssign = @{}; $script:dzDynVars = @{}; $script:dzDlVars = @{}; $script:dzNested = 0; $script:dzScriptParams = @{}
    if ($ast.ParamBlock) { foreach ($p in $ast.ParamBlock.Parameters) { $script:dzScriptParams[([string]$p.Name.VariablePath.UserPath).ToLower()] = $true } }
    $script:dzUserAliases = @{}; $script:dzDlFiles = @{}; $script:dzParamNames = @{}; $script:dzSbParams = @{}
    foreach ($p in @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.ParameterAst] }, $true))) {
        $pn = ([string]$p.Name.VariablePath.UserPath).ToLower()
        $script:dzParamNames[$pn] = $true
        # A script block given to the script from outside is code Shelly never sees; one given to a function by the script is
        $isSb = $p.StaticType -and $p.StaticType.Name -eq 'ScriptBlock'
        if ($isSb -and $script:dzScriptParams.ContainsKey($pn)) { $script:dzDynVars[$pn] = $true }
        if ($isSb -and -not $script:dzScriptParams.ContainsKey($pn)) { $script:dzSbParams[$pn] = $true }
    }
    # Aliases first (Set-Alias/New-Alias with -Name/-Value or positional)
    foreach ($cmd in @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.CommandAst] }, $true))) {
        $n = [string]$cmd.GetCommandName()
        if (-not $n) { continue }
        $leaf = Get-DzLeaf $n
        if ($script:dzAliases.ContainsKey($leaf)) { $leaf = $script:dzAliases[$leaf] }
        if ($leaf -ne 'set-alias' -and $leaf -ne 'new-alias') { continue }
        $c = $null
        try { $c = Split-CommandAst $cmd '' } catch {}
        if (-not $c) { continue }
        $an = Get-DzParam $c 'name'; $av = Get-DzParam $c 'value'
        $pos = @($c.Pos)
        if (-not $an -or $an -eq '$true') { if ($pos.Count -gt 0) { $an = $pos[0]; $pos = @($pos | Select-Object -Skip 1) } }
        if (-not $av -or $av -eq '$true') { if ($pos.Count -gt 0) { $av = $pos[0] } }
        if ($an -and $av -and $an -ne '$true' -and $av -ne '$true') { $script:dzUserAliases[(Get-DzLeaf $an)] = [string]$av }
    }
    $assigns = @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true)) | Sort-Object { $_.Extent.StartOffset }
    foreach ($a in @($assigns)) {
        if ([string]$a.Operator -ne 'Equals') { continue }
        $left = $a.Left
        while ($left -is [System.Management.Automation.Language.AttributedExpressionAst]) { $left = $left.Child }
        if (-not ($left -is [System.Management.Automation.Language.VariableExpressionAst])) { continue }
        $n = ([string]$left.VariablePath.UserPath).ToLower()
        $right = $a.Right
        $expr = $null
        if ($right -is [System.Management.Automation.Language.PipelineAst] -and $right.PipelineElements.Count -eq 1 -and $right.PipelineElements[0] -is [System.Management.Automation.Language.CommandExpressionAst]) { $expr = $right.PipelineElements[0].Expression }
        elseif ($right -is [System.Management.Automation.Language.CommandExpressionAst]) { $expr = $right.Expression }
        $entry = [pscustomobject]@{ Off = $a.Extent.StartOffset; Kind = 'other'; Value = $null }
        if ($expr -is [System.Management.Automation.Language.ScriptBlockExpressionAst]) { $entry.Kind = 'sb' }
        elseif ($expr -is [System.Management.Automation.Language.HashtableAst]) {
            $h = @{}
            foreach ($kv in $expr.KeyValuePairs) {
                $k = Get-DzValue $kv.Item1
                if ($null -eq $k) { continue }
                $val = Get-DzValue $kv.Item2
                if ($null -eq $val) { $val = [string]$kv.Item2.Extent.Text }
                $h[([string]$k).ToLower()] = [string]$val
            }
            $entry.Kind = 'hash'; $entry.Value = $h
        } elseif ($expr) {
            $v = Get-DzValue $expr
            if ($null -ne $v) { $entry.Kind = 'const'; $entry.Value = [string]$v }
        }
        if (-not $script:dzAssign.ContainsKey($n)) { $script:dzAssign[$n] = New-Object System.Collections.ArrayList }
        [void]$script:dzAssign[$n].Add($entry)
        $dl = Get-DzDownloadUrl $right
        if ($null -ne $dl) { $script:dzDlVars[$n] = $dl }
        if (Test-DzDynamicOrigin $right) { $script:dzDynVars[$n] = $true }
    }
    # Files a download writes (-OutFile, Start-BitsTransfer -Destination, curl -o, WebClient.DownloadFile)
    foreach ($x in @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.CommandAst] -or $a -is [System.Management.Automation.Language.InvokeMemberExpressionAst] }, $true))) {
        $file = $null; $url = $null
        if ($x -is [System.Management.Automation.Language.CommandAst]) {
            $c = $null
            try { $c = Split-CommandAst $x '' } catch {}
            if (-not $c -or -not (Test-DzDl $c)) { continue }
            $url = Get-DzUrlOf $c
            $file = Get-DzParam $c 'outfile' 2
            if (-not $file -or $file -eq '$true') { $file = Get-DzParam $c 'destination' 2 }
            if ((-not $file -or $file -eq '$true') -and $c.Name -eq 'start-bitstransfer' -and @($c.Pos).Count -ge 2) { $file = @($c.Pos)[1] }
            if ((-not $file -or $file -eq '$true')) { $file = Get-DzParam $c 'output' 1 }
            if ((-not $file -or $file -eq '$true') -and ($c.Name -eq 'bitsadmin' -or $c.Name -eq 'certutil')) { $file = @($c.Args)[-1] }
        } else {
            $mn = ''
            try { $mn = ([string]$x.Member.Value).ToLower() } catch {}
            if ($mn -notlike 'downloadfile*' -or -not $x.Arguments -or $x.Arguments.Count -lt 2) { continue }
            $url = (Get-DzArgText $x.Arguments[0]).Trim([char]34, [char]39)
            $file = Get-DzArgText $x.Arguments[1]
        }
        if ($file -and $file -ne '$true') {
            $leaf = Get-DzLeaf $file
            if ($leaf -match $script:dzExecExt) { $script:dzDlFiles[$leaf] = $url }
        }
    }
}

# The URL when one of these words is a file the script downloaded
function Get-DzDownloadedRun($words) {
    if ($script:dzDlFiles.Count -eq 0) { return $null }
    foreach ($w in @($words)) {
        if (-not $w) { continue }
        foreach ($part in @(Split-DzText ([string]$w))) {
            $leaf = Get-DzLeaf $part
            if ($script:dzDlFiles.ContainsKey($leaf)) { return $script:dzDlFiles[$leaf] }
        }
    }
    return $null
}

# The variable holds a script block written in the script (or is a [scriptblock] parameter of one of its functions)
function Test-DzSbVar([string]$name, [int]$off) {
    $n = $name.ToLower()
    if ($script:dzSbParams.ContainsKey($n)) { return $true }
    $a = Get-DzAssign $n $off
    return ($a -and $a.Kind -eq 'sb')
}

# A failure inside the scan is not "nothing found": it is counted and reported as a finding (see Get-DangerFindings)
function Add-DzError($err) {
    $script:dzErrCount++
    if (-not $script:dzErrFirst) { $script:dzErrFirst = [string]$err.Exception.Message }
}

# Code nested deeper than the scan goes (cmd /c cmd /c ...) is not read: that is said, not skipped silently
function Add-DzTooDeep([int]$line) { Add-Danger 'Nested' 'medium' $line 'nested too deep' }

function Add-DzDynamic([int]$line, [string]$detail) { Add-Danger 'Dynamic' 'high' $line $detail }

function Add-DzDlExec([int]$line, [string]$url) { Add-Danger 'DlExec' 'high' $line $url }

# What powershell / pwsh gets to run from its command line: everything after -Command, or, without it, everything from the
# first argument that is not a parameter (the words are joined with spaces, as PowerShell does). $null for -File and
# -EncodedCommand, or when there is no code.
function Get-DzPsCommand($c) {
    $seq = @($c.Seq)
    $withValue = @('executionpolicy', 'ep', 'inputformat', 'if', 'outputformat', 'of', 'windowstyle', 'w', 'version', 'v',
                   'configurationname', 'config', 'workingdirectory', 'wd', 'settingsfile', 'custompipename')
    for ($i = 0; $i -lt $seq.Count; $i++) {
        $t = [string]$seq[$i]
        if ($t.Length -gt 1 -and $t.StartsWith('-')) {
            $k = $t.Substring(1).TrimEnd(':').ToLower()
            if ($k -eq 'f' -or $k -eq 'e' -or $k -eq 'ec' -or 'file'.StartsWith($k) -or 'encodedcommand'.StartsWith($k)) { return $null }
            if ('command'.StartsWith($k)) {
                if ($i + 1 -ge $seq.Count) { return $null }
                return (@($seq[($i + 1)..($seq.Count - 1)]) -join ' ')
            }
            if ($withValue -contains $k) { $i++ }
            continue
        }
        return (@($seq[$i..($seq.Count - 1)]) -join ' ')
    }
    return $null
}

function Test-DzRunKey($c) {
    $hay = [string]$c.Text + ' ' + (@($c.Args) -join ' ') + ' ' + (@($c.Params.Values) -join ' ')
    return ($hay -match '(?i)\\CurrentVersion\\Run(Once|Services|ServicesOnce)?(\\|[''"\s]|$)')
}

function Test-DzCommand($c, [int]$line, [string]$pre, [int]$depth) {
    if (-not $c) { return }
    if ($depth -gt 3) { Add-DzTooDeep $line; return }
    $n = $c.Name
    $tok = @(Get-DzTokens $c)
    $joined = ' ' + ($tok -join ' ') + ' '

    # A file the script downloaded is started
    if ($script:dzRunners -contains $n -or $c.Display -match $script:dzExecExt) {
        $u = Get-DzDownloadedRun (@($c.Display) + @($c.Args) + @($c.Params.Values))
        if ($null -ne $u) { Add-DzDlExec $line $u }
    }
    if ($c.Display -match '(?i)\.psm?1$' -and $n -match '\.psm?1$') { Add-Danger 'ScriptCall' 'low' $line $c.Display; return }

    # Programs that fetch and run remote code themselves: mshta / regsvr32 scriptlets and msiexec packages from a URL
    if (@('mshta', 'regsvr32', 'msiexec') -contains $n) {
        foreach ($x in (@($c.Display) + @($c.Args) + @($c.Params.Values))) {
            if (([string]$x) -match '(?i)\b(https?|ftp)://[^\s''"]+') { Add-DzDlExec $line $Matches[0]; break }
        }
        return
    }

    if ($n -eq 'remove-item') {
        $paths = @($c.Args) + @(Get-DzParam $c 'path') + @(Get-DzParam $c 'literalpath' 2)
        $paths = @($paths | Where-Object { $_ -and $_ -ne '$true' })
        $sys = Test-DzSystemPath $paths
        if ($sys) { Add-Danger 'DelSys' 'high' $line $sys; return }
        $rec = (Test-DzOn (Get-DzParam $c 'recurse')) -or $tok -contains '/s'
        $flags = @()
        if ($rec) { $flags += '-Recurse' }
        if ((Test-DzOn (Get-DzParam $c 'force' 2)) -or $tok -contains '/q' -or $tok -contains '/f') { $flags += '-Force' }
        $wild = @($paths | Where-Object { ([string]$_) -match '\*' }) | Select-Object -First 1
        if ($wild) { $flags += [string]$wild }
        if ($c.UnknownSplat) { Add-Danger 'Del' 'medium' $line 'parameters hidden in a splatted variable'; return }
        if ($flags.Count -eq 0) { return }
        $allTemp = $paths.Count -gt 0 -and @($paths | Where-Object { -not (Test-DzTempPath $_) }).Count -eq 0
        # -Force alone (hidden and read-only items) on named items, or anything below the temp folder without -Recurse: info
        if ((-not $rec -and -not $wild) -or ($allTemp -and -not $rec)) { Add-Danger 'Del' 'low' $line ($flags -join ', ') }
        else { Add-Danger 'Del' 'medium' $line ($flags -join ', ') }
        return
    }
    if (@('format-volume', 'clear-disk', 'initialize-disk', 'remove-partition', 'diskpart', 'format') -contains $n) {
        Add-Danger 'Disk' 'high' $line $c.Display; return
    }
    if ($n -eq 'bcdedit') {
        if ($joined -match ' /(set|delete|deletevalue|import|create|copy|default|bootsequence|displayorder|timeout|bootems|ems|debug|bootdebug|hypervisorsettings|clean|mirror|store)\b') {
            Add-Danger 'Boot' 'high' $line 'bcdedit'
        }
        return
    }
    if (@('stop-computer', 'restart-computer') -contains $n) { Add-Danger 'Power' 'high' $line $c.Display; return }
    if ($n -eq 'shutdown' -and $joined -match ' /(s|r|p|g|sg|h|fw)\b') { Add-Danger 'Power' 'high' $line 'shutdown'; return }

    if (@('set-itemproperty', 'new-itemproperty', 'remove-itemproperty', 'rename-itemproperty', 'clear-itemproperty', 'new-item', 'set-item') -contains $n) {
        # A value in a Run key starts a program at every sign-in (HKCU needs no administrator)
        if (@('set-itemproperty', 'new-itemproperty') -contains $n -and (Test-DzRunKey $c)) { Add-Danger 'Autorun' 'medium' $line $c.Display }
        $hk = $c.Text -match '(?i)HKLM:|HKEY_LOCAL_MACHINE' -or (@(@($c.Args) + @($c.Params.Values)) -match '(?i)^HKLM:|HKEY_LOCAL_MACHINE').Count -gt 0
        if (-not $hk -and $script:dzHasHklm -and $n -like '*-itemproperty') {
            # The path is in a variable and the script mentions HKLM somewhere
            $hk = @(@($c.Args) + @(Get-DzParam $c 'path') | Where-Object { ([string]$_).StartsWith('$') }).Count -gt 0
        }
        if ($hk) { Add-Danger 'RegWrite' 'medium' $line $c.Display }
        return
    }
    if ($n -eq 'reg') {
        $sub = ''
        if ($c.Args.Count -gt 0) { $sub = ([string]$c.Args[0]).ToLower() }
        if ($sub -eq 'add' -and (Test-DzRunKey $c)) { Add-Danger 'Autorun' 'medium' $line 'reg add' }
        if ($sub -eq 'delete' -and $joined -match ' (hklm|hkey_local_machine)') { Add-Danger 'RegDel' 'high' $line 'reg delete' }
        elseif (@('add', 'delete', 'import', 'restore', 'load', 'unload', 'copy') -contains $sub) { Add-Danger 'RegWrite' 'medium' $line ('reg ' + $sub) }
        return
    }
    if ($n -eq 'set-executionpolicy') {
        $pol = @(@(Get-DzParam $c 'executionpolicy') + @($c.Args) | Where-Object { ([string]$_) -match '^(unrestricted|bypass|remotesigned|allsigned|restricted|default|undefined)$' }) | Select-Object -First 1
        if ([string]$pol -match '^(restricted|allsigned|default|undefined)$') { return }   # making it stricter is fine
        $scope = [string](Get-DzParam $c 'scope')
        $detail = (@([string]$pol, $(if ($scope) { '-Scope ' + $scope } else { '' })) | Where-Object { $_ }) -join ', '
        # Only for this PowerShell session, or RemoteSigned (the usual setting): medium; Unrestricted/Bypass for good: high
        if ($scope -match '^process' -or [string]$pol -match '^remotesigned$') { Add-Danger 'ExecPolicy' 'medium' $line $detail }
        else { Add-Danger 'ExecPolicy' 'high' $line $detail }
        return
    }
    if (@('set-mppreference', 'add-mppreference') -contains $n) {
        $bad = @()
        foreach ($k in $c.Params.Keys) {
            if ($k -like 'exclusion*' -or ($k -like 'disable*' -and (Test-DzOn $c.Params[$k]))) { $bad += ('-' + $k) }
        }
        if ($bad.Count -gt 0) { Add-Danger 'Defender' 'high' $line ($bad -join ', ') }
        elseif ($c.UnknownSplat) { Add-Danger 'Defender' 'medium' $line 'parameters hidden in a splatted variable' }
        return
    }
    if ($n -eq 'netsh') {
        if ($joined -match 'advfirewall .*\bstate +off' -or $joined -match ' firewall .*opmode +(mode=)?disable') { Add-Danger 'Firewall' 'high' $line 'netsh' }
        elseif ($joined -match ' interface .*\bdisable') { Add-Danger 'NetAdapter' 'medium' $line 'netsh interface' }
        return
    }
    if ($n -eq 'set-netfirewallprofile') {
        if ([string](Get-DzParam $c 'enabled' 2) -match '^\$?(false|0)$') { Add-Danger 'Firewall' 'high' $line 'Set-NetFirewallProfile -Enabled False' }
        return
    }
    if ($n -eq 'disable-netadapter') { Add-Danger 'NetAdapter' 'medium' $line $c.Display; return }

    if ($n -eq 'stop-service' -or ($n -eq 'set-service' -and $joined -match ' (disabled|manual|stopped) ')) {
        $names = (@($c.Args) + @(Get-DzParam $c 'name')) -join ' '
        $sev = Get-DzServiceSev $names
        if ($sev) { Add-Danger 'Service' $sev $line ($c.Display + ' ' + (@($c.Args) -join ' ')).Trim() }
        return
    }
    if ($n -eq 'remove-service') { Add-Danger 'SvcDelete' 'high' $line ('Remove-Service ' + (@($c.Args) -join ' ')).Trim(); return }
    if ($n -eq 'sc' -or $n -eq 'net' -or $n -eq 'net1') {
        $sub = ''; $rest = ''
        if ($c.Args.Count -gt 0) { $sub = ([string]$c.Args[0]).ToLower() }
        if ($c.Args.Count -gt 1) { $rest = (@($c.Args)[1..($c.Args.Count - 1)]) -join ' ' }
        if ($n -eq 'sc' -and $sub -eq 'delete') { Add-Danger 'SvcDelete' 'high' $line ('sc delete ' + $rest).Trim(); return }
        if (@('stop', 'config', 'pause') -contains $sub) {
            $sev = Get-DzServiceSev $rest
            if ($sev) { Add-Danger 'Service' $sev $line ($n + ' ' + $sub + ' ' + $rest).Trim() }
            return
        }
        if ($n -ne 'sc' -and $sub -eq 'user') {
            if ($joined -match ' /(add|delete|del|active)' -or $c.Args.Count -ge 3) { Add-Danger 'Account' 'high' $line 'net user' }
            return
        }
        if ($n -ne 'sc' -and $sub -eq 'localgroup' -and $joined -match ' /(add|delete|del)' -and $joined -match 'admin|администратор') {
            Add-Danger 'Account' 'high' $line 'net localgroup'
        }
        return
    }
    if ($n -eq 'takeown' -or ($n -eq 'icacls' -and $joined -match ' /(grant|deny|reset|setowner|remove|inheritance|setintegritylevel|restore|substitute)')) {
        $sev = 'medium'
        if (Test-DzSystemPath (@($c.Args) + @($c.Params.Values))) { $sev = 'high' }
        Add-Danger 'Acl' $sev $line $c.Display; return
    }
    if ($n -eq 'set-acl') {
        if (Test-DzSystemPath (@($c.Args) + @(Get-DzParam $c 'path'))) { Add-Danger 'Acl' 'high' $line 'Set-Acl' }
        return
    }
    if ($n -eq 'cipher' -and $joined -match ' /w') { Add-Danger 'Wipe' 'high' $line 'cipher /w'; return }
    if (($n -eq 'vssadmin' -and $joined -match ' (delete|resize) ' -and $joined -match ' shadow') -or
        ($n -eq 'wmic' -and $joined -match 'shadowcopy' -and $joined -match ' delete') -or
        ($n -eq 'wbadmin' -and $joined -match ' delete ')) {
        Add-Danger 'Shadows' 'high' $line $c.Display; return
    }
    if (@('remove-ciminstance', 'remove-wmiobject') -contains $n) {
        if (($pre + ' ' + $c.Text) -match '(?i)shadowcopy') { Add-Danger 'Shadows' 'high' $line $c.Display }
        return
    }
    if (@('invoke-cimmethod', 'invoke-wmimethod') -contains $n) {
        $mname = [string](Get-DzParam $c 'methodname')
        if (-not $mname -or $mname -eq '$true') { $mname = [string](Get-DzParam $c 'name') }
        $all = ($pre + ' ' + $c.Text)
        if ($mname -match '^(?i)(win32shutdown|win32shutdowntracker|shutdown|reboot)$') { Add-Danger 'Power' 'high' $line ($c.Display + ' ' + $mname) }
        elseif ($all -match '(?i)shadowcopy' -and $mname -match '^(?i)delete$') { Add-Danger 'Shadows' 'high' $line $c.Display }
        return
    }
    if (@('clear-eventlog', 'remove-eventlog') -contains $n) { Add-Danger 'Logs' 'high' $line $c.Display; return }
    if ($n -eq 'wevtutil') {
        if ($c.Args.Count -gt 0 -and ([string]$c.Args[0]) -match '^(?i)(cl|clear-log|um|uninstall-manifest|sl|set-log)$') { Add-Danger 'Logs' 'high' $line ('wevtutil ' + $c.Args[0]) }
        return
    }
    if ($n -eq 'schtasks') {
        if ($joined -match ' /(create|change) ') {
            if ($joined -match ' /ru +(system|"?nt authority\\system"?)\b' -or $joined -match ' /rl +highest') { Add-Danger 'Task' 'high' $line 'schtasks' }
            else { Add-Danger 'Task' 'medium' $line 'schtasks' }
        } elseif ($joined -match ' /delete ') { Add-Danger 'Task' 'medium' $line 'schtasks /delete' }
        return
    }
    if ($n -eq 'register-scheduledtask') {
        if ($script:dzFull -match '(?i)-RunLevel\s+Highest|-UserId\s+[''"]?(nt authority\\)?system|-User\s+[''"]?(nt authority\\)?system') { Add-Danger 'Task' 'high' $line $c.Display }
        else { Add-Danger 'Task' 'medium' $line $c.Display }
        return
    }
    if ($n -eq 'invoke-expression') {
        # Reached for nested code (cmd /c, Start-Process) where there is no AST; the AST path handles the rest
        Add-DzDynamic $line 'Invoke-Expression'; return
    }
    if ($n -eq 'add-type') {
        $code = $false
        foreach ($k in $c.Params.Keys) { if ($k.Length -ge 2 -and (@('typedefinition', 'memberdefinition', 'path', 'literalpath') | Where-Object { $_.StartsWith($k) })) { $code = $true } }
        if (@($c.Pos).Count -gt 0) { $code = $true }
        if (-not $code) { return }
        # C# written in the script can be read; source from a variable, a file or a download cannot
        $src = @(@($c.Pos) + @(Get-DzParam $c 'typedefinition' 2) + @(Get-DzParam $c 'memberdefinition' 2) | Where-Object { $_ -and $_ -ne '$true' })
        $path = Get-DzParam $c 'path' 2
        if (-not $path) { $path = Get-DzParam $c 'literalpath' 2 }
        if ($path -or @($src | Where-Object { ([string]$_).StartsWith('$') }).Count -gt 0 -or $src.Count -eq 0) { Add-DzDynamic $line 'Add-Type' }
        else { Add-Danger 'AddType' 'medium' $line 'Add-Type' }
        return
    }
    if (@('remove-localuser', 'new-localuser', 'set-localuser', 'disable-localuser', 'rename-localuser') -contains $n) {
        Add-Danger 'Account' 'high' $line $c.Display; return
    }
    if (@('add-localgroupmember', 'remove-localgroupmember') -contains $n) {
        if ($c.Text -match '(?i)admin|администратор|S-1-5-32-544') { Add-Danger 'Account' 'high' $line $c.Display }
        return
    }
    if ($n -eq 'powershell' -or $n -eq 'pwsh') {
        foreach ($k in $c.Params.Keys) {
            if ($k -eq 'ec' -or 'encodedcommand'.StartsWith($k)) { Add-Danger 'Encoded' 'high' $line ('-' + $k) }
        }
        # Code passed with -Command (or as the first argument) is scanned as well
        $code = Get-DzParam $c 'command'
        if ($code -eq '$true') { $code = $null }
        $explicit = [bool]$code
        $all = Get-DzPsCommand $c
        if ($all) { $code = $all }
        elseif (-not $code -and -not (Get-DzParam $c 'file' 1) -and @($c.Pos).Count -gt 0) { $code = (@($c.Pos) -join ' ') }
        if ($code) {
            if (([string]$code).StartsWith('$') -and ([string]$code) -notmatch '\s') { if ($explicit) { Add-DzDynamic $line ('-Command ' + $code) } }
            else { Invoke-DzText ([string]$code) $line ($depth + 1) }
        }
        $file = Get-DzParam $c 'file' 1
        if ($file -and $file -ne '$true') { Add-Danger 'ScriptCall' 'low' $line ([string]$file) }
        return
    }
    if ($n -eq 'start-process') {
        if ([string]$c.Params['verb'] -match '^runas$') { Add-Danger 'Elevate' 'medium' $line 'Start-Process -Verb RunAs' }
        # Look at the program it starts and its arguments
        $file = Get-DzParam $c 'filepath' 2
        if (-not $file -or $file -eq '$true') { if ($c.Args.Count -gt 0) { $file = $c.Args[0] } }
        $al = Get-DzParam $c 'argumentlist' 2
        if (-not $al -and $c.Args.Count -gt 1) { $al = $c.Args[1] }
        if ($file) {
            $tokens = @([string]$file) + @(Split-DzText ([string]$al))
            Test-DzCommand (New-DzFromTokens $tokens $c.Text) $line '' ($depth + 1)
        }
        return
    }
    if ($n -eq 'cmd') {
        $idx = -1
        for ($i = 0; $i -lt $c.Args.Count; $i++) { if ([string]$c.Args[$i] -match '^/[ck]$') { $idx = $i; break } }
        if ($idx -lt 0 -or $idx + 1 -ge $c.Args.Count) { return }
        $rest = (@($c.Args)[($idx + 1)..($c.Args.Count - 1)]) -join ' '
        foreach ($seg in ($rest -split '&&|\|\||&|\|')) {
            $t = @(Split-DzText $seg)
            if ($t.Count -gt 0) { Test-DzCommand (New-DzFromTokens $t $c.Text) $line '' ($depth + 1) }
        }
        return
    }
}

function Invoke-DzText([string]$code, [int]$line, [int]$depth) {
    if (-not $code) { return }
    if ($depth -gt 3) { Add-DzTooDeep $line; return }
    $tk = $null; $er = $null
    $script:dzNested++
    try {
        $sub = [System.Management.Automation.Language.Parser]::ParseInput($code, [ref]$tk, [ref]$er)
        Invoke-DzScan $sub $line $depth
    } catch { Add-DzError $_ }
    $script:dzNested--
}

# A code-running sink (Invoke-Expression, [scriptblock]::Create, ...) with these arguments: download -> DlExec, else Dynamic;
# a constant argument is scanned as code too
function Test-DzSink($argAsts, $preAsts, [int]$line, [string]$what, [int]$depth) {
    $url = $null
    foreach ($a in @($argAsts) + @($preAsts)) { if ($a -and $null -eq $url) { $url = Get-DzDownloadUrl $a } }
    if ($null -ne $url) { Add-DzDlExec $line $url; return }
    Add-DzDynamic $line $what
    foreach ($a in @($argAsts)) {
        if (-not $a) { continue }
        $v = Get-DzValue $a
        if ($null -ne $v -and -not ([string]$v).StartsWith('$')) { Invoke-DzText ([string]$v) $line ($depth + 1) }
    }
}

function Invoke-DzScan($ast, [int]$lineFix, [int]$depth) {
    foreach ($cmd in @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.CommandAst] }, $true))) {
        $line = $cmd.Extent.StartLineNumber
        if ($lineFix -gt 0) { $line = $lineFix }
        $pre = ''
        $preAsts = @()
        $p = $cmd.Parent
        if ($p -is [System.Management.Automation.Language.PipelineAst]) {
            $idx = $p.PipelineElements.IndexOf($cmd)
            for ($i = 0; $i -lt $idx; $i++) { $pre += ' ' + $p.PipelineElements[$i].Extent.Text; $preAsts += $p.PipelineElements[$i] }
        }
        $els = $cmd.CommandElements
        $first = $els[0]
        $name = $cmd.GetCommandName()
        $c = $null
        try {
            if ($name) {
                $c = Split-CommandAst $cmd ''
            } else {
                # The command is an expression: a script block, a string with a path, a variable or computed text
                if ($first -is [System.Management.Automation.Language.ScriptBlockExpressionAst]) { continue }
                $isVar = $first -is [System.Management.Automation.Language.VariableExpressionAst]
                $vn = ''
                if ($isVar) { $vn = ([string]$first.VariablePath.UserPath).ToLower() }
                if ($isVar -and (Test-DzSbVar $vn $first.Extent.StartOffset)) { continue }
                if ($isVar -and $script:dzParamNames.ContainsKey($vn) -and -not $script:dzScriptParams.ContainsKey($vn) -and $null -eq (Get-DzAssign $vn $first.Extent.StartOffset)) {
                    # A function's own parameter: what it runs is decided by the script's own calls
                    Add-Danger 'Dynamic' 'medium' $line ([string]$first.Extent.Text)
                    continue
                }
                $isText = $first -is [System.Management.Automation.Language.ExpandableStringExpressionAst]
                $folded = Get-DzValue $first
                $url = $null
                if (-not $isText) { $url = Get-DzDownloadUrl $first }
                if ($null -ne $url) { Add-DzDlExec $line $url; continue }
                if ($isText -and $null -ne $folded) {
                    $c = Split-CommandAst $cmd ([string]$folded)
                } elseif ($null -ne $folded -and (-not ([string]$folded).StartsWith('$') -or ($isVar -and ([string]$folded) -match '[\\/]|\.\w+$'))) {
                    $f = [string]$folded
                    # A program path in a variable is common; a command name hidden in a variable or built from pieces is not
                    if (-not $isVar -or $f -notmatch '[\\/.]') { Add-DzDynamic $line ($first.Extent.Text + ' -> ' + $f) }
                    $c = Split-CommandAst $cmd $f
                } else {
                    Add-DzDynamic $line ([string]$first.Extent.Text)
                    continue
                }
            }
        } catch { Add-DzError $_ }
        if (-not $c) { continue }
        try {
            if ($c.Name -eq 'invoke-expression') {
                Test-DzSink @($els | Select-Object -Skip 1) $preAsts $line 'Invoke-Expression' $depth
                continue
            }
            if (@('invoke-command', 'start-job', 'start-threadjob') -contains $c.Name) {
                $sb = $null
                for ($i = 1; $i -lt $els.Count; $i++) {
                    $e = $els[$i]
                    if ($e -is [System.Management.Automation.Language.CommandParameterAst]) {
                        if ($e.ParameterName.Length -ge 2 -and 'scriptblock'.StartsWith($e.ParameterName.ToLower())) {
                            if ($e.Argument) { $sb = $e.Argument } elseif ($i + 1 -lt $els.Count) { $sb = $els[$i + 1] }
                            break
                        }
                        if ($e.ParameterName -match '^(?i)(fi|filepath|f)') { $sb = 'file'; break }
                    } elseif ($i -eq 1) { $sb = $e }
                }
                if ($sb -ne 'file' -and $sb -and -not ($sb -is [System.Management.Automation.Language.ScriptBlockExpressionAst])) {
                    $isSbVar = $sb -is [System.Management.Automation.Language.VariableExpressionAst] -and (Test-DzSbVar ([string]$sb.VariablePath.UserPath) $sb.Extent.StartOffset)
                    if (-not $isSbVar) { Test-DzSink @($sb) @() $line ($c.Display + ' ' + $sb.Extent.Text) $depth }
                }
            }
            Test-DzCommand $c $line $pre $depth
        } catch { Add-DzError $_ }
    }
    # .NET calls
    foreach ($m in @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.InvokeMemberExpressionAst] }, $true))) {
        $mn = ''
        try { $mn = ([string]$m.Member.Value).ToLower() } catch {}
        if (-not $mn) { continue }
        $line = $m.Extent.StartLineNumber
        if ($lineFix -gt 0) { $line = $lineFix }
        $target = $m.Expression
        $tt = ([string]$target.Extent.Text).ToLower()
        $type = ''
        if ($target -is [System.Management.Automation.Language.TypeExpressionAst]) { $type = ([string]$target.TypeName.FullName).ToLower() -replace '^system\.', '' }
        $args0 = $null; $args1 = $null
        if ($m.Arguments -and $m.Arguments.Count -gt 0) { $args0 = $m.Arguments[0] }
        if ($m.Arguments -and $m.Arguments.Count -gt 1) { $args1 = $m.Arguments[1] }
        try {
            if ($mn -eq 'create' -and $type -match '(^|\.)scriptblock$') { Test-DzSink @($m.Arguments) @() $line '[scriptblock]::Create' $depth; continue }
            if ($tt -match '^\$executioncontext\.invokecommand$' -and @('invokescript', 'newscriptblock', 'expandstring') -contains $mn) {
                Test-DzSink @($m.Arguments) @() $line ('$ExecutionContext.InvokeCommand.' + $m.Member.Value) $depth; continue
            }
            if (@('invoke', 'invokereturnasis', 'invokewithcontext', 'dynamicinvoke') -contains $mn) {
                if ($target -is [System.Management.Automation.Language.InvokeMemberExpressionAst]) {
                    $inner = ([string]$target.Member.Value).ToLower()
                    if (@('getmethod', 'getmethods', 'getmember', 'getproperty', 'getdelegateforfunctionpointer', 'createdelegate', 'newscriptblock') -contains $inner) { Add-DzDynamic $line ('reflection: ' + $inner + '().' + $m.Member.Value) }
                } elseif ($target -is [System.Management.Automation.Language.VariableExpressionAst]) {
                    $vn = ([string]$target.VariablePath.UserPath).ToLower()
                    if (Test-DzSbVar $vn $target.Extent.StartOffset) { continue }
                    if ($script:dzDynVars.ContainsKey($vn)) {
                        $url = $script:dzDlVars[$vn]
                        if ($url) { Add-DzDlExec $line $url } else { Add-DzDynamic $line ($target.Extent.Text + '.' + $m.Member.Value + '()') }
                    } elseif ($mn -ne 'invoke' -or $script:dzParamNames.ContainsKey($vn)) { Add-DzDynamic $line ($target.Extent.Text + '.' + $m.Member.Value + '()') }
                }
                continue
            }
            if ($mn -eq 'invokemember') { Add-DzDynamic $line 'reflection: InvokeMember'; continue }
            if ($type -match '(^|\.)reflection\.assembly$' -and @('load', 'loadfrom', 'loadfile', 'unsafeloadfrom') -contains $mn) {
                $isConst = $args0 -is [System.Management.Automation.Language.StringConstantExpressionAst] -and $mn -eq 'load'
                if (-not $isConst) { Add-DzDynamic $line ('[Reflection.Assembly]::' + $m.Member.Value) }
                continue
            }
            if ($type -match 'interopservices\.marshal$' -and $mn -eq 'getdelegateforfunctionpointer') { Add-DzDynamic $line 'Marshal.GetDelegateForFunctionPointer'; continue }
            if ($type -match '(^|\.)appdomain$' -and $mn -match '^(load|executeassembly)') { Add-DzDynamic $line ('AppDomain.' + $m.Member.Value); continue }
            if ($mn -eq 'frombase64string') {
                if ($script:dzHasSink) { Add-Danger 'Encoded' 'high' $line 'FromBase64String' } else { Add-Danger 'Encoded' 'low' $line 'FromBase64String' }
                continue
            }
            if ($script:dzDlMembers -contains $mn) {
                Add-Danger 'Download' 'low' $line ((Get-DzArgText $args0).Trim([char]34, [char]39))
                continue
            }
            # File system through .NET
            if (($type -match '(^|\.)io\.file$' -and $mn -eq 'delete') -or ($type -match '(^|\.)io\.directory$' -and $mn -eq 'delete') -or
                ($type -match 'visualbasic\.fileio\.filesystem$' -and $mn -match '^delete(directory|file)$')) {
                $path = ''
                if ($args0) { $path = Get-DzArgText $args0 }
                $rec = ($type -match 'directory$' -and $args1 -and (Test-DzOn (Get-DzArgText $args1))) -or $mn -eq 'deletedirectory'
                $label = $target.Extent.Text + '::' + $m.Member.Value
                if (Test-DzSystemPath @($path)) { Add-Danger 'NetDelete' 'high' $line ($label + ' ' + $path) }
                elseif (Test-DzTempPath $path) { }
                elseif ($rec) { Add-Danger 'NetDelete' 'medium' $line ($label + ' ' + $path) }
                else { Add-Danger 'NetDelete' 'low' $line ($label + ' ' + $path) }
                continue
            }
            if ($mn -eq 'delete' -and -not $m.Static) {
                if ($script:dzFull -match '(?i)shadowcopy') { Add-Danger 'Shadows' 'high' $line 'Win32_ShadowCopy.Delete()' }
                elseif ($args0 -and (Test-DzOn (Get-DzArgText $args0))) { Add-Danger 'NetDelete' 'medium' $line ($m.Extent.Text) }
                continue
            }
            # Registry through .NET
            if (@('deletesubkey', 'deletesubkeytree', 'deletevalue') -contains $mn) {
                if ($tt -match 'localmachine|classesroot|\]::users' -or ($script:dzHasHklmNet -and -not ($tt -match 'currentuser'))) { Add-Danger 'RegDel' 'high' $line ($m.Member.Value + ' ' + (Get-DzArgText $args0)) }
                else { Add-Danger 'RegWrite' 'medium' $line $m.Member.Value }
                continue
            }
            if (@('setvalue', 'createsubkey') -contains $mn -and $script:dzHasHklmNet -and -not ($tt -match 'currentuser')) {
                Add-Danger 'RegWrite' 'medium' $line ('HKLM ' + $m.Member.Value); continue
            }
            # Processes through .NET
            if ($type -match '(^|\.)diagnostics\.process$' -and $mn -eq 'start' -and $args0) {
                $tokens = @(Get-DzArgText $args0)
                if ($args1) { $tokens += @(Split-DzText (Get-DzArgText $args1)) }
                $u = Get-DzDownloadedRun $tokens
                if ($null -ne $u) { Add-DzDlExec $line $u }
                Test-DzCommand (New-DzFromTokens $tokens $m.Extent.Text) $line '' ($depth + 1)
                continue
            }
        } catch { Add-DzError $_ }
    }
    # $ExecutionContext.InvokeCommand used as a property chain without a call we know
    foreach ($x in @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.MemberExpressionAst] -and -not ($a -is [System.Management.Automation.Language.InvokeMemberExpressionAst]) }, $true))) {
        if ([string]$x.Extent.Text -match '^(?i)\$executioncontext\.invokecommand\.(invokescript|newscriptblock)$') {
            $line = $x.Extent.StartLineNumber
            if ($lineFix -gt 0) { $line = $lineFix }
            Add-DzDynamic $line ([string]$x.Extent.Text)
        }
    }
    # Encoded command lines hidden in strings (Start-Process powershell '-enc ...', etc.)
    foreach ($s in @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.StringConstantExpressionAst] -or $a -is [System.Management.Automation.Language.ExpandableStringExpressionAst] }, $true))) {
        if ([string]$s.Value -match '(?i)(^|\s)[-/](e|ec|en|enc|enco|encod|encode|encoded|encodedc[a-z]*)\s+[A-Za-z0-9+/=]{16,}') {
            $line = $s.Extent.StartLineNumber
            if ($lineFix -gt 0) { $line = $lineFix }
            Add-Danger 'Encoded' 'high' $line '-EncodedCommand'
        }
    }
}

function Get-DangerFindings($ast) {
    $script:dzCur = New-Object System.Collections.ArrayList
    $script:dzErrCount = 0; $script:dzErrFirst = ''; $script:dzRxTimeouts = 0
    if (-not $ast) { return }
    $full = ''
    try { $full = $ast.Extent.StartScriptPosition.GetFullScript() } catch {}
    if (-not $full) { $full = $ast.Extent.Text }
    $script:dzFull = $full
    $script:dzLines = @($full -split '\r?\n')
    $script:dzHasHklm = $full -match '(?i)HKLM:|HKEY_LOCAL_MACHINE'
    $script:dzHasHklmNet = $full -match '(?i)registry\]::localmachine|registryhive\]::localmachine|openbasekey\([^)]*localmachine'
    $script:dzHasSink = $full -match '(?i)\b(iex|invoke-expression|icm|invoke-command)\b|scriptblock\]::create|newscriptblock|invokescript|assembly\]::load|\badd-type\b|&\s*\$'
    try { Initialize-DzContext $ast } catch { Add-DzError $_ }
    try { Invoke-DzScan $ast 0 0 } catch { Add-DzError $_ }
    # Something failed inside the scan: the result may be incomplete, which is a finding of its own (never "clean")
    if ($script:dzErrCount -gt 0) { Add-Danger 'AnalyzerError' 'medium' 1 ([string]$script:dzErrFirst) }
    # A line that downloads and runs code says so once; 'built dynamically' adds nothing there
    $dl = @{}
    foreach ($f in $script:dzCur) { if ($f.Rule -eq 'DlExec') { $dl[$f.Line] = $true } }
    $res = @($script:dzCur | Where-Object { -not (($_.Rule -eq 'Dynamic' -or $_.Rule -eq 'Download') -and $dl.ContainsKey($_.Line)) })
    @($res | Sort-Object @{ Expression = { if ($_.Sev -eq 'high') { 0 } elseif ($_.Sev -eq 'medium') { 1 } else { 2 } } }, @{ Expression = { $_.Line } })
}

function Test-Ps7LikeError($err, [string]$text) {
    if (-not $err -or -not $text) { return $false }
    $off = 0
    try { $off = [int]$err.Extent.StartOffset } catch { return $false }
    $start = [Math]::Max(0, $off - 3)
    if ($start -ge $text.Length) { return $false }
    $near = $text.Substring($start, [Math]::Min(8, $text.Length - $start))
    return ($near -match '\?\?|\?\.|\?\[|&&|\|\||\s\?\s')
}

function Get-EngineHints($ast, $tokens, $errors, [string]$text) {
    $h7 = New-Object System.Collections.ArrayList
    $hw = New-Object System.Collections.ArrayList
    foreach ($t in @($tokens)) {
        if (-not $t) { continue }
        $k = [string]$t.Kind
        if ($script:ps7TokenText.ContainsKey($k)) { [void]$h7.Add($script:ps7TokenText[$k]) }
        # .NET Framework-only assemblies and types in code (strings, type names, arguments; not comments)
        if ($k -ne 'Comment') {
            $tt = [string]$t.Text
            if ($tt.Length -gt 12) { foreach ($rx in $script:winPsTypes.Keys) { if ($tt -match $rx) { [void]$hw.Add($script:winPsTypes[$rx]) } } }
        }
    }
    foreach ($e in @($errors)) {
        if (-not $e) { continue }
        if (Test-Ps7LikeError $e $text) {
            $near = ''
            try { $near = [string]$e.Extent.Text } catch {}
            if ($near -match '\?\?=') { [void]$h7.Add('??=') }
            elseif ($near -match '\?\?') { [void]$h7.Add('??') }
            elseif ($near -match '&&') { [void]$h7.Add('&&') }
            elseif ($near -match '\|\|') { [void]$h7.Add('||') }
            elseif ($near -match '\?\.') { [void]$h7.Add('?.') }
            elseif ($near -match '\?\[') { [void]$h7.Add('?[]') }
            else { [void]$h7.Add('? :') }
        }
    }
    if ($text -match '(?im)^\s*workflow\s+[\w-]+') { [void]$hw.Add('workflow') }
    if ($ast) {
        try {
            foreach ($f in @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true))) {
                if ($f.IsWorkflow) { [void]$hw.Add('workflow') }
            }
        } catch {}
        try {
            foreach ($sb in @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.ScriptBlockAst] }, $true))) {
                if ($sb.PSObject.Properties['CleanBlock'] -and $sb.CleanBlock) { [void]$h7.Add('clean { }') }
            }
        } catch {}
        try {
            foreach ($cmd in @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.CommandAst] }, $true))) {
                $name = [string]$cmd.GetCommandName()
                if (-not $name) { continue }
                $ln = $name.ToLower()
                if ($script:ps7Cmdlets -contains $ln) { [void]$h7.Add($name) }
                if ($script:winPsCmdlets -contains $ln) { [void]$hw.Add($name) }
                # 'clean { }' read by the 5.1 parser: a command named clean with one script block argument
                if ($ln -eq 'clean' -and $cmd.CommandElements.Count -eq 2 -and $cmd.CommandElements[1] -is [System.Management.Automation.Language.ScriptBlockExpressionAst]) {
                    [void]$h7.Add('clean { }')
                }
                if ($script:ps7Params.ContainsKey($ln)) {
                    foreach ($el in $cmd.CommandElements) {
                        if ($el -is [System.Management.Automation.Language.CommandParameterAst]) {
                            $pn = $el.ParameterName.ToLower()
                            foreach ($known in $script:ps7Params[$ln]) {
                                if ($pn.Length -ge 3 -and $known.StartsWith($pn)) { [void]$h7.Add($name + ' -' + $known); break }
                            }
                        }
                    }
                }
                if (@('get-content', 'set-content', 'add-content', 'gc', 'sc', 'ac') -contains $ln) {
                    for ($i = 0; $i -lt $cmd.CommandElements.Count - 1; $i++) {
                        $el = $cmd.CommandElements[$i]
                        if ($el -is [System.Management.Automation.Language.CommandParameterAst] -and $el.ParameterName -match '^(?i)enc') {
                            if ([string]$cmd.CommandElements[$i + 1].Extent.Text -match '^(?i)[''"]?byte[''"]?$') { [void]$hw.Add($name + ' -Encoding Byte') }
                        }
                    }
                }
            }
        } catch {}
        try {
            foreach ($v in @($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.VariableExpressionAst] }, $true))) {
                if ($v.VariablePath.UserPath -eq 'PSStyle') { [void]$h7.Add('$PSStyle'); break }
            }
        } catch {}
    }
    return [pscustomobject]@{ Ps7 = @($h7 | Select-Object -Unique); WinPs = @($hw | Select-Object -Unique) }
}

function Get-RequiresInfo($ast) {
    $ver = $null; $eds = @(); $elev = $false
    $req = $null
    try { $req = $ast.ScriptRequirements } catch {}
    if ($req) {
        try { if ($req.RequiredPSVersion) { $ver = [version]$req.RequiredPSVersion } } catch {}
        try { $eds = @($req.RequiredPSEditions | Where-Object { $_ } | ForEach-Object { [string]$_ }) } catch {}
        try { $elev = [bool]$req.IsElevationRequired } catch {}
    }
    return [pscustomobject]@{ Version = $ver; Editions = $eds; Elevation = $elev }
}

function ConvertTo-ErrorList($errors) {
    @($errors | Where-Object { $_ } | ForEach-Object {
        [pscustomobject]@{ Line = $_.Extent.StartLineNumber; Message = [string]$_.Message }
    })
}

# ---------- Main: one record per file ----------
# Input: a UTF-8 text file with one script path per line. Output: a JSON array (UTF-8 with BOM).
# Record: P (path), T (LastWriteTimeUtc ticks), E (engine: 7 | 5.1), Desc, Errors, Ps7, WinPs, Params, Danger,
# Req (Version, Editions, Elevation), MissingModules.
$engine = $(if ($PSVersionTable.PSEdition -eq 'Core') { '7' } else { '5.1' })
$res = New-Object System.Collections.ArrayList
foreach ($p in [IO.File]::ReadAllLines($In, [Text.Encoding]::UTF8)) {
    if (-not $p) { continue }
    $t = $null; $e = $null; $ast = $null
    try {
        $item = Get-Item -LiteralPath $p -ErrorAction Stop
        $ticks = [string]$item.LastWriteTimeUtc.Ticks
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($item.FullName, [ref]$t, [ref]$e)
        $text = ''
        try { $text = $ast.Extent.Text } catch {}
        $h = Get-EngineHints $ast $t $e $text
        $req = Get-RequiresInfo $ast
        $missing = @()
        try {
            if ($ast.ScriptRequirements) {
                foreach ($m in @($ast.ScriptRequirements.RequiredModules | Where-Object { $_ })) { if (-not (Test-ModuleAvailable $m.Name)) { $missing += [string]$m.Name } }
            }
        } catch {}
        [void]$res.Add([pscustomobject]@{
            P = $p; T = $ticks; E = $engine; Desc = [string](Get-Description $item.FullName)
            Errors = @(ConvertTo-ErrorList $e); Ps7 = @($h.Ps7 | ForEach-Object { [string]$_ }); WinPs = @($h.WinPs | ForEach-Object { [string]$_ })
            Params = @(Get-ParamsFromAst $ast); Danger = @(Get-DangerFindings $ast)
            Req = [pscustomobject]@{ Version = $(if ($req.Version) { [string]$req.Version } else { '' }); Editions = @($req.Editions); Elevation = [bool]$req.Elevation }
            MissingModules = @($missing)
        })
    } catch {}
}
$json = ConvertTo-Json -InputObject @($res) -Depth 6 -Compress
[IO.File]::WriteAllText($Out, $json, (New-Object System.Text.UTF8Encoding $true))
