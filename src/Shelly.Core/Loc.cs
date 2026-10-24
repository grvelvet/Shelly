using System.Reflection;
using System.Text.Json;

namespace Shelly.Core;

/// <summary>UI strings in English and Russian. The base texts are in Resources/Strings.json;
/// the keys in <see cref="Extra"/> override them. Missing keys fall back to English, then to the key itself.</summary>
public static class Loc
{
    static Dictionary<string, Dictionary<string, string>>? tables;
    static Dictionary<string, Dictionary<string, string>> Tables => tables ??= Load();
    public static string Language { get; set; } = "en";
    public static event Action? LanguageChanged;

    public static void SetLanguage(string lang)
    {
        lang = lang == "ru" ? "ru" : "en";
        if (lang == Language) return;
        Language = lang;
        LanguageChanged?.Invoke();
    }

    /// <summary>The UI language for a culture: Russian for ru-*, English for everything else.</summary>
    public static string LanguageFor(System.Globalization.CultureInfo culture) => culture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";

    /// <summary>The language of the system, for a first start: the user's Windows display language, or the installed one
    /// when that is not available (invariant culture).</summary>
    public static string SystemLanguage()
    {
        var c = System.Globalization.CultureInfo.CurrentUICulture;
        if (c.Equals(System.Globalization.CultureInfo.InvariantCulture)) c = System.Globalization.CultureInfo.InstalledUICulture;
        return LanguageFor(c);
    }

    public static string T(string key)
    {
        if (Tables.TryGetValue(Language, out var t) && t.TryGetValue(key, out var v)) return v;
        if (Tables["en"].TryGetValue(key, out v)) return v;
        return key;
    }

    public static string F(string key, params object?[] args)
    {
        try { return string.Format(T(key), args); } catch (FormatException) { return T(key); }
    }

    internal static bool Has(string key) => Tables["en"].ContainsKey(key);

    static Dictionary<string, Dictionary<string, string>> Load()
    {
        var res = new Dictionary<string, Dictionary<string, string>>
        {
            ["en"] = new(StringComparer.Ordinal), ["ru"] = new(StringComparer.Ordinal)
        };
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shelly.Strings.json"))
        {
            if (s != null)
            {
                var doc = JsonDocument.Parse(s);
                foreach (var lang in doc.RootElement.EnumerateObject())
                {
                    if (!res.TryGetValue(lang.Name, out var table)) continue;
                    foreach (var p in lang.Value.EnumerateObject())
                        table[p.Name] = (p.Value.GetString() ?? "").Replace("`n", "\n");
                }
            }
        }
        foreach (var (key, en, ru) in Extra)
        {
            res["en"][key] = en;
            res["ru"][key] = ru;
        }
        return res;
    }

