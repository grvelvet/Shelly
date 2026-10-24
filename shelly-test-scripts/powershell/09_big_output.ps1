# Описание: Большой вывод: тысячи строк, проверка ограничений панели и истории
# Shelly-Icon: E8A5
#
# Что проверяет:
#   - панель вывода не тормозит на 5000 строк (по умолчанию);
#   - ограничения: панель показывает не больше 100 000 строк / 4 МБ за запуск, дальше - пометка о пропуске;
#     История хранит не больше «Сохранять вывода на запуск» (по умолчанию 1 МБ): начало и последние строки,
#     между ними строка о пропущенных строках;
#   - Lines = 120000: в панели сообщение, что остальное пропущено; последняя строка «КОНЕЦ» есть в истории.
# Ожидается: строки пронумерованы 000001...; последняя строка «КОНЕЦ: N строк»; код 0.
param(
    [int]$Lines = 5000,
    [int]$Width = 60
)

$pad = 'x' * [Math]::Max(0, $Width)
$sw = [Diagnostics.Stopwatch]::StartNew()
$batch = New-Object System.Collections.Generic.List[string]
for ($i = 1; $i -le $Lines; $i++) {
    $batch.Add(("{0:D6} строка {1}" -f $i, $pad))
    if ($batch.Count -ge 1000) { [Console]::Out.Write(($batch -join "`n") + "`n"); $batch.Clear() }
}
if ($batch.Count -gt 0) { [Console]::Out.Write(($batch -join "`n") + "`n") }
[Console]::Out.Flush()
Write-Output ("КОНЕЦ: {0} строк за {1:N1} с" -f $Lines, $sw.Elapsed.TotalSeconds)
exit 0
