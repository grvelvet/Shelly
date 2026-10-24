# Описание: Коды выхода: exit 0 / 1 / свой код, throw, Write-Error, код внешней программы
# Shelly-Icon: E7BA
#
# Что проверяет, какой результат Shelly показывает в строке, в подвале вывода и в «Истории».
# Mode (выпадающий список):
#   ok         -> exit 0: успех (зелёный);
#   error      -> exit 1: ошибка;
#   custom     -> exit Code (по умолчанию 42): ошибка с кодом 42;
#   throw      -> необработанное исключение: текст ошибки красным, код 1;
#   writeerror -> Write-Error без прерывания: красная строка, но код 0 (успех);
#   native     -> последней была внешняя программа (cmd /c exit 7): код 7;
#   stop       -> $ErrorActionPreference = 'Stop' + ошибка командлета: код 1.
# В отдельном окне код виден после закрытия окна (окно остаётся открытым, запись в истории «закрыто»).
param(
    [ValidateSet('ok', 'error', 'custom', 'throw', 'writeerror', 'native', 'stop')]
    [string]$Mode = 'ok',

    [int]$Code = 42
)

Write-Output "Режим: $Mode"
switch ($Mode) {
    'ok'         { Write-Output "Выход с кодом 0"; exit 0 }
    'error'      { Write-Output "Выход с кодом 1"; exit 1 }
    'custom'     { Write-Output "Выход с кодом $Code"; exit $Code }
    'throw'      { Write-Output "Сейчас будет throw..."; throw "Тестовое исключение из 06_exit_codes.ps1" }
    'writeerror' { Write-Error "Тестовая ошибка (не прерывает скрипт)"; Write-Output "Скрипт продолжился после Write-Error"; exit 0 }
    'native'     { Write-Output "cmd.exe /c exit 7 ..."; cmd.exe /c "exit 7"; Write-Output "LASTEXITCODE = $LASTEXITCODE" }
    'stop'       { $ErrorActionPreference = 'Stop'; Write-Output "Get-Item несуществующего файла..."; Get-Item -LiteralPath 'Z:\нет\такого\файла.txt'; Write-Output "эта строка не должна появиться" }
}
