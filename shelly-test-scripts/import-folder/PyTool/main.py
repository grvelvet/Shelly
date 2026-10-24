# Описание: PyTool: скрипт со своими файлами (импорт папкой)
# Shelly-Icon: E8F1
#
# Что проверяет «Импортировать папку…» (команды «>», меню группы Scripts или перетаскивание папки PyTool в окно):
#   - копируется вся папка: main.py, textutil.py, _fmt.py, data\config.json, data\items.csv;
#   - в диалоге импорта отмечены main.py и textutil.py (_fmt.py в список не попадает: имя на «_»).
#     Снимите флажок с textutil.py - он останется на диске, но будет скрыт (запишется в .shelly.json);
#   - у main метка папки («Скрипт со своими файлами»);
#   - скрипт находит модули и данные рядом с собой через Path(__file__).parent.
# Ожидается: печать настроек из config.json, таблица из items.csv, «.shelly.json рядом: да» (после импорта), код 0.
import csv
import json
import sys
from pathlib import Path

import _fmt
import textutil

here = Path(__file__).resolve().parent
cfg = json.loads((here / "data" / "config.json").read_text(encoding="utf-8"))
print(f"Папка скрипта: {here}")
print(f"Конфигурация: {cfg['title']} (версия {cfg['version']})")
with open(here / "data" / "items.csv", encoding="utf-8", newline="") as f:
    rows = list(csv.DictReader(f))
print(_fmt.table(rows, ["name", "qty", "price"]))
total = sum(int(r["qty"]) * float(r["price"]) for r in rows)
print(textutil.banner(f"Итого: {total:.2f} {cfg['currency']}"))
manifest = here / ".shelly.json"
print(f".shelly.json рядом: {'да' if manifest.exists() else 'нет (папка не импортирована, а скопирована или подключена)'}")
if manifest.exists():
    data = json.loads(manifest.read_text(encoding="utf-8"))
    print(f"  скрыто: {data.get('hidden', [])}")
sys.exit(0)
