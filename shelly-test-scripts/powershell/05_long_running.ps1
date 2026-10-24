# Описание: Долгий скрипт: вывод раз в секунду, прогресс, кнопка «Остановить», дочерний процесс
# Shelly-Icon: E916
#
# Что проверяет:
#   - вывод приходит построчно, сразу (не в конце);
#   - «Остановить» (кнопка в строке, в панели вывода или в меню) завершает скрипт и всё дерево процессов;
#   - при остановке блок finally НЕ выполняется (процесс убивается), поэтому файл-метка
#     %TEMP%\shelly-test-ps-running.txt остаётся. При обычном завершении он удаляется;
#   - WithChild: запускает ping.exe как дочерний процесс; после «Остановить» в диспетчере задач
#     не должно остаться ping.exe;
#   - уведомление о долгом запуске (Параметры > Запуск, по умолчанию после 20 с) при Seconds > 20;
#   - одновременные запуски: этот же скрипт во встроенном режиме - только один, в отдельном окне - сколько угодно.
# Ожидается: обычное завершение - «Готово», код 0, метки нет; остановка - статус «Остановлен», метка осталась.
param(
    [int]$Seconds = 30,
    [switch]$WithChild
)

$marker = Join-Path $env:TEMP 'shelly-test-ps-running.txt'
Set-Content -LiteralPath $marker -Value ("запущен {0:yyyy-MM-dd HH:mm:ss}, PID {1}" -f (Get-Date), $PID) -Encoding UTF8
Write-Output "PID: $PID, метка: $marker"
try {
    if ($WithChild) {
        Write-Output "Запускаю ping.exe на $Seconds с (дочерний процесс)..."
        ping.exe -n ($Seconds + 1) 127.0.0.1
        Write-Output "ping.exe завершился, код $LASTEXITCODE"
    } else {
        for ($i = 1; $i -le $Seconds; $i++) {
            Write-Progress -Activity "Долгий скрипт" -Status "$i из $Seconds с" -PercentComplete ([int](100 * $i / $Seconds))
            Write-Output ("{0:HH:mm:ss}  шаг {1} из {2}" -f (Get-Date), $i, $Seconds)
            Start-Sleep -Seconds 1
        }
        Write-Progress -Activity "Долгий скрипт" -Completed
    }
    Write-Output "Готово."
}
finally {
    # Не выполняется при «Остановить» (процесс завершается принудительно)
    Remove-Item -LiteralPath $marker -ErrorAction SilentlyContinue
    Write-Output "finally: метка удалена"
}
exit 0