    // Keys and texts that override Resources/Strings.json
    static readonly (string Key, string En, string Ru)[] Extra =
    {
        ("navScripts", "Scripts", "Скрипты"),
        ("navFavorites", "Favorites", "Избранное"),
        ("navHistory", "History", "История"),
        ("navSettings", "Settings", "Параметры"),
        ("navMenuTip", "Collapse or expand the menu (Ctrl+M)", "Свернуть или развернуть меню (Ctrl+M)"),
        ("newFolderTip", "New folder", "Новая папка"),
        ("omniHint", "Search scripts, or type > for commands", "Поиск скриптов, > для команд"),
        ("omniTip", "Search by name, folder and description (Ctrl+K). Type > for commands.", "Поиск по имени, папке и описанию (Ctrl+K). Введи > для команд."),
        ("openBuildFolder", "Build folder", "Папка build"),
        ("openShellyFolder", "Shelly folder", "Папка Shelly"),
        ("ctxRun", "Run", "Запустить"),
        ("ctxRunWindow", "Run in separate window", "Запустить в отдельном окне"),
        ("ctxRunAdmin", "Run as administrator", "Запустить от администратора"),
        ("ctxIcon", "Change icon…", "Сменить значок…"),
        ("ctxIconReset", "Default icon", "Значок по умолчанию"),
        ("ctxOpenFolder", "Open file location", "Открыть расположение файла"),
        ("ctxCopyPath", "Copy path", "Копировать путь"),
        ("iconFilter", "Icons and images (*.ico;*.png)|*.ico;*.png", "Значки и картинки (*.ico;*.png)|*.ico;*.png"),
        ("iconSet", "Icon changed: {0}", "Значок изменён: {0}"),
        ("iconReset", "Default icon: {0}", "Значок по умолчанию: {0}"),
        ("iconFail", "Could not use this image: {0}", "Не удалось использовать картинку: {0}"),
        ("histTitle", "History", "История"),
        ("histFilterAll", "All runs", "Все запуски"),
        ("histFilterOk", "Successful", "Успешные"),
        ("histFilterFail", "Failed", "С ошибкой"),
        ("histFilterStopped", "Stopped", "Остановленные"),
        ("histFilterWindow", "Separate window", "В отдельном окне"),
        ("histFilterTip", "Filter runs", "Фильтр запусков"),
        ("histDelete", "Delete from history", "Удалить из истории"),
        ("histOpenScript", "Open script", "Открыть скрипт"),
        ("outCopied", "Output copied", "Вывод скопирован"),
        ("setUpdates", "Updates", "Обновления"),
        ("setUpdateCheck", "Check for updates at startup", "Проверять обновления при запуске"),
        ("setUpdateNote", "Asks GitHub Releases about once a day (at most every 20 hours). Nothing is downloaded or installed without you.", "Примерно раз в день (не чаще чем раз в 20 часов) спрашивает GitHub Releases. Без тебя ничего не скачивается и не ставится."),
        ("updNone", "You have the latest version ({0}).", "У тебя последняя версия ({0})."),
        ("updAvailable", "Shelly {0} is available (you have {1}).", "Доступна Shelly {0} (у тебя {1})."),
        ("updDownload", "Download", "Скачать"),
        ("updFail", "Could not check for updates: {0}", "Не удалось проверить обновления: {0}"),
        ("updNoRepo", "The update check is off: no GitHub repository is set in this build.", "Проверка обновлений выключена: в этой сборке не указан репозиторий GitHub."),
        ("setJumpList", "Favorites in the taskbar jump list", "Избранное в списке переходов на панели задач"),
        ("setJumpListNote", "Right-click Shelly on the taskbar to run a favorite", "Правый клик по Shelly на панели задач запускает избранный скрипт"),
        ("jumpFavorites", "Favorites", "Избранное"),
        ("jumpRunTip", "Run {0}", "Запустить {0}"),
        ("setAboutHost", "Running on .NET {0}  ·  Windows build {1}", "Работает на .NET {0}  ·  сборка Windows {1}"),
        ("cHost", "Shelly runs on", "Shelly работает на"),
        ("cHostOk", ".NET {0} ({1}): OK.", ".NET {0} ({1}): OK."),
        ("cAnalyzer", "Script analysis", "Анализ скриптов"),
        ("cAnalyzerOk", "Parsed with {0}.", "Разбор через {0}."),
        ("cAnalyzerNo", "No PowerShell found: parameters are read with a simple parser, safety checks are unavailable.", "PowerShell не найден: параметры читаются упрощённым разбором, проверка безопасности недоступна."),
        ("analyzing", "Checking scripts…", "Проверяю скрипты…"),
        ("analyzeWait", "Checking {0} before running…", "Проверяю {0} перед запуском…"),
        ("analyzeFail", "{0}: the safety check could not run. Run anyway?", "{0}: проверка безопасности не выполнилась. Всё равно запустить?"),
        ("editorTitle", "{0} — Shelly editor", "{0} — редактор Shelly"),
        ("edUnsaved", "Unsaved", "Не сохранено"),
        ("edLineNumbers", "Line numbers", "Номера строк"),
        ("edWrap", "Word wrap", "Перенос строк"),
        ("edFont", "Font size", "Размер шрифта"),
        ("buildExeWait", "Building {0}…", "Собираю {0}…"),
        ("toggleOn", "On", "Вкл"),
        ("toggleOff", "Off", "Выкл"),
        ("singleArgUnknown", "Unknown script: {0}", "Скрипт не найден: {0}"),
        ("aboutRepoTip", "Shelly on GitHub: source, releases, issues", "Shelly на GitHub: исходники, релизы, вопросы"),
        ("engMarkTip", "Change the engine in Run settings (gear button).", "Движок меняется в параметрах запуска (кнопка с шестерёнкой)."),
        ("capMin", "Minimize", "Свернуть"),
        ("capMax", "Maximize", "Развернуть"),
        ("capRestore", "Restore Down", "Свернуть в окно"),
        ("capClose", "Close", "Закрыть"),
        ("favEmptyTitle", "No favorites yet", "Избранного пока нет"),
        ("favEmptyText", "Star a script (Ctrl+D or right-click → Add to favorites) to keep it here and in the tray menu.", "Отметь скрипт звёздочкой (Ctrl+D или правый щелчок → «В избранное»), и он будет здесь и в меню в трее."),
        ("goScripts", "Go to scripts", "К скриптам"),
        ("histEmptyTitle", "No runs yet", "Запусков пока нет"),
        ("histEmptyText", "Every run shows up here with its result, time and output.", "Здесь появляется каждый запуск: результат, время и вывод."),
        ("histNoMatchTitle", "No matching runs", "Подходящих запусков нет"),
        ("histNoMatchText", "Try other words or show all results.", "Попробуй другие слова или покажи все результаты."),
        ("clearSearch", "Clear search", "Сбросить поиск"),
        ("searchEmptyTitle", "No scripts found", "Скрипты не найдены"),
        ("searchEmptyText", "Nothing matches “{0}”. Search looks in names, folders and descriptions.", "Ничего не подходит под «{0}». Поиск идёт по именам, папкам и описаниям."),
        ("scriptsEmptyTitle", "No scripts yet", "Скриптов пока нет"),
        ("scriptsEmptyText", "Create a script, or put .ps1, .py, .bat or .cmd files into the Scripts folder.", "Создай скрипт или положи файлы .ps1, .py, .bat или .cmd в папку Scripts."),
        ("openScriptsFolder", "Open folder", "Открыть папку"),
        ("rowIconTip", "Right-click → Change icon… to use your own picture", "Правый щелчок → «Сменить значок…», чтобы поставить свою картинку"),
        ("navOpen", "Open navigation", "Открыть навигацию"),
        ("navClose", "Close navigation", "Закрыть навигацию"),
        ("searchOpenTip", "Search (Ctrl+K)", "Поиск (Ctrl+K)"),
        ("searchCloseTip", "Close search (Esc)", "Закрыть поиск (Esc)"),
        ("phComputed", "set by the script", "задаёт скрипт"),
        ("rsEngLabel", "PowerShell: {0}", "PowerShell: {0}"),
        ("rsEngAutoShort", "Auto ({0})", "Авто ({0})"),
        ("rsModeLabel", "Mode: {0}", "Режим: {0}"),
        ("rsModeNormal", "Normal", "Обычный"),
        ("rsRememberShort", "Remember for this script", "Запомнить для скрипта"),
        ("rsLastEntry", "Last run {0}", "Последний запуск {0}"),
        ("rsModeTip", "Normal: output in Shelly. Separate window: a PowerShell console. As administrator: a console with elevation (UAC).", "Обычный: вывод в Shelly. В отдельном окне: консоль PowerShell. От администратора: консоль с повышением прав (UAC)."),
        ("rsRequired", "Required", "Обязательный"),
        // 1.0.10: header toolbar (sort, collapse/expand all, view, filter)
        ("sortTip", "Sort: {0}", "Сортировка: {0}"),
        ("sortName", "Name", "Имя"),
        ("sortModified", "Date modified", "Дата изменения"),
        ("sortLastRun", "Last run", "Последний запуск"),
        ("sortRunCount", "Run count", "Число запусков"),
        ("sortAsc", "Ascending", "По возрастанию"),
        ("sortDesc", "Descending", "По убыванию"),
        ("sortMenu", "Sort", "Сортировка"),
        ("collapseAllTip", "Collapse all groups (Ctrl+Shift+[)", "Свернуть все группы (Ctrl+Shift+[)"),
        ("expandAllTip", "Expand all groups (Ctrl+Shift+])", "Развернуть все группы (Ctrl+Shift+])"),
        ("collapseAll", "Collapse all groups", "Свернуть все группы"),
        ("expandAll", "Expand all groups", "Развернуть все группы"),
        ("viewTip", "View: {0}", "Вид: {0}"),
        ("viewMenu", "View", "Вид"),
        ("viewCards", "Cards", "Карточки"),
        ("viewCompact", "Compact list", "Компактный список"),
        ("filterTip", "Filter", "Фильтр"),
        ("filterTipOn", "Filter: {0}", "Фильтр: {0}"),
        ("filterMenu", "Filter", "Фильтр"),
        ("filter_ran", "Ran before", "Уже запускались"),
        ("filter_never", "Never run", "Ни разу не запускались"),
        ("filter_ps51", "Needs PowerShell 5.1", "Нужен PowerShell 5.1"),
        ("filter_danger", "Has safety warnings", "Есть предупреждения безопасности"),
        ("filter_admin", "Run as admin (remembered mode)", "Запуск от администратора (запомненный режим)"),
        ("filter_fav", "Favorites only", "Только избранное"),
        ("filtersChip", "Filters: {0}", "Фильтры: {0}"),
        ("filtersClear", "Clear", "Сбросить"),
        ("filtersClearTip", "Clear all filters", "Сбросить все фильтры"),
        ("filterEmptyTitle", "No scripts match the filters", "Нет скриптов под эти фильтры"),
        ("filterEmptyText", "Turn some filters off or clear them all.", "Отключите часть фильтров или сбросьте их все."),
        ("clearFilters", "Clear filters", "Сбросить фильтры"),
        ("moreToolsTip", "More: sort, groups, view, filter", "Ещё: сортировка, группы, вид, фильтр"),
        ("setToastAfter", "Only if longer than", "Только если дольше"),
        ("setToastAfterNote", "Shorter runs don't notify. Inline output doesn't notify while the Shelly window is active; scheduled runs notify regardless of length", "Более короткие запуски не уведомляют. Встроенный вывод не уведомляет, пока окно Shelly активно; запуски по расписанию уведомляют при любой длительности"),
        ("secondsN", "{0} s", "{0} с"),
        ("rDlExec", "Downloads and runs code from the internet: {0}", "Скачивает и выполняет код из интернета: {0}"),
        ("rDlTrusted", "Runs code from trusted source {0}. Trusting a source means trusting whatever it serves at run time.", "Выполняет код из доверенного источника {0}. Доверять источнику — значит доверять всему, что он отдаст в момент запуска."),
        ("rDynamic", "The script builds commands dynamically; Shelly can't verify what it runs ({0}).", "Скрипт собирает команды на ходу; Shelly не может проверить, что он выполняет ({0})."),
        ("rNetDelete", "Deletes files or folders through .NET, without the Recycle Bin ({0}).", "Удаляет файлы или папки через .NET, мимо Корзины ({0})."),
        ("rTask", "Creates or changes a scheduled task ({0}). Tasks run later on their own, possibly as SYSTEM.", "Создаёт или меняет задание планировщика ({0}). Задания запускаются сами позже, возможно от имени SYSTEM."),
        ("rLogs", "Clears Windows event logs ({0}). This erases the traces of what happened on the computer.", "Очищает журналы событий Windows ({0}). Так стираются следы того, что происходило на компьютере."),
        ("rAddType", "Compiles and loads C# code ({0}). Read the code in the script: it runs with the script's rights.", "Компилирует и загружает код на C# ({0}). Прочитай этот код в скрипте: он выполняется с правами скрипта."),
        ("rScriptCall", "Runs another script ({0}). Shelly checks only this file, not the called one.", "Запускает другой скрипт ({0}). Shelly проверяет только этот файл, не вызываемый."),
        ("dzHigh", "DANGER", "ОПАСНО"),
        ("dzMedium", "WARNING", "ВНИМАНИЕ"),
        ("dzLow", "INFO", "ИНФО"),
        ("sevHigh", "Danger", "Опасно"),
        ("sevMedium", "Warning", "Внимание"),
        ("scanChecking", "Safety check: checking…", "Проверка безопасности: проверяю…"),
        ("scanUnverified", "Safety check: not verified (PowerShell could not check the script)", "Проверка безопасности: не проверено (PowerShell не смог проверить скрипт)"),
        ("dzChecking", "PowerShell has not finished checking the script yet. These are the results of Shelly's simpler built-in scanner.", "PowerShell ещё не закончил проверку. Это результаты более простого встроенного сканера Shelly."),
        ("dzUnverified", "PowerShell could not check this script. Shelly's simpler built-in scanner was used; it can miss things.", "PowerShell не смог проверить этот скрипт. Использован более простой встроенный сканер Shelly — он может что-то пропустить."),
        ("analyzeNone", "{0}: no PowerShell could check the script. Run anyway?", "{0}: ни одна версия PowerShell не смогла проверить скрипт. Всё равно запустить?"),
        ("dzGuardrail", "This check reads the script before it runs. It is a guardrail, not a sandbox: it can miss things, and the script runs with your rights.", "Проверка читает скрипт перед запуском. Это ограждение, а не песочница: она может что-то пропустить, а скрипт выполняется с твоими правами."),
        ("dzTrust", "Don't warn again for this version of the script (until the file changes)", "Больше не предупреждать для этой версии скрипта (пока файл не изменится)"),
        ("dzNoTrustRed", "Scripts with danger findings are checked before every run; they cannot be marked as trusted.", "Скрипты с опасными командами проверяются перед каждым запуском; пометить их доверенными нельзя."),
        ("dzReviewed", "I have reviewed it", "Я проверил скрипт"),
        ("dzBuild", "Build", "Собрать"),
        ("dzTrustSource", "Trust this source ({0})", "Доверять источнику ({0})"),
        ("dzTrustSourceTip", "Adds the source to Settings > Safety > Trusted sources. Shelly then shows its scripts as info, but they still run whatever the source serves.", "Добавляет источник в Параметры > Безопасность > Доверенные источники. Его скрипты будут отмечаться как информация, но выполнится всё, что отдаст источник."),
        ("srcAdded", "Trusted source added: {0}", "Добавлен доверенный источник: {0}"),
        ("srcRemoved", "Trusted source removed: {0}", "Доверенный источник удалён: {0}"),
        ("srcInvalid", "Not added: {0}. Use an HTTPS host (example.com) or, on shared hosts like github.com, host/owner/repo.", "Не добавлено: {0}. Укажи HTTPS-хост (example.com) или, на общих хостах вроде github.com, хост/владелец/репозиторий."),
        ("setSafetyNote", "Shelly reads scripts for commands that delete, format, weaken protection or download and run code. A guardrail, not a sandbox", "Shelly ищет в скриптах команды, которые удаляют, форматируют, ослабляют защиту или скачивают и запускают код. Это ограждение, а не песочница"),
        ("safetyOffText", "Shelly will no longer warn about scripts with warning findings. Administrator runs still ask.\n\nScripts with danger findings and scripts downloaded from the internet are still checked before every run. The badges in the list stay.", "Shelly перестанет предупреждать о скриптах с замечаниями «Внимание». Запуск от администратора по-прежнему спрашивает.\n\nСкрипты с опасными командами и скачанные из интернета по-прежнему проверяются перед каждым запуском. Значки в списке останутся."),
        ("setTrustedNote", "Scripts with warnings you chose not to be warned about again (until the file changes). Danger scripts can't be trusted", "Скрипты с замечаниями, о которых ты попросил больше не предупреждать (пока файл не изменится). Опасным скриптам доверять нельзя"),
        ("setSources", "Trusted sources", "Доверенные источники"),
        ("setSourcesNote", "Code downloaded and run from these HTTPS sources is shown as info instead of danger. Trusting a source means trusting whatever it serves at run time", "Код, который скачивается и выполняется из этих HTTPS-источников, отмечается как информация, а не как опасность. Доверять источнику — значит доверять всему, что он отдаст в момент запуска"),
        ("setSrcBuiltin", "built-in", "встроенный"),
        ("setSrcBuiltinTip", "{0}\nBuilt into Shelly: this is the install command documented by the project itself:\n{1}", "{0}\nВстроен в Shelly: это команда установки из документации самого проекта:\n{1}"),
        ("setSrcRemove", "Remove", "Удалить"),
        ("setSrcAdd", "Add", "Добавить"),
        ("setSrcAddTip", "An HTTPS host (example.com, also covers its subdomains) or, on shared hosts, host/owner/repo (github.com/owner/repo). Plain http is never trusted", "HTTPS-хост (example.com, вместе с поддоменами) или, на общих хостах, хост/владелец/репозиторий (github.com/owner/repo). Обычный http не доверяется никогда"),
        ("bcHead", "Build “{0}” into an .exe?", "Собрать «{0}» в .exe?"),
        ("bcSub", "The script has warnings, was downloaded from the internet or could not be checked. The .exe runs it without Shelly's checks.", "В скрипте есть замечания, он скачан из интернета или его не удалось проверить. Собранный .exe запускает его без проверок Shelly."),
        ("bcZone", "Downloaded from the internet: {0}", "Скачан из интернета: {0}"),
        ("bcNote", "Don't distribute .exe files built from scripts you haven't verified: people who run them get no warning.", "Не распространяй .exe, собранные из непроверенных скриптов: тот, кто их запустит, не получит предупреждения."),
        ("bcType", "To build, type the script's name ({0}) or BUILD:", "Чтобы собрать, введи имя скрипта ({0}) или BUILD:"),
        ("motwTitle", "Downloaded from the internet", "Скачан из интернета"),
        ("motwHead", "“{0}” was downloaded from the internet", "«{0}» скачан из интернета"),
        ("motwSub", "Windows marked this file as downloaded (Mark of the Web). Shelly doesn't run or build it until you unblock it.", "Windows пометила этот файл как скачанный (Mark of the Web). Shelly не запускает и не собирает его, пока ты его не разблокируешь."),
        ("motwHost", "Downloaded from", "Откуда скачан"),
        ("motwReferrer", "Page", "Страница"),
        ("motwAlso", "Also marked (loaded by this script)", "Тоже помечен (загружается этим скриптом)"),
        ("motwNoUrl", "The mark doesn't say where the file came from.", "В пометке не указано, откуда файл."),
        ("motwScan", "Safety check", "Проверка безопасности"),
        ("motwScanClean", "Nothing found. That doesn't prove the script is safe: read it before you unblock it.", "Ничего не найдено. Это не доказывает, что скрипт безопасен: прочитай его, прежде чем разблокировать."),
        ("motwNote", "Unblock removes the mark from the file (like Unblock in the file's Properties). Then start the script again: it goes through the normal safety check.", "«Разблокировать» снимает пометку с файла (как «Разблокировать» в свойствах файла). Потом запусти скрипт снова: он пройдёт обычную проверку безопасности."),
        ("motwUnblock", "Unblock", "Разблокировать"),
        ("motwBlocked", "Not run: {0} was downloaded from the internet and is still blocked", "Не запущен: {0} скачан из интернета и ещё заблокирован"),
        ("motwUnblocked", "{0} is unblocked. Run it again to start it", "{0} разблокирован. Запусти его снова"),
        ("motwUnblockFail", "Could not remove the mark from {0}", "Не удалось снять пометку с {0}"),
        ("motwMark", "Downloaded from the internet", "Скачан из интернета"),
        ("motwMarkTip", "Blocked until you unblock it: Run shows the details", "Заблокирован, пока ты его не разблокируешь: подробности — по кнопке запуска"),
        ("motwNoteZone", "From the local network or a trusted site: {0}", "Из локальной сети или с надёжного сайта: {0}"),
        ("discTitle", "Before you start", "Перед началом"),
        ("discHead", "Shelly runs scripts, it doesn't make them safe", "Shelly запускает скрипты, но не делает их безопасными"),
        ("discRights", "Scripts run with your user rights. In administrator mode they get full control of the computer (after the UAC prompt).", "Скрипты выполняются с правами твоей учётной записи. В режиме администратора они получают полный контроль над компьютером (после запроса UAC)."),
        ("discSandbox", "Shelly is not a sandbox: it doesn't isolate or restrict scripts.", "Shelly — не песочница: он не изолирует и не ограничивает скрипты."),
        ("discCheck", "The safety check reads the script before it runs. It is a guardrail and can miss things.", "Проверка безопасности читает скрипт перед запуском. Это ограждение, и она может что-то пропустить."),
        ("discTrust", "Run only scripts you trust and have read.", "Запускай только те скрипты, которым доверяешь и которые прочитал."),
        ("discLiability", "The authors of Shelly are not responsible for damage caused by scripts you run.", "Авторы Shelly не несут ответственности за ущерб от запущенных тобой скриптов."),
        ("discAccept", "I understand", "Понятно"),
        ("discExit", "Exit", "Выйти"),
        ("logRunError", "Script error: {0}  ·  exit code {1}", "Ошибка скрипта: {0}  ·  код выхода {1}"),
        ("logRunWindow", "The script ran in its own window, so its error text was not captured.", "Скрипт работал в отдельном окне, поэтому текст ошибки не сохранён."),
        // 1.1.2: Favorites sort
        ("favSortManual", "Manual", "Вручную"),
        ("favSortName", "By name", "По имени"),
        ("favSortLastRun", "By last run", "По последнему запуску"),
        ("favSortRunCount", "By run count", "По частоте запусков"),
        ("favSortType", "By type (PowerShell, Python, Batch, then custom)", "По типу (PowerShell, Python, Batch, затем свои)"),
        ("favMoveEarlier", "Move earlier", "Переместить раньше"),
        ("favMoveLater", "Move later", "Переместить позже"),
    };
}
