# Описание: Привет от PowerShell: версия движка, кодировки, как Shelly запустил скрипт
# Shelly-Icon: E946
#
# Что проверяет:
#   - какой движок выбрал Shelly (5.1 или 7): PSVersion / PSEdition;
#   - рабочую папку (Shelly запускает из папки скрипта) и $PSScriptRoot;
#   - кодировку вывода (во встроенном режиме UTF-8), перенаправлены ли вход и выход;
#   - командную строку, с которой Shelly запустил PowerShell (-NoProfile -NonInteractive -ExecutionPolicy Bypass).
# Ожидается:
#   - встроенный режим: Edition Core / 7.x или Desktop / 5.1 (как выбрано в «Параметрах запуска»),
#     OutputEncoding = utf-8, вход и выход перенаправлены (True), код выхода 0;
#   - отдельное окно: то же, но вывод не перенаправлен (False), окно остаётся открытым.
# Переключите движок на 5.1 и на 7 в «Параметрах запуска» и сравните.

$ErrorActionPreference = 'Continue'
Write-Output "Привет от PowerShell!"
Write-Output ("Версия:            {0}" -f $PSVersionTable.PSVersion)
Write-Output ("Редакция:          {0}" -f $PSVersionTable.PSEdition)
Write-Output ("Хост:              {0}" -f $Host.Name)
Write-Output ("64-битный процесс: {0}" -f [Environment]::Is64BitProcess)
Write-Output ("Режим языка:       {0}" -f $ExecutionContext.SessionState.LanguageMode)
Write-Output ("Политика:          {0}" -f (Get-ExecutionPolicy))
Write-Output ("Рабочая папка:     {0}" -f (Get-Location).Path)
Write-Output ("PSScriptRoot:      {0}" -f $PSScriptRoot)
Write-Output ("OutputEncoding:    {0}" -f [Console]::OutputEncoding.WebName)
Write-Output ("Вывод перенаправлен: {0}" -f [Console]::IsOutputRedirected)
Write-Output ("Вход перенаправлен:  {0}" -f [Console]::IsInputRedirected)
Write-Output ("Пользователь:      {0}\{1}" -f $env:USERDOMAIN, $env:USERNAME)
Write-Output "Командная строка:"
Write-Output ("  " + [Environment]::CommandLine)
exit 0
