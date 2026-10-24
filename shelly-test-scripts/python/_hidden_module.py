# Описание: этот файл НЕ должен появиться в списке Shelly
#
# Файлы .py, имя которых начинается с «_» (как __init__.py), Shelly считает модулями и не показывает.
# Его импортирует 10_uses_hidden_module.py.

def greet(name: str) -> str:
    return f"Привет из _hidden_module, {name}!"
