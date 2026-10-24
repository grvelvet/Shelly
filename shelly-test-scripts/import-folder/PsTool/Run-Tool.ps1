# Описание: PsTool: PowerShell-скрипт со своими файлами (импорт папкой)
# Shelly-Icon: E8F1
#
# Что проверяет «Импортировать папку…» для PowerShell:
#   - Run-Tool.ps1 подключает lib\Helpers.ps1 (dot-sourcing через $PSScriptRoot) и читает settings.json и notes.txt
#     рядом с собой - всё работает после копирования папки в Scripts;
#   - в диалоге импорта снимите флажок с lib\Helpers.ps1 - он будет скрыт (запишется в .shelly.json);
#   - .shelly.json в этой папке-образце: при импорте Shelly его НЕ копирует, а пишет заново по флажкам диалога.
#     Он нужен для другого теста: если скопировать папку PsTool в Scripts вручную, Shelly прочтёт этот файл
#     и скроет lib\Helpers.ps1 (в подключённых внешних папках .shelly.json не учитывается).
# Ожидается: строки из settings.json и notes.txt, результат функций из Helpers.ps1, код 0.

. "$PSScriptRoot\lib\Helpers.ps1"

$settings = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Write-Output ("Папка скрипта: {0}" -f $PSScriptRoot)
Write-Output ("Настройки: {0}, повторов {1}" -f $settings.title, $settings.repeat)
Write-Output (Format-Banner $settings.title)
$notes = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'notes.txt') -Encoding UTF8
Write-Output ("notes.txt, строк: {0}" -f @($notes).Count)
foreach ($n in $notes) { Write-Output ("  - {0}" -f $n) }
Write-Output ("Сумма 1..10 из Helpers.ps1: {0}" -f (Get-Sum (1..10)))
$manifest = Join-Path $PSScriptRoot '.shelly.json'
if (Test-Path -LiteralPath $manifest) {
    $m = Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Output (".shelly.json: скрыто {0}" -f (@($m.hidden) -join ', '))
} else {
    Write-Output ".shelly.json нет"
}
exit 0
