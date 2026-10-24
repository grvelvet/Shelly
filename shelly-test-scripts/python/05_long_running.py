# Описание: Долгий Python-скрипт: вывод раз в секунду, «Остановить», дочерний процесс
# Shelly-Icon: E916
#
# Аргументы (поле «Аргументы»): число секунд (по умолчанию 30) и, по желанию, слово child.
#   Например: 60 child
# Что проверяет:
#   - вывод приходит сразу, построчно (Shelly запускает python -u);
#   - «Остановить» завершает процесс и всё его дерево (с child - и дочерний python, проверьте диспетчер задач);
#   - при остановке finally не выполняется, файл-метка %TEMP%\shelly-test-py-running.txt остаётся;
#     при обычном завершении он удаляется;
#   - в отдельном окне Ctrl+C даёт KeyboardInterrupt (строка «Прервано Ctrl+C»).
# Ожидается: обычное завершение - «Готово», код 0; «Остановить» - статус «Остановлен».
import os
import subprocess
import sys
import tempfile
import time

seconds = 30
child = False
for a in sys.argv[1:]:
    if a.isdigit():
        seconds = int(a)
    elif a.lower() == "child":
        child = True

marker = os.path.join(tempfile.gettempdir(), "shelly-test-py-running.txt")
with open(marker, "w", encoding="utf-8") as f:
    f.write(f"запущен {time.strftime('%Y-%m-%d %H:%M:%S')}, PID {os.getpid()}\n")
print(f"PID {os.getpid()}, метка: {marker}")

proc = None
try:
    if child:
        code = "import time\nfor i in range(%d):\n    print('  ребёнок:', i + 1, flush=True)\n    time.sleep(1)\n" % seconds
        proc = subprocess.Popen([sys.executable, "-u", "-c", code])
        print(f"Дочерний процесс PID {proc.pid}")
        proc.wait()
    else:
        for i in range(1, seconds + 1):
            print(f"{time.strftime('%H:%M:%S')}  шаг {i} из {seconds}  [{'#' * (i * 20 // seconds):<20}]")
            time.sleep(1)
    print("Готово.")
except KeyboardInterrupt:
    print("Прервано Ctrl+C")
    sys.exit(130)
finally:
    # Не выполняется при «Остановить» (процесс завершается принудительно)
    try:
        os.remove(marker)
        print("finally: метка удалена")
    except OSError:
        pass
sys.exit(0)
