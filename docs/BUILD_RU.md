# Сборка Shelly из исходников

Для разработчиков. Пользователю достаточно zip из [релизов](https://github.com/grvelvet/Shelly/releases).

## Что нужно

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0): `winget install Microsoft.DotNet.SDK.10`.
  `global.json` берёт самый новый установленный 10.0.x (`rollForward: latestFeature`), 11.x и превью не берёт.
- Windows PowerShell 5.1 (есть в Windows) или PowerShell 7. Для тестов анализатора нужен PowerShell 7 (`pwsh`):
  без него эти тесты пропускаются.
- Ничего больше: ни Cake, ни Nuke, ни MinVer, только `dotnet` и `build.ps1`.

## Команды

Из корня репозитория:

```powershell
.\build.ps1                      # всё: build, test, publish, pack (то же, что в CI)
.\build.ps1 build
.\build.ps1 test                 # artifacts\test-results\Shelly.Core.Tests.trx
.\build.ps1 publish              # artifacts\publish\fd и artifacts\publish\sc
.\build.ps1 publish -Flavor fd   # только малая сборка
.\build.ps1 pack                 # zip и SHA256SUMS.txt в artifacts\dist
.\build.ps1 local                # Shelly.exe в корень репозитория, рядом со Scripts
.\build.ps1 clean                # удалить artifacts\ (и bin\, obj\, publish\ старой раскладки)
.\build.ps1 clean all            # с нуля
.\build.ps1 pack -Version v1.4.1 # как в Release для тега v1.4.1
```

Если PowerShell запрещает скрипты: `powershell -ExecutionPolicy Bypass -File .\build.ps1` или `build.bat`.
`build.bat` оставлен для привычки: без аргументов (или `fd`) это `build.ps1 local`, `sc` это
`build.ps1 publish -Flavor sc`, остальное передаётся в `build.ps1` как есть.

Ошибка любого шага останавливает сборку: в конце `FAILED: ...` и код выхода 1.

## Куда пишется

| Папка | Что |
|---|---|
| `artifacts\bin\`, `artifacts\obj\` | промежуточная сборка (`UseArtifactsOutput`), вместо `bin\` и `obj\` в проектах |
| `artifacts\test-results\` | результаты тестов (TRX) |
| `artifacts\publish\fd\` | `Shelly.exe`, один файл, нужен .NET 10 Desktop Runtime |
| `artifacts\publish\sc\` | `Shelly.exe`, один файл, автономная |
| `artifacts\dist\` | `shelly-<версия>.zip`, `shelly-<версия>-selfcontained.zip`, `SHA256SUMS.txt` |
| `artifacts\release-notes.md` | раздел CHANGELOG этой версии (текст релиза) |

В zip папка `Shelly\`: `Shelly.exe`, `Scripts\` (только файлы под git, если папка есть), `assets\shelly.ico`,
пустая `build\`, `docs\USER_GUIDE.md`, `docs\USER_GUIDE_RU.md`, `docs\FEATURES_RU.md`, `README.md`, `CHANGELOG.md`,
`LICENSE`. Время файлов в zip — время коммита: один коммит даёт одинаковый zip.

## Версия

Одно место: `VersionPrefix` в `Directory.Build.props`.

- Локальная сборка и CI: `1.4.1-dev` (в свойствах exe `1.4.1-dev+<коммит>`), zip `shelly-1.4.1-dev.zip`.
- Релиз: `-Version v1.4.1` (тег). Версия тега должна совпасть с `VersionPrefix`, иначе сборка падает. Суффикс
  тега (`v1.4.1-beta.1`) становится меткой предрелиза.
- В окне Shelly и при проверке обновлений версия `1.4.1`: `AssemblyVersion` всегда `X.Y.Z.0`.

## Релиз

1. В ветке: `VersionPrefix` новой версии, раздел `## [X.Y.Z] - дата` в `CHANGELOG.md`.
2. Pull request в `main`, CI зелёный, слить.
3. Тег на коммите `main`:

   ```powershell
   git switch main; git pull
   git tag v1.3.2
   git push origin v1.3.2
   ```

4. Release (`.github/workflows/release.yml`) проверяет, что тег на `main`, собирает, тестирует, упаковывает и
   публикует релиз: оба zip и `SHA256SUMS.txt`, текст — раздел CHANGELOG и сгенерированный GitHub список PR.
   Тег с `-` (`v1.4.1-rc.1`) публикуется как предрелиз.

Если Release упал: исправить в `main`, удалить тег (`git push origin :refs/tags/v1.3.2`, `git tag -d v1.3.2`)
и поставить заново.

## Зависимости

Версии пакетов — только в `Directory.Packages.props` (central package management), в `.csproj` без `Version`.
Dependabot раз в неделю предлагает обновления NuGet и GitHub Actions. У каждого прогона CI в Summary размер
`Shelly.exe`: смотрите его перед слиянием обновления AvalonEdit.

## Строгость

`TreatWarningsAsErrors`: любое предупреждение компилятора или анализатора — ошибка. Исключение: предупреждения
NuGet об уязвимостях пакетов (NU1901–NU1904) остаются предупреждениями, чтобы новая запись в базе уязвимостей не
ломала релиз; их видно в логе.
