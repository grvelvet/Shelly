# Описание: Юникод: кириллица, эмодзи, разные алфавиты и очень длинные строки
# Shelly-Icon: E774
#
# Что проверяет: файл в UTF-8 с BOM читается и в 5.1, и в 7; вывод во встроенной панели в UTF-8;
#   передачу кириллицы в параметре; длинную строку без переносов (6000 символов) и строку с табуляциями.
# Ожидается: весь текст читается без «кракозябр» в обоих движках. Эмодзи в панели Shelly показываются;
#   в старом окне консоли Windows 10 (conhost) эмодзи могут быть квадратиками - это шрифт консоли.
param(
    [string]$Text = 'Привет, мир! Ёлка, щука, ъ'
)

Write-Output "Параметр Text: $Text (длина $($Text.Length))"
Write-Output "Русский:     Съешь же ещё этих мягких французских булок, да выпей чаю."
Write-Output "Українська:  Гей, хлопці, ґанок, їжак, є"
Write-Output "Ελληνικά:    Καλημέρα κόσμε"
Write-Output "Deutsch:     Größe, Übermaß, Straße"
Write-Output "中文 / 日本語: 你好，世界 / こんにちは"
Write-Output "Иврит (RTL): שלום עולם"
# Одинарные кавычки: внутри двойных PowerShell считает „ “ ” тоже кавычками и разорвал бы строку
Write-Output 'Символы:     « » „ “ ” — – … № € ₽ ° ± × ÷ ≠ ≤ ≥ ∞ ✓ ✗ →'
# Эмодзи вне BMP (суррогатные пары) собираются из кодов, чтобы файл не зависел от редактора
$emoji = [char]::ConvertFromUtf32(0x1F600) + ' ' + [char]::ConvertFromUtf32(0x1F680) + ' ' + [char]::ConvertFromUtf32(0x1F40D) + ' ' + [char]::ConvertFromUtf32(0x2705)
Write-Output "Эмодзи:      $emoji"
Write-Output ("Табуляция:   a`tб`tв`tг")
Write-Output ""
$long = ('Длинная-строка-' * 400)
Write-Output ("Длинная строка ({0} символов):" -f $long.Length)
Write-Output $long
Write-Output "Конец длинной строки."
$sb = New-Object System.Text.StringBuilder
foreach ($n in 1..30) { [void]$sb.Append("слово$n ") }
Write-Output ("Строка из слов: " + $sb.ToString())
exit 0
