# Описание: stdout и stderr в Python: print, sys.stderr, warnings, logging, ANSI-цвета
# Shelly-Icon: E8BD
#
# Что проверяет, как Shelly показывает два канала вывода Python.
# Ожидается во встроенном режиме:
#   - строки stdout - обычные, строки stderr (print(file=sys.stderr), warnings, logging) - помечены «! » / красным;
#   - ANSI-цвета вырезаются (виден только текст);
#   - stdout и stderr - разные каналы, поэтому их взаимный порядок может немного отличаться от порядка в коде;
#   - код выхода 0, хотя в stderr были строки (в журнале Shelly - запись об ошибке).
# В отдельном окне ANSI-цвета видны.
import logging
import sys
import time
import warnings

logging.basicConfig(level=logging.INFO, format="logging %(levelname)s: %(message)s")  # пишет в stderr

print("1. print -> stdout")
print("2. print(file=sys.stderr) -> stderr", file=sys.stderr)
warnings.warn("3. warnings.warn -> stderr")
logging.info("4. logging.info")
logging.error("5. logging.error")
print("6. ANSI: \x1b[31mкрасный\x1b[0m и \x1b[32mзелёный\x1b[0m")
print("7. Чередование:")
for i in range(1, 7):
    if i % 2:
        print(f"    out {i}")
    else:
        print(f"    err {i}", file=sys.stderr)
    time.sleep(0.05)
print("8. Прогресс в одной строке через \\r (в панели Shelly - отдельные куски или одна строка в конце):")
for p in range(0, 101, 20):
    print(f"\r    {p}%", end="", flush=True)
    time.sleep(0.2)
print()
print("9. Готово (код 0)")
sys.exit(0)
