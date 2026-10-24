<#
.SYNOPSIS
    Форма параметров: строка, числа, дата, switch, bool, ValidateSet, массив, обязательный параметр
.DESCRIPTION
    Описание в списке Shelly берётся из .SYNOPSIS (эта строка не показывается).

    Что проверяет «Параметры запуска» (шестерёнка, Ctrl+I):
      - Name: обязательный (красная *), подсказка из HelpMessage; пока пусто, «Запустить» не даёт запустить;
      - Count [int] = 3, Ratio [double] = 0.5: подсказка-заполнитель с умолчанием; пустое поле не передаётся;
      - When [datetime]: текст; PowerShell понимает дату в формате 2026-10-21 (ISO);
        «21.10.2026» даёт ошибку привязки параметра (PowerShell переводит строку в дату без учёта языка системы);
      - Loud [switch]: флажок; Quiet [switch] = $true: снятый флажок передаётся как -Quiet:$false;
      - Enabled [bool] = $true: флажок, передаётся -Enabled:$true / -Enabled:$false;
      - Color: ValidateSet с кириллицей и пробелом -> выпадающий список, первым «(по умолчанию)»;
      - Items [string[]]: список через запятую -> -Items 'a','b';
      - Year: умолчание вычисляется -> заполнитель «задаётся скриптом»;
      - Note: попробуйте текст с апострофом и кавычками: O'Brien "в кавычках" - должен дойти без изменений.
    Ожидается: скрипт печатает каждое значение с типом; код выхода 0.
    Ошибки ввода (Count = abc, неверная дата): красная строка ошибки PowerShell, скрипт не выполняется,
    НО код выхода 0 и Shelly показывает успех: ошибка привязки параметров не завершает команду с кодом 1
    (так работает обёртка Shelly «& 'скрипт' ...; exit 0»). Проверьте и сообщите, если это мешает.
#>
# Shelly-Icon: E713
param(
    [Parameter(Mandatory = $true, HelpMessage = 'Ваше имя (обязательно)')]
    [string]$Name,

    [Parameter(HelpMessage = 'Сколько раз поздороваться')]
    [int]$Count = 3,

    [double]$Ratio = 0.5,

    [datetime]$When,

    [switch]$Loud,

    [switch]$Quiet = $true,

    [bool]$Enabled = $true,

    [ValidateSet('Зелёный', 'Красный', 'Синий цвет', 'Default')]
    [string]$Color = 'Зелёный',

    [string[]]$Items = @('один', 'два'),

    [int]$Year = (Get-Date).Year,

    [string]$Note
)

function Show([string]$label, $value) {
    if ($null -eq $value) { Write-Output ("{0,-8} = <null>" -f $label); return }
    $t = $value.GetType().Name
    if ($value -is [array]) { $value = "[" + (($value | ForEach-Object { "'$_'" }) -join ', ') + "] (" + $value.Count + " шт.)" }
    Write-Output ("{0,-8} = {1}  [{2}]" -f $label, $value, $t)
}

Write-Output "Переданные параметры (PSBoundParameters):"
foreach ($k in $PSBoundParameters.Keys) { Write-Output ("  -{0}" -f $k) }
Write-Output ""
Write-Output "Итоговые значения:"
Show 'Name' $Name
Show 'Count' $Count
Show 'Ratio' $Ratio
if ($PSBoundParameters.ContainsKey('When')) { Show 'When' $When.ToString('yyyy-MM-dd HH:mm') } else { Show 'When' $null }
Show 'Loud' $Loud.IsPresent
Show 'Quiet' $Quiet.IsPresent
Show 'Enabled' $Enabled
Show 'Color' $Color
Show 'Items' $Items
Show 'Year' $Year
Show 'Note' $Note
Write-Output ""
$greeting = "Привет, $Name!"
if ($Loud) { $greeting = $greeting.ToUpper() }
for ($i = 1; $i -le [Math]::Min($Count, 20); $i++) {
    if (-not $Quiet) { Write-Output ("{0}. {1}" -f $i, $greeting) }
}
if ($Quiet) { Write-Output "(Quiet включён: приветствия не печатаются. Снимите флажок Quiet.)" }
exit 0
