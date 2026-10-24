# Описание: PsTool: библиотека функций для Run-Tool.ps1 (при импорте снимите с неё флажок)
#
# Подключается из Run-Tool.ps1 через dot-sourcing. Если запустить отдельно - только сообщение, код 0.

function Format-Banner([string]$Text) {
    $line = '=' * ($Text.Length + 4)
    return "$line`n| $Text |`n$line"
}

function Get-Sum([int[]]$Numbers) {
    $s = 0
    foreach ($n in $Numbers) { $s += $n }
    return $s
}

if ($MyInvocation.InvocationName -ne '.') {
    Write-Output "Это библиотека для Run-Tool.ps1, запускать отдельно не нужно."
}
