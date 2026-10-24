@echo off
:: Описание: Коды выхода bat: exit /b N, errorlevel последней команды, stderr
:: Shelly-Icon: E7BA
::
:: Аргумент (поле «Аргументы»):
::   ok           -> код 0 (по умолчанию)
::   error        -> exit /b 1
::   custom 42    -> exit /b 42 (число можно поменять)
::   last         -> без exit: код последней команды (dir несуществующей папки, обычно 1)
::   stderr       -> строки в stderr (красным во встроенном выводе), код 0
:: Ожидается: код в строке скрипта, в конце вывода и в «Истории»; уведомление об ошибке при коде не 0.
chcp 65001 >nul
set "mode=%~1"
if "%mode%"=="" set mode=ok
echo Режим: %mode%
if /i "%mode%"=="ok" exit /b 0
if /i "%mode%"=="error" (
  echo Ошибка: что-то пошло не так 1>&2
  exit /b 1
)
if /i "%mode%"=="custom" (
  echo Выход с кодом %~2
  exit /b %~2
)
if /i "%mode%"=="stderr" (
  echo Это строка в stderr 1>&2
  echo Ещё одна строка в stderr 1>&2
  exit /b 0
)
if /i "%mode%"=="last" (
  dir "%TEMP%\shelly-no-such-folder-12345" >nul
  goto :eof
)
echo Неизвестный режим: %mode% 1>&2
exit /b 3
