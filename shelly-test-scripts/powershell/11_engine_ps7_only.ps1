# Описание: Только PowerShell 7: операторы ?? и ?: (Shelly сам выберет 7)
# Shelly-Icon: E946
#
# Что проверяет автовыбор движка: скрипт использует синтаксис PowerShell 7 (??, тернарный ?:),
#   который Windows PowerShell 5.1 не разбирает.
# Ожидается:
#   - PowerShell 7 установлен: в режиме «Авто» запускается в 7 (подсказка у метки движка объясняет почему), код 0;
#   - 7 не установлен: Shelly перед запуском сообщает, что нужен PowerShell 7, и предлагает установить;
#   - если вручную выбрать 5.1: ошибки разбора, код 1.

$value = $null
$text = $value ?? 'значение по умолчанию из ??'
$kind = ($PSVersionTable.PSVersion.Major -ge 7) ? 'PowerShell 7+' : 'старый PowerShell'
Write-Output "Оператор ??:  $text"
Write-Output "Оператор ?: : $kind"
Write-Output ("Версия: {0} ({1})" -f $PSVersionTable.PSVersion, $PSVersionTable.PSEdition)
exit 0
