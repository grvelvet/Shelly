# Описание: Импорт модуля из той же папки (_hidden_module.py скрыт из списка)
# Shelly-Icon: E943
#
# Что проверяет: Python-скрипт видит модули рядом с собой (папка скрипта - sys.path[0]);
#   файл _hidden_module.py лежит рядом, но в списке Shelly его нет (имя начинается с «_»).
#   После запуска появится папка __pycache__ - её Shelly тоже не показывает.
# Ожидается: «Привет из _hidden_module, Shelly!», код 0.
import sys

import _hidden_module

print(_hidden_module.greet("Shelly"))
print(f"Модуль загружен из: {_hidden_module.__file__}")
sys.exit(0)
