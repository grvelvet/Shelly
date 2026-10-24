# Описание: Большой вывод Python: тысячи строк, ограничения панели и истории
# Shelly-Icon: E8A5
#
# Аргументы: число строк (по умолчанию 5000) и ширина (по умолчанию 60). Например: 120000
# Что проверяет: скорость панели, ограничения (панель - до 100 000 строк / 4 МБ, история - «Сохранять вывода
#   на запуск», по умолчанию 1 МБ: начало и последние строки с пометкой о пропуске).
# Ожидается: строки 000001...; последняя - «КОНЕЦ: N строк»; при 120000 в панели пометка о пропуске; код 0.
import sys
import time

lines = int(sys.argv[1]) if len(sys.argv) > 1 else 5000
width = int(sys.argv[2]) if len(sys.argv) > 2 else 60
pad = "x" * max(0, width)
t = time.time()
out = sys.stdout
buf = []
for i in range(1, lines + 1):
    buf.append(f"{i:06d} строка {pad}")
    if len(buf) >= 1000:
        out.write("\n".join(buf) + "\n")
        buf.clear()
if buf:
    out.write("\n".join(buf) + "\n")
print(f"КОНЕЦ: {lines} строк за {time.time() - t:.1f} с")
sys.exit(0)
