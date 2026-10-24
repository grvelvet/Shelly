# Описание: Запущен ли скрипт от имени администратора
# Shelly-Icon: E8D7
# Shelly-Mode: admin
#
# Что проверяет:
#   - строка «Shelly-Mode: admin» выше: двойной щелчок / Enter запускают скрипт от администратора
#     (пока в «Параметрах запуска» не запомнен другой режим);
#   - перед запуском от администратора Shelly всегда спрашивает подтверждение, затем появляется запрос UAC;
#   - после UAC Shelly проверяет SHA-256 файла: если файл успели изменить, скрипт не запустится;
#   - рабочая папка в окне администратора - папка скрипта (а не System32).
# Ожидается: от администратора - «Администратор: ДА», окно остаётся открытым;
#   через меню «Запустить» (встроенный режим) - «Администратор: нет», код 0.

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($id)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Output ("Пользователь:   {0}" -f $id.Name)
Write-Output ("Администратор:  {0}" -f $(if ($isAdmin) { 'ДА' } else { 'нет' }))
Write-Output ("Рабочая папка:  {0}" -f (Get-Location).Path)
Write-Output ("Движок:         {0} {1}" -f $PSVersionTable.PSEdition, $PSVersionTable.PSVersion)
exit 0
