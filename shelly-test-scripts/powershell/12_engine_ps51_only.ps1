# Описание: Только Windows PowerShell 5.1: Get-WmiObject (Shelly сам выберет 5.1)
# Shelly-Icon: E946
#
# Что проверяет автовыбор движка: Get-WmiObject есть только в Windows PowerShell 5.1.
# Ожидается:
#   - в режиме «Авто» запускается в 5.1 (метка 5.1 у строки, подсказка: команды только для 5.1), код 0;
#   - фильтр списка «Нужен PowerShell 5.1» показывает этот скрипт;
#   - если вручную выбрать 7: Shelly предупреждает; при запуске в 7 ошибка «Get-WmiObject не найден», код 1.

$os = Get-WmiObject -Class Win32_OperatingSystem
Write-Output ("ОС:     {0} (сборка {1})" -f $os.Caption, $os.BuildNumber)
Write-Output ("Версия: {0} ({1})" -f $PSVersionTable.PSVersion, $PSVersionTable.PSEdition)
exit 0
