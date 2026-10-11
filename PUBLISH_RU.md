# Как опубликовать на GitHub

## Перед публикацией

1. Авторы уже вписаны: `LICENSE` (Copyright (c) 2026 nah and Vika), README и свойства `Shelly.exe`.
2. Репозиторий уже задан: `<ShellyGitHubRepo>grvelvet/shelly</ShellyGitHubRepo>` в `Directory.Build.props`.
   **Репозиторий на GitHub должен называться `shelly` и лежать у пользователя `grvelvet`**
   (https://github.com/grvelvet/shelly). От этого зависят проверка обновлений и ссылки в «Параметры → О программе».
   Если имя или владелец другие, поменяй это значение (и ссылки в README и внизу `CHANGELOG.md`)
   и **пересобери** Shelly: значение зашивается в exe при сборке.
3. Сделай скриншот Shelly (Win+Shift+S) и сохрани как `docs/screenshot.png`. Без него в README будет пустая картинка.
4. Проверь, что в папке нет личного: `launcher-settings.json`, папки `launcher-history`, `assets\script-icons`,
   собранных `.exe`, папок `bin`, `obj`, `publish`, своих скриптов с паролями или путями вида `C:\Users\<имя>`.
   `.gitignore` не даст их закоммитить.

## Сборка exe для релиза

Нужен [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`winget install Microsoft.DotNet.SDK.10`).
В папке репозитория:

```bat
dotnet test tests\Shelly.Core.Tests -c Release
dotnet publish src\Shelly -c Release -r win-x64 -o publish\fd
dotnet publish src\Shelly -c Release -r win-x64 -p:SelfContained=true -o publish\sc
```

- `publish\fd\Shelly.exe` (около 2 МБ) — основная сборка, нужна среда .NET 10 Desktop Runtime. Если её нет, Windows
  при запуске сама покажет окно со ссылкой на скачивание.
- `publish\sc\Shelly.exe` (около 70 МБ) — всё внутри, ничего ставить не нужно.

Собери два архива с одинаковой структурой:

```
Shelly\
  Shelly.exe
  Scripts\ (usd_rub.ps1, Examples\ (Hello with parameters.ps1, Chris Titus WinUtil.ps1), System\)
  assets\shelly.ico
  build\.keep
  docs\USER_GUIDE.md, docs\USER_GUIDE_RU.md
  README.md, CHANGELOG.md, LICENSE
```

Назови их `shelly-X.Y.Z.zip` (с маленьким exe) и `shelly-X.Y.Z-selfcontained.zip`, например `shelly-1.0.11.zip` и
`shelly-1.0.11-selfcontained.zip`.

## Контрольные суммы

Посчитай SHA-256 обоих архивов и сохрани в `SHA256SUMS.txt` (формат `sha256sum`: хэш, два пробела, имя файла).
В PowerShell, в папке с архивами:

```powershell
Get-ChildItem shelly-1.0.11*.zip | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_ -Algorithm SHA256).Hash.ToLower(), $_.Name } |
    Set-Content SHA256SUMS.txt -Encoding ascii
```

(в Linux/WSL: `sha256sum shelly-1.0.11*.zip > SHA256SUMS.txt`). Проверь, что в файле строка на каждый архив, и приложи
`SHA256SUMS.txt` к релизу вместе с архивами. Пересчитывай его после любой пересборки: старый хэш уже не подойдёт.

## Вариант A: через сайт GitHub (без Git)

1. Зайди на github.com → **New repository**.
2. Имя: `shelly` (именно так: на него ссылается проверка обновлений). **Не** ставь галочки «Add a README», «.gitignore», «license»: репозиторий должен быть пустым.
3. Нажми **Create repository**, затем на странице репозитория ссылку **uploading an existing file**.
4. Открой папку `shelly` в Проводнике, выдели **всё её содержимое** (не саму папку) и перетащи в окно браузера.
   Проверь, что `.gitignore` и `.gitattributes` попали в список. Папки `bin`/`obj`/`publish`, если ты уже собирал,
   не перетаскивай.
5. Внизу напиши сообщение, например `Shelly 1.0.11`, и нажми **Commit changes**.

## Вариант B: через Git или GitHub Desktop

Создай пустой репозиторий `shelly` в аккаунте `grvelvet` (как в пунктах 1–3 варианта A), затем в папке `shelly`:

```bat
git init
git add .
git commit -m "Shelly 1.0.11"
git branch -M main
git remote add origin https://github.com/grvelvet/shelly.git
git push -u origin main
```

В **GitHub Desktop**: *File → Add local repository* → выбери папку `shelly` → «create a repository» →
*Publish repository* (сними галочку «Keep this code private», если нужен открытый репозиторий).

## Релиз (важно для проверки обновлений)

*Releases → Draft a new release*:

- тег **`v1.0.11`** (формат `vX.Y.Z`: Shelly сравнивает его со своей версией),
- заголовок `Shelly 1.0.11`, в описание скопируй раздел 1.0.11 из `CHANGELOG.md` и добавь строку
  «Проверка: SHA-256 в `SHA256SUMS.txt`, `Get-FileHash .\shelly-1.0.11.zip`»,
- приложи оба zip **и `SHA256SUMS.txt`**.

Неподписанный exe может вызывать предупреждение SmartScreen («Система Windows защитила ваш компьютер» → «Подробнее» →
«Выполнить в любом случае»). Это нормально для новых программ без цифровой подписи; в README написано, как сначала
сверить контрольную сумму.

Архив, скачанный браузером и распакованный Проводником, передаёт пометку «скачан из интернета» (Mark of the Web) всем
файлам, в том числе примерам в `Scripts`. Shelly 1.0.11 не запускает помеченные скрипты, пока их не разблокировать —
так и задумано. В README предложено снять пометку с архива до распаковки (`Unblock-File`).

## Безопасность

- `SECURITY.md` лежит в корне репозитория: GitHub показывает его на вкладке **Security**.
- Включи приём сообщений об уязвимостях: *Settings → Code security* (или *Security*) → **Private vulnerability
  reporting → Enable**. Тогда кнопка **Report a vulnerability** на вкладке Security создаёт закрытое сообщение
  (Security Advisory), видимое только владельцам репозитория. На это ссылается `SECURITY.md`.

## Версии

Номер версии хранится в одном месте: `<Version>` в `Directory.Build.props` (его видно в «Параметры → О программе»,
в окне «Совместимость» и в свойствах `Shelly.exe`). При новом выпуске меняй его, добавляй раздел в `CHANGELOG.md`,
ставь тег `vX.Y.Z` и создавай релиз.

## Оформление репозитория

На странице репозитория нажми ⚙ рядом с **About**:

- **Description:** `Shelly — a Windows 11-style launcher for PowerShell scripts: run, edit, check and build to .exe. .NET 10 + WPF Fluent, portable.`
- **Topics:** `powershell`, `wpf`, `dotnet`, `windows-11`, `fluent`, `launcher`, `script-manager`, `shelly`

## Обновление с 0.9.8 (если репозиторий уже опубликован)

Удали из корня `Launcher.ps1`, `Launcher.bat` и `Build-Exe.bat` (они теперь в `legacy\`), добавь `src\`, `tests\`,
`legacy\`, `Shelly.slnx`, `Directory.Build.props` и замени `README.md`, `CHANGELOG.md`, `PUBLISH_RU.md`, `.gitignore`,
`.gitattributes`. Затем:

```bat
git add -A
git commit -m "Shelly 1.0.11"
git tag v1.0.11
git push && git push --tags
```

и создай релиз, как описано выше.
