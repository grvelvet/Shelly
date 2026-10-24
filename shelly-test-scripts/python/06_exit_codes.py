# Описание: Коды выхода Python: 0, 1, свой код, исключение с трассировкой, только stderr
# Shelly-Icon: E7BA
#
# Аргумент (поле «Аргументы») - режим:
#   ok           -> код 0 (по умолчанию);
#   error        -> sys.exit(1);
#   custom 42    -> sys.exit(42) (число можно поменять);
#   exception    -> необработанное исключение: трассировка в stderr (красным), код 1;
#   message      -> sys.exit("текст"): текст в stderr, код 1;
#   stderr       -> строки только в stderr, но код 0 (успех, запись в журнале Shelly).
# Ожидается: результат в строке, подвале вывода («код N») и в «Истории» соответствует коду.
import sys

mode = sys.argv[1].lower() if len(sys.argv) > 1 else "ok"
print(f"Режим: {mode}")

if mode == "ok":
    sys.exit(0)
elif mode == "error":
    sys.exit(1)
elif mode == "custom":
    code = int(sys.argv[2]) if len(sys.argv) > 2 else 42
    print(f"Выход с кодом {code}")
    sys.exit(code)
elif mode == "exception":
    def inner():
        return {"ключ": 1}["нет такого ключа"]
    inner()
elif mode == "message":
    sys.exit("Ошибка: выход через sys.exit(\"текст\")")
elif mode == "stderr":
    print("Предупреждение в stderr, но всё хорошо", file=sys.stderr)
    sys.exit(0)
else:
    print(f"Неизвестный режим {mode!r}. Режимы: ok, error, custom N, exception, message, stderr", file=sys.stderr)
    sys.exit(2)
