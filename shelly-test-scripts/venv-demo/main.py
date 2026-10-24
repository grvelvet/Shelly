# Описание: venv-demo: запуск из .venv рядом со скриптом (сначала setup_venv.ps1)
# Shelly-Icon: E943
#
# Что проверяет поиск виртуального окружения: .venv / venv / env (с pyvenv.cfg) в папке скрипта или выше,
#   до корня Scripts (или подключённой папки). Ближайшее побеждает.
# Порядок теста:
#   1. Запустите main.py ДО setup_venv.ps1: метка PY, «В venv: нет», colorama не найдена -> код 2.
#   2. Запустите setup_venv.ps1 (создаёт .venv и ставит colorama из requirements.txt; нужен интернет).
#   3. Список обновляется сам (или F5 на странице «Скрипты»): у main метка venv, в шапке вывода «.venv · Python 3.x».
#   4. Запустите main.py: «В venv: да», VIRTUAL_ENV = ...\venv-demo\.venv, colorama найдена, код 0.
#      То же в отдельном окне и от администратора (первая строка окна «.venv · Python 3.x»).
#   5. Параметры > Окружение > Python: выключите «Использовать .venv рядом со скриптом» -> снова системный Python, код 2.
#   6. Удалите .venv\Scripts\python.exe -> метка venv красная, запуск даёт ошибку «Виртуальное окружение повреждено»,
#      на системный Python Shelly не переключается. Восстановление: удалить .venv и снова setup_venv.ps1.
import os
import sys

in_venv = sys.prefix != sys.base_prefix
print(f"Python:        {sys.version.split()[0]}")
print(f"sys.executable: {sys.executable}")
print(f"В venv:        {'да' if in_venv else 'нет'}")
print(f"VIRTUAL_ENV:   {os.environ.get('VIRTUAL_ENV', '<нет>')}")
print(f"PYTHONHOME:    {os.environ.get('PYTHONHOME', '<нет>')}")
first_path = os.environ.get("PATH", "").split(os.pathsep)[0]
print(f"PATH[0]:       {first_path}")
try:
    import colorama
except ImportError:
    print("colorama: НЕ НАЙДЕНА. Запустите setup_venv.ps1 (или включите использование .venv).", file=sys.stderr)
    sys.exit(2)
colorama.just_fix_windows_console() if hasattr(colorama, "just_fix_windows_console") else colorama.init()
print(f"colorama:      {colorama.__version__} из {os.path.dirname(colorama.__file__)}")
print(colorama.Fore.GREEN + "Зелёный текст через colorama" + colorama.Style.RESET_ALL + " (во встроенной панели цвета вырезаются)")
sys.exit(0)
