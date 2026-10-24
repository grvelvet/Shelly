# Описание: Потоки вывода: Output, Host с цветами, Warning, Verbose, Debug, Information, Error, ANSI
# Shelly-Icon: E8BD
#
# Что проверяет, как Shelly показывает разные потоки PowerShell.
# Ожидается во встроенном режиме:
#   - Output, Host, WARNING:, VERBOSE:, DEBUG:, INFO - обычные строки (цветов нет: вывод - простой текст);
#   - Write-Error и [Console]::Error - строки ошибок (в панели помечены «! » / красным);
#   - ANSI-последовательности вырезаются, в панели виден только текст «красный»/«зелёный»;
#   - Write-Progress во встроенном режиме не показывается (Shelly отключает прогресс);
#   - порядок строк stdout и stderr может немного перемешаться (это два разных канала);
#   - код выхода 0, хотя были ошибки (Write-Error не прерывает скрипт). В журнале Shelly - запись об ошибке.
# В отдельном окне: цвета Write-Host и ANSI видны, Write-Progress рисует полосу.

$VerbosePreference = 'Continue'
$DebugPreference = 'Continue'
$esc = [char]27

Write-Output "1. Write-Output: обычная строка"
Write-Host   "2. Write-Host зелёным" -ForegroundColor Green
Write-Host   "3. Write-Host жёлтым на синем" -ForegroundColor Yellow -BackgroundColor DarkBlue
Write-Warning "4. Write-Warning: предупреждение"
Write-Verbose "5. Write-Verbose: подробности"
Write-Debug "6. Write-Debug: отладка"
Write-Information "7. Write-Information: информация" -InformationAction Continue
Write-Error "8. Write-Error: ошибка, но скрипт продолжается"
[Console]::Error.WriteLine("9. [Console]::Error.WriteLine: строка в stderr")
Write-Output ("10. ANSI: " + "$esc[31mкрасный$esc[0m и $esc[32mзелёный$esc[0m")
Get-Process -Id $PID | Select-Object Id, ProcessName | Format-Table -AutoSize | Out-String -Stream | Where-Object { $_.Trim() } | ForEach-Object { "11. таблица: $_" }

for ($i = 1; $i -le 5; $i++) {
    Write-Progress -Activity "12. Write-Progress" -Status "шаг $i из 5" -PercentComplete ($i * 20)
    Start-Sleep -Milliseconds 300
}
Write-Progress -Activity "12. Write-Progress" -Completed

Write-Output "13. Чередование stdout/stderr:"
for ($i = 1; $i -le 6; $i++) {
    if ($i % 2) { Write-Output "    out $i" } else { [Console]::Error.WriteLine("    err $i") }
}
Write-Output "14. Готово (код выхода 0)"
exit 0
