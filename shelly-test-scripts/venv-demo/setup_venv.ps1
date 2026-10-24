# Описание: venv-demo: создать .venv и установить requirements.txt
# Shelly-Icon: E896
#
# Создаёт виртуальное окружение .venv в папке venv-demo и ставит пакеты из requirements.txt (colorama).
# Нужен Python 3 (py launcher или python в PATH) и интернет для pip.
# Что проверяет: долгий вывод pip во встроенной панели; после создания .venv у main.py появляется метка venv,
#   а сама папка .venv в списке скриптов не показывается.
# Создать заново: удалите папку .venv вручную (в Проводнике) и запустите скрипт ещё раз.
#   (Удаление внутри скрипта вызвало бы замечание «Внимание» проверки безопасности.)
# Ожидается: «Готово: ...\.venv\Scripts\python.exe», код 0. Ошибка pip/venv -> её код выхода.

$venv = Join-Path $PSScriptRoot '.venv'
if (-not (Test-Path -LiteralPath (Join-Path $venv 'pyvenv.cfg'))) {
    Write-Output "Создаю $venv ..."
    if (Get-Command py.exe -ErrorAction SilentlyContinue) {
        py.exe -3 -m venv "$PSScriptRoot\.venv"
    } elseif (Get-Command python.exe -ErrorAction SilentlyContinue) {
        python.exe -m venv "$PSScriptRoot\.venv"
    } else {
        Write-Error "Python 3 не найден (нет py.exe и python.exe в PATH)."
        exit 1
    }
    if ($LASTEXITCODE) { Write-Error "python -m venv завершился с кодом $LASTEXITCODE"; exit $LASTEXITCODE }
} else {
    Write-Output "Уже есть: $venv (чтобы создать заново, удалите папку .venv)"
}
Write-Output "Устанавливаю requirements.txt ..."
& "$PSScriptRoot\.venv\Scripts\python.exe" -m pip install --disable-pip-version-check -r "$PSScriptRoot\requirements.txt"
if ($LASTEXITCODE) { Write-Error "pip завершился с кодом $LASTEXITCODE"; exit $LASTEXITCODE }
Write-Output ("Готово: {0}" -f (Join-Path $venv 'Scripts\python.exe'))
exit 0
