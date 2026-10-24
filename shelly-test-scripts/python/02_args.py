# Описание: Аргументы Python (argparse): поле «Аргументы» в параметрах запуска
# Shelly-Icon: E713
#
# У .py нет формы параметров: в «Параметрах запуска» одно поле «Аргументы». Текст делится как командная
# строка Windows (пробелы разделяют, двойные кавычки группируют) и попадает в sys.argv.
# Что проверяет: разбор аргументов, кириллицу и пробелы в кавычках, «Запомнить для этого скрипта».
# Попробуйте:
#   --name "Анна Каренина" --count 3 -v --color red "файл с пробелом.txt" второй.txt
#   --name 'одинарные' (одинарные кавычки НЕ группируют: придут как отдельные слова с кавычками)
#   --count abc        -> argparse печатает ошибку в stderr, код 2
#   --help             -> справка, код 0
# Ожидается: печать sys.argv как есть и разобранных значений; код 0 (или 2 при ошибке).
import argparse
import sys

print(f"sys.argv[1:] = {sys.argv[1:]!r}")

parser = argparse.ArgumentParser(prog="02_args.py", description="Тест аргументов Shelly")
parser.add_argument("--name", default="Мир", help="имя (по умолчанию Мир)")
parser.add_argument("--count", type=int, default=1, help="сколько раз повторить (целое)")
parser.add_argument("-v", "--verbose", action="store_true", help="подробно")
parser.add_argument("--color", choices=["red", "green", "blue"], default="green", help="цвет")
parser.add_argument("files", nargs="*", help="произвольные позиционные аргументы")
args = parser.parse_args()

print("Разобрано:")
print(f"  name    = {args.name!r}")
print(f"  count   = {args.count!r}")
print(f"  verbose = {args.verbose!r}")
print(f"  color   = {args.color!r}")
print(f"  files   = {args.files!r} ({len(args.files)} шт.)")
for i in range(min(args.count, 20)):
    print(f"{i + 1}. Привет, {args.name}!")
if args.verbose:
    print("(verbose включён)")
sys.exit(0)
