# Описание: Python: запущен ли от имени администратора (IsUserAnAdmin)
# Shelly-Icon: E8D7
#
# Что проверяет запуск .py от администратора (меню > «Запустить от администратора» или режим в «Параметрах запуска»):
#   подтверждение Shelly, UAC, проверка SHA-256 после UAC, окно PowerShell с первой строкой «Python 3.x».
# Ожидается: от администратора - «Администратор: ДА»; обычный запуск - «нет»; код 0.
import ctypes
import os
import sys

try:
    is_admin = bool(ctypes.windll.shell32.IsUserAnAdmin())
except AttributeError:          # не Windows
    is_admin = hasattr(os, "geteuid") and os.geteuid() == 0
print(f"Администратор: {'ДА' if is_admin else 'нет'}")
print(f"Пользователь:  {os.environ.get('USERNAME') or os.environ.get('USER')}")
print(f"Рабочая папка: {os.getcwd()}")
print(f"Python:        {sys.version.split()[0]} ({sys.executable})")
sys.exit(0)
