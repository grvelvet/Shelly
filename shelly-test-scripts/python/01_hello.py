# Описание: Привет от Python: версия, интерпретатор, venv, переменные окружения от Shelly
# Shelly-Icon: E943
#
# Что проверяет:
#   - какой интерпретатор нашёл Shelly (Параметры > Окружение > Среда выполнения: Python);
#   - что Shelly задаёт PYTHONUTF8=1, PYTHONIOENCODING=utf-8, PYTHONUNBUFFERED=1 и запускает с -u;
#   - рабочую папку (папка скрипта), sys.argv, VIRTUAL_ENV (пусто, если рядом нет .venv);
#   - перенаправлены ли вход и выход (во встроенном режиме - да, stdin закрыт).
# Ожидается: встроенный режим - в шапке вывода «Python 3.x», код 0; отдельное окно - первая строка окна
#   «Python 3.x», isatty = True, окно остаётся открытым («Код выхода: 0»).
import os
import platform
import sys

print("Привет от Python!")
print(f"Версия:          {sys.version.split()[0]} ({platform.architecture()[0]})")
print(f"sys.executable:  {sys.executable}")
print(f"sys.prefix:      {sys.prefix}")
print(f"base_prefix:     {sys.base_prefix}")
print(f"В venv:          {'да' if sys.prefix != sys.base_prefix else 'нет'}")
print(f"VIRTUAL_ENV:     {os.environ.get('VIRTUAL_ENV', '<нет>')}")
for name in ("PYTHONUTF8", "PYTHONIOENCODING", "PYTHONUNBUFFERED", "PYTHONHOME"):
    print(f"{name + ':':<17}{os.environ.get(name, '<нет>')}")
print(f"Командная строка: {getattr(sys, 'orig_argv', sys.argv)!r}")  # с Shelly: [..., '-u', 'путь\\01_hello.py', ...]
print(f"UTF-8 режим:     {sys.flags.utf8_mode}")
print(f"stdout:          {sys.stdout.encoding}, isatty = {sys.stdout.isatty()}")
print(f"stdin:           isatty = {sys.stdin.isatty() if sys.stdin else None}")
print(f"Рабочая папка:   {os.getcwd()}")
print(f"Папка скрипта:   {os.path.dirname(os.path.abspath(__file__))}")
print(f"sys.argv:        {sys.argv!r}")
print(f"ОС:              {platform.platform()}")
sys.exit(0)
