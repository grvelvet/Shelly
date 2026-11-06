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
        ("setUpdateCheck", "Check for updates on startup", "Проверять обновления при запуске"),
        ("setUpdateNote", "Check GitHub for new releases", "Проверять новые релизы на GitHub"),
        ("updNone", "You have the latest version ({0}).", "Установлена последняя версия ({0})."),
        ("updAvailable", "Shelly {0} is available (current: {1}).", "Доступна Shelly {0} (текущая: {1})."),
        ("updDownload", "Download", "Скачать"),
        ("updFail", "Could not check for updates: {0}", "Не удалось проверить обновления: {0}"),
        ("updNoRepo", "Update check is disabled in this build.", "Проверка обновлений отключена в этой сборке."),
        ("setJumpList", "Taskbar Jump List", "Быстрый запуск из панели задач"),
        ("setJumpListNote", "Favorites and recent scripts on right-click in the taskbar", "Избранные и недавние скрипты в контекстном меню значка Shelly"),
        ("jumpFavorites", "Favorites", "Избранное"),
        ("jumpRunTip", "Run {0}", "Запустить {0}"),
        ("setAboutHost", "Running on .NET {0}  ·  Windows build {1}", "Работает на .NET {0}  ·  сборка Windows {1}"),
        ("cHost", "Shelly runs on", "Среда Shelly"),
        ("cHostOk", ".NET {0} ({1}): OK.", ".NET {0} ({1}): OK."),
        ("cAnalyzer", "Script analysis", "Анализ скриптов"),
        ("cAnalyzerOk", "Parsed with {0}.", "Разбор через {0}."),
        ("cAnalyzerNo", "PowerShell not found: safety checks unavailable.", "PowerShell не найден: проверка безопасности недоступна."),
        ("analyzing", "Checking scripts…", "Проверка скриптов…"),
        ("analyzeWait", "Checking {0}…", "Проверка {0}…"),
        ("analyzeFail", "{0}: safety check failed. Run anyway?", "{0}: не удалось выполнить проверку. Запустить?"),
        ("editorTitle", "{0} — Shelly editor", "{0} — редактор Shelly"),
        ("edUnsaved", "Unsaved", "Не сохранено"),
        ("edLineNumbers", "Line numbers", "Номера строк"),
        ("edWrap", "Word wrap", "Перенос строк"),
        ("edFont", "Font size", "Размер шрифта"),
        ("buildExeWait", "Building {0}…", "Сборка {0}…"),
        ("toggleOn", "On", "Вкл"),
        ("toggleOff", "Off", "Выкл"),
        ("singleArgUnknown", "Unknown script: {0}", "Скрипт не найден: {0}"),
        ("aboutRepoTip", "Shelly on GitHub", "Репозиторий Shelly на GitHub"),
        ("engMarkTip", "Change engine in Run settings", "Изменить среду в параметрах запуска"),
        ("capMin", "Minimize", "Свернуть"),
        ("capMax", "Maximize", "Развернуть"),
        ("capRestore", "Restore Down", "Свернуть в окно"),
        ("capClose", "Close", "Закрыть"),
        ("favEmptyTitle", "No favorites yet", "Избранного пока нет"),
        ("favEmptyText", "Click ☆ on any script to pin it here", "Нажмите ☆ рядом со скриптом, чтобы добавить его в избранное"),
        ("goScripts", "Go to scripts", "К скриптам"),
        ("histEmptyTitle", "No runs yet", "Запусков пока нет"),
        ("histEmptyText", "Run results and logs will appear here", "Здесь будут отображаться результаты и логи запусков"),
        ("histNoMatchTitle", "No matching runs", "Совпадений не найдено"),
        ("histNoMatchText", "Try a different search query or filter", "Измените поисковый запрос или фильтр"),
        ("clearSearch", "Clear search", "Сбросить поиск"),
        ("searchEmptyTitle", "No scripts found", "Скрипты не найдены"),
        ("searchEmptyText", "No matches for “{0}”", "По запросу «{0}» ничего не найдено"),
        ("scriptsEmptyTitle", "No scripts yet", "Скриптов пока нет"),
        ("scriptsEmptyText", "Click “+” to create a script, or drag files and folders here", "Нажмите «+» для создания скрипта или перетащите сюда файлы и папки"),
        ("openScriptsFolder", "Open folder", "Открыть папку"),
        ("rowIconTip", "Right-click → Change icon…", "Правый клик → «Сменить значок…»"),
        ("navOpen", "Open navigation", "Открыть меню"),
        ("navClose", "Close navigation", "Закрыть меню"),
        ("searchOpenTip", "Search (Ctrl+K)", "Поиск (Ctrl+K)"),
        ("searchCloseTip", "Close search (Esc)", "Закрыть поиск (Esc)"),
        ("phComputed", "set by the script", "задаёт скрипт"),
        ("rsEngLabel", "PowerShell: {0}", "PowerShell: {0}"),
        ("rsEngAutoShort", "Auto ({0})", "Авто ({0})"),
        ("rsModeLabel", "Mode: {0}", "Режим: {0}"),
        ("rsModeNormal", "Normal", "Обычный"),
        ("rsRememberShort", "Remember for this script", "Запомнить для этого скрипта"),
        ("rsLastEntry", "Last run {0}", "Последний запуск {0}"),
        ("rsModeTip", "Runs inline by default. Administrator mode always opens a separate window.", "По умолчанию вывод отображается внутри Shelly. Права администратора открывают отдельное окно."),
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
        ("filter_danger", "Has safety warnings", "С предупреждениями"),
        ("filter_admin", "Run as admin", "От администратора"),
        ("filter_fav", "Favorites only", "Только избранное"),
        ("filtersChip", "Filters: {0}", "Фильтры: {0}"),
        ("filtersClear", "Clear", "Сбросить"),
        ("filtersClearTip", "Clear all filters", "Сбросить все фильтры"),
        ("filterEmptyTitle", "No matching scripts", "Нет подходящих скриптов"),
        ("filterEmptyText", "Adjust or clear active filters", "Измените или сбросьте активные фильтры"),
        ("clearFilters", "Clear filters", "Сбросить фильтры"),
        ("moreToolsTip", "Sort, groups, view, filter", "Сортировка, группы, вид, фильтр"),
        ("setToastAfter", "Only if longer than", "Только если дольше"),
        ("setToastAfterNote", "Skip notifications for quick runs", "Не беспокоить при быстрых запусках"),
        ("secondsN", "{0} s", "{0} с"),
        ("rDlExec", "Downloads and runs code from the internet: {0}", "Скачивает и выполняет код из интернета: {0}"),
        ("rDlTrusted", "Runs code from trusted source {0}.", "Выполняет код из доверенного источника {0}."),
        ("rDynamic", "Builds commands dynamically ({0}).", "Динамически формирует команды ({0})."),
        ("rNetDelete", "Deletes files or folders via .NET, bypassing Recycle Bin ({0}).", "Удаляет файлы через .NET мимо Корзины ({0})."),
        ("rTask", "Creates or modifies a scheduled task ({0}).", "Создаёт или изменяет задачу планировщика ({0})."),
        ("rLogs", "Clears Windows event logs ({0}).", "Очищает журналы событий Windows ({0})."),
        ("rAddType", "Compiles and loads C# code ({0}).", "Компилирует и загружает код C# ({0})."),
        ("rScriptCall", "Calls another external script ({0}).", "Вызывает другой внешний скрипт ({0})."),
        ("dzHigh", "DANGER", "ОПАСНО"),
        ("dzMedium", "WARNING", "ВНИМАНИЕ"),
        ("dzLow", "INFO", "ИНФО"),
        ("sevHigh", "Danger", "Опасно"),
        ("sevMedium", "Warning", "Внимание"),
        ("scanChecking", "Safety check: in progress…", "Проверка безопасности…"),
        ("scanUnverified", "Safety check: not verified", "Проверка безопасности: не проверено"),
        ("dzChecking", "Safety check is still running. Review the script before running.", "Проверка ещё не завершена. Рекомендуется проверить код перед запуском."),
        ("dzUnverified", "Could not verify this script automatically. Run only if you trust the source.", "Не удалось проверить скрипт автоматически. Запускайте только если доверяете источнику."),
        ("analyzeNone", "{0}: could not verify script. Run anyway?", "{0}: не удалось проверить скрипт. Запустить?"),
        ("dzGuardrail", "Static check only; scripts run with your user permissions.", "Статическая проверка кода; скрипт выполняется с вашими правами."),
        ("dzTrust", "Trust this script version", "Запомнить для этой версии скрипта"),
        ("dzNoTrustRed", "Danger findings require confirmation.", "Опасные команды требуют подтверждения."),
        ("dzReviewed", "I have reviewed it", "Я проверил скрипт"),
        ("dzBuild", "Build", "Собрать"),
        ("dzTrustSource", "Trust source ({0})", "Доверять источнику ({0})"),
        ("dzTrustSourceTip", "Add to Settings > Safety > Trusted sources", "Добавить в Параметры > Безопасность > Доверенные источники"),
        ("srcAdded", "Trusted source added: {0}", "Добавлен доверенный источник: {0}"),
        ("srcRemoved", "Trusted source removed: {0}", "Доверенный источник удалён: {0}"),
        ("srcInvalid", "Invalid HTTPS host or repo: {0}", "Укажите HTTPS-хост или github.com/owner/repo: {0}"),
        ("setSafetyNote", "Warn about risky commands on first run or when a script changes", "Предупреждать о потенциально опасных командах при первом запуске или изменении скрипта"),
        ("safetyOffText", "Shelly will stop checking scripts for dangerous commands before running or building .exe.", "Shelly перестанет проверять скрипты на опасные команды перед запуском и сборкой .exe."),
        ("setTrustedNote", "Scripts trusted for their current content hash", "Скрипты, одобренные для текущей версии файла"),
        ("setSources", "Trusted sources", "Доверенные источники"),
        ("setSourcesNote", "Downloads from these HTTPS hosts do not trigger warnings", "Загрузка кода с этих HTTPS-адресов не вызывает предупреждений"),
        ("setSrcBuiltin", "built-in", "встроенный"),
        ("setSrcBuiltinTip", "{0}\n{1}", "{0}\n{1}"),
        ("setSrcRemove", "Remove", "Удалить"),
        ("setSrcAdd", "Add", "Добавить"),
        ("setSrcAddTip", "HTTPS host (example.com) or github.com/owner/repo", "HTTPS-хост (example.com) или github.com/owner/repo"),
        ("bcHead", "Build “{0}” into an .exe?", "Собрать «{0}» в .exe?"),
        ("bcSub", "Builds a standalone .exe that runs without Shelly.", "Создаёт автономный .exe для запуска без Shelly."),
        ("bcZone", "Downloaded from the internet: {0}", "Скачан из интернета: {0}"),
        ("bcNote", "Shelly safety checks do not run inside standalone .exe files.", "Внутри .exe проверка безопасности Shelly не выполняется."),
        ("bcType", "Type the script name ({0}) or BUILD to confirm:", "Введите имя скрипта ({0}) или BUILD для подтверждения:"),
        ("motwTitle", "Downloaded from the internet", "Файл из интернета"),
        ("motwHead", "“{0}” was downloaded from the internet", "«{0}» загружен из интернета"),
        ("motwSub", "This file was downloaded from the internet and blocked by Windows.", "Файл загружен из интернета и заблокирован Windows."),
        ("motwHost", "Source", "Источник"),
        ("motwReferrer", "Page", "Страница"),
        ("motwAlso", "Also marked", "Также помечен"),
        ("motwNoUrl", "Source URL is not specified.", "Адрес источника не указан."),
        ("motwScan", "Safety check", "Проверка безопасности"),
        ("motwScanClean", "No dangerous commands found.", "Опасных команд не обнаружено."),
        ("motwNote", "“Unblock” removes the Zone.Identifier mark for future runs.", "«Разблокировать» снимет метку Zone.Identifier для следующих запусков."),
        ("motwUnblock", "Unblock", "Разблокировать"),
        ("motwBlocked", "Blocked: {0} is downloaded from the internet", "Заблокирован файл из интернета: {0}"),
        ("motwUnblocked", "Unblocked: {0}", "Разблокирован: {0}"),
        ("motwUnblockFail", "Could not unblock {0}", "Не удалось разблокировать {0}"),
        ("motwMark", "From the internet", "Из интернета"),
        ("motwMarkTip", "Blocked by Windows (Mark of the Web)", "Заблокирован Windows (Mark of the Web)"),
        ("motwNoteZone", "Trusted or local network zone: {0}", "Доверенная или локальная зона: {0}"),
        ("discTitle", "Before you start", "Перед началом"),
        ("discHead", "Important safety notice", "Важная информация"),
        ("discRights", "Scripts run with your user permissions (or full system access as administrator).", "Скрипты выполняются с правами вашего пользователя (или с полным доступом от администратора)."),
        ("discSandbox", "Shelly is a launcher, not an isolated sandbox.", "Shelly — это лаунчер, а не изолированная песочница."),
        ("discCheck", "Built-in safety checks help spot risky commands but cannot guarantee safety.", "Встроенная проверка помогает заметить опасные команды, но не гарантирует полную безопасность."),
        ("discTrust", "Only run scripts from sources you trust.", "Запускайте только скрипты из доверенных источников."),
        ("discLiability", "You are responsible for the scripts you run.", "Вы сами отвечаете за действия запускаемых скриптов."),
        ("discAccept", "I understand", "Понятно"),
        ("discExit", "Exit", "Выйти"),
        ("logRunError", "Script error: {0}  ·  exit code {1}", "Ошибка скрипта: {0}  ·  код выхода {1}"),
        ("logRunWindow", "Ran in a separate window.", "Выполнялся в отдельном окне."),
        // 1.1.2: Favorites sort
        ("favSortManual", "Manual", "Вручную"),
        ("favSortName", "By name", "По имени"),
        ("favSortLastRun", "By last run", "По последнему запуску"),
        ("favSortRunCount", "By run count", "По частоте запусков"),
        ("favSortType", "By type", "По типу"),
        ("favMoveEarlier", "Move earlier", "Переместить раньше"),
        ("favMoveLater", "Move later", "Переместить позже"),
    };
}
