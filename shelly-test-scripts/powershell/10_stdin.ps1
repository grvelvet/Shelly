# Описание: Ввод с клавиатуры: Read-Host и чтение stdin
# Shelly-Icon: E765
# Shelly-Mode: window
#
# Что проверяет: во встроенном режиме у скрипта нет ввода (PowerShell запущен с -NonInteractive, stdin закрыт).
#   Строка «Shelly-Mode: window» выше: двойной щелчок запускает скрипт в отдельном окне.
# Ожидается:
#   - отдельное окно (двойной щелчок): спрашивает имя, здоровается, окно остаётся открытым;
#   - встроенный режим (меню > «Запустить» или F5 в редакторе): Read-Host выдаёт ошибку
#     «NonInteractive mode», скрипт сообщает об этом, [Console]::In.ReadLine() сразу возвращает пусто; код 3.

$interactive = $true
try {
    $name = Read-Host -Prompt 'Как вас зовут?'
} catch {
    $interactive = $false
    Write-Output "Read-Host не работает: $($_.Exception.Message)"
}
if ($interactive) {
    Write-Output "Привет, $name!"
    $ok = Read-Host -Prompt 'Нажмите Enter для выхода'
    exit 0
}
$line = [Console]::In.ReadLine()
Write-Output ("[Console]::In.ReadLine() вернул: {0}" -f $(if ($null -eq $line) { '<null> (stdin закрыт)' } else { "'$line'" }))
Write-Output "Запустите скрипт в отдельном окне, чтобы ввести данные."
exit 3
