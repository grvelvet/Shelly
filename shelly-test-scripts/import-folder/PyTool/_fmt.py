# Модуль форматирования для main.py. Имя начинается с «_»: Shelly его не показывает и в диалоге импорта не предлагает.


def table(rows, cols):
    widths = {c: max(len(c), *(len(str(r[c])) for r in rows)) for c in cols}
    head = "  ".join(c.ljust(widths[c]) for c in cols)
    sep = "  ".join("-" * widths[c] for c in cols)
    body = ["  ".join(str(r[c]).ljust(widths[c]) for c in cols) for r in rows]
    return "\n".join([head, sep, *body])
