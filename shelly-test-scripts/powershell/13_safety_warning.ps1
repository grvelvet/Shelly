# Описание: Проверка безопасности: безвредный скрипт с замечанием «Внимание» (удаление папки в TEMP)
# Shelly-Icon: E7BA
#
# Что проверяет проверку безопасности Shelly. Скрипт создаёт свою папку %TEMP%\shelly-test-safety
#   и удаляет ТОЛЬКО её (Remove-Item -Recurse -Force). Анализатор видит рекурсивное удаление и ставит
#   замечание «Внимание» (не «Опасно»).
# Ожидается:
#   - у строки метка замечания, в подробностях - строка с Remove-Item;
#   - перед запуском окно «Проверка безопасности»: отметьте «Я понимаю, что делает этот скрипт» и «Запустить»;
#     флажок «Больше не предупреждать для этой версии скрипта» добавляет его в доверенные
#     (Параметры > Безопасность), после изменения файла предупреждение вернётся;
#   - результат: «Папка создана... удалена», код 0.

$dir = Join-Path $env:TEMP 'shelly-test-safety'
New-Item -ItemType Directory -Path $dir -Force | Out-Null
Set-Content -LiteralPath (Join-Path $dir 'test.txt') -Value 'тест' -Encoding UTF8
Write-Output "Папка создана: $dir"
Remove-Item "$env:TEMP\shelly-test-safety" -Recurse -Force
Write-Output ("Папка удалена: {0}" -f (-not (Test-Path -LiteralPath $dir)))
exit 0
