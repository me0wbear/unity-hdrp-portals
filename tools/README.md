# Проверки Portal Player

`check.sh` собирает выбранную проверку и принимает только завершённый результат,
связанный с текущим запуском, проектом и полным Git SHA. Стартовый диагностический
тег, пустой лог и даже `Passed` при ненулевом коде Player не означают успех.

## Требования и запуск

- Windows, Git for Windows Bash с `cygpath`, `git` и стандартными утилитами.
- Windows PowerShell 5.1 (`powershell.exe`) либо PowerShell 7 (`pwsh.exe`) в `PATH`.
  Runner сначала ищет Windows PowerShell. Без verifier или PowerShell запуск запрещён.
- По умолчанию Unity `6000.5.9f1` в `C:/Program Files/Unity/Hub/Editor/6000.5.9f1/Editor/Unity.exe`.
- Проект должен содержать `ProjectSettings/ProjectVersion.txt`, иметь Git-коммит
  и не быть открыт в Unity. При наличии `Temp/UnityLockfile` runner останавливается,
  ничего не завершает принудительно и не удаляет этот файл.

Из корня проекта в Git Bash:

```bash
bash tools/check.sh Seam
bash tools/check.sh Seam PORTAL_NODEPTH=1
bash tools/check.sh Performance
```

Из каталога `tools` работает `bash check.sh Seam`. Проект определяется по расположению
скрипта, а не по текущему каталогу. Для другой рабочей копии задайте переменную перед командой:

```bash
PORTAL_PROJECT='C:/Users/faust/Documents/PortalsHDRP-depth-crossing' bash tools/check.sh Seam
```

`PORTAL_UNITY` аналогично задаёт альтернативный исполняемый файл. Это также граница
подмены Unity в тестах. Пути Windows и Git Bash нормализуются через `cygpath` и `pwd -P`;
в identity и аргументах Unity передаётся абсолютный Windows-путь с `/`.

Дополнительные аргументы имеют вид `NAME=value`. Имя должно соответствовать
`[A-Za-z_][A-Za-z0-9_]*`, переводы строк запрещены. Значения передаются буквально,
без `eval`, только дочерним Unity/Player; они не изменяют переменные самого runner.
Имена `PORTAL_CHECK_*`, `PORTAL_RUNNER_*`, `PORTAL_PROJECT`, `PORTAL_UNITY` и переменные управления
shell/поиском программ (`PATH`, `BASH_ENV` и аналогичные) зарезервированы независимо
от регистра. Для значения с пробелами или метасимволами заключите весь аргумент в одинарные кавычки.

## Результат и связь со сборкой

Перед warmup runner устанавливает окружение, общее для компиляции, сборки и Player:

| Переменная | Значение |
| --- | --- |
| `PORTAL_CHECK_NAME` | Точное имя выбранной проверки |
| `PORTAL_CHECK_COMMIT` | Полный `git rev-parse --verify HEAD` выбранного проекта |
| `PORTAL_CHECK_PROJECT` | Канонический абсолютный путь выбранного проекта |
| `PORTAL_CHECK_RUN_ID` | Новый GUID без дефисов для каждого запуска |
| `PORTAL_CHECK_OUTPUT` | Абсолютный путь уникального каталога логов |

Следующая задача должна добавить callback сборки, сохраняющий эту identity в Player,
и runtime, печатающий ровно одну однострочную запись:

```text
[PortalCheckResult] {"check":"Seam","completed":true,"status":"Passed","commit":"0123456789abcdef0123456789abcdef01234567","projectPath":"C:/PortalProject","runId":"0123456789abcdef0123456789abcdef","frameCount":120,"crossingCount":0,"failureReason":""}
```

Все девять полей обязательны. Имена полей, `check`, `commit`, `runId` и `Passed`
сравниваются с учётом регистра. `completed` — JSON boolean `true`, `frameCount` —
положительное целое, `crossingCount` — неотрицательное целое, `failureReason` —
пустая строка при успехе. Остальные поля identity и `status` должны быть строками.
Пути Windows сравниваются без учёта регистра, допускаются `/`, `\` и конечный разделитель;
относительные пути и сегменты `.`/`..` не принимаются. Дополнительные JSON-поля разрешены.

`Failed`, `Blocked`, незавершённый результат, неправильные типы, отсутствующие поля,
некорректный JSON, повторные JSON-ключи, несколько итоговых записей и несовпадающая
identity приводят к отказу. Допустим только один JSON-объект без комментариев,
конечных запятых и постороннего содержимого. Ненулевой exit Player всегда означает отказ.

Verifier совместим с Windows PowerShell 5.1 и завершается кодом `0` только при полном
совпадении контракта, иначе `1`. Ручной вызов требует фактических значений конкретного запуска:

```powershell
& .\tools\verify-check.ps1 -LogPath 'C:\PortalProject\Logs\checks\run\player.log' `
    -ExpectedCheck Seam -ExpectedCommit '0123456789abcdef0123456789abcdef01234567' `
    -ExpectedProjectPath 'C:\PortalProject' -ExpectedRunId '0123456789abcdef0123456789abcdef' `
    -PlayerExitCode 0
```

Полный SHA обозначает HEAD, но не доказывает отсутствие незакоммиченных изменений.
Для воспроизводимой сертификации используйте чистую рабочую копию. Сам runner не
меняет Git-состояние и не скрывает изменения, создаваемые существующими Unity builders.

## Логи, свежесть и блокировки

Каждый прошедший предварительные проверки запуск получает каталог
`Logs/checks/<check>-<UTC timestamp>-<runId>` с `warmup.log`, `build.log`, `player.log`.
Все три файла создаются сразу, поэтому при раннем отказе последующие логи могут быть пустыми.
Runner печатает полные пути. Старые логи не читаются и не перезаписываются.

Warmup и build выполняются последовательно. При ненулевом коде следующие стадии
не запускаются. Перед build удаляется только конкретный
старый EXE выбранной проверки внутри проверенного каталога сборки; соседние файлы
сохраняются. Нулевой build без нового EXE считается ошибкой. PowerShell запускает
нативные процессы и сохраняет полные Windows-коды в `warmup.exitcode`, `build.exitcode`,
`player.exitcode`. Verifier получает полный код Player. Наружу Bash возвращает младшие
8 бит ненулевого кода, а если они равны нулю — `1`: Windows exit `256` не превращается
в успешный запуск. Отсутствие файла кода также означает отказ. При отказе verifier
после нулевого Player возвращается `1`.

`Logs/checks/.runner-lock` создаётся атомарно и защищает проект от второго runner.
При обычном завершении, включая ошибки стадий, пустая собственная блокировка удаляется.
После сигнала прерывания или аварийного завершения она может остаться: runner никогда
не угадывает, устарела ли блокировка, и не удаляет чужую. Перед ручным удалением только
этого пустого каталога убедитесь, что runner и его Unity/Player действительно завершены.
Блокировка не мешает пользователю открыть Unity вручную после предварительной проверки;
не открывайте выбранный проект до конца запуска.

У runner нет внутреннего watchdog. Зависшая Unity или Player может ожидаться неограниченно.
Для ограниченного ожидания используйте внешний supervisor/таймаут, например из Git Bash:

```bash
timeout --signal=TERM --kill-after=30s 15m bash tools/check.sh Seam
```

Завершение оболочки не гарантирует завершение дочернего Windows-процесса. После таймаута
проверьте процессы и блокировку до повторного запуска. Runner не завершает сторонние
Unity-процессы и не обещает автоматическое восстановление после зависания.

## Проверки и миграция

Сохранены имена и пути `Seam`, `Color`, `Ghost`, `Rotate`, `Cross`, `Look`,
`Cinemachine`, `Bubble`, `Close`, `Light`, `Prefab`, `AutoWire`, `Setup`.
Добавлены:

| Имя | Метод сборки | EXE |
| --- | --- | --- |
| `SandboxParity` | `SandboxParityCheckBuilder.BuildPlayer` | `BuildSandboxParityCheck/SandboxParityCheck.exe` |
| `Performance` | `PortalPerformanceCheckBuilder.BuildPlayer` | `BuildPortalPerformanceCheck/PortalPerformanceCheck.exe` |

`Performance` запускается в 1920×1080, остальные проверки — в 1280×720, оконный режим.
Наличие маршрута не означает наличие соответствующего builder/runtime в текущем проекте.
Legacy-проверки сохраняют сборку и запуск, но до миграции на итоговый контракт намеренно
возвращают ошибку и не считаются сертифицированными. Автоматического перевода старых тегов
в `Passed` нет. Эти tools не исправляют обнаруженный нативный сбой Seam.

## Тесты без Unity и Pester

Из корня проекта в PowerShell:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tools/tests/verify-check.tests.ps1
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tools/tests/check-runner.tests.ps1
& 'C:\Program Files\Git\bin\bash.exe' -n tools/check.sh
```

Файлы `.ps1` сохранены в UTF-8 с BOM для корректного чтения в Windows PowerShell 5.1.
Verifier проверяется в отдельных процессах Windows PowerShell 5.1. Runner-тесты создают
две временные Git-копии и компилируют fake Unity/Player через штатный .NET Framework
(`System.Web.Extensions`); реальная Unity не запускается. Путь установленной Unity
в тестовой копии дополнительно заменён отсутствующим sentinel-файлом. Каждый вызов
runner ограничен 20 секундами, при превышении завершается только его тестовое дерево процессов.
Тесты покрывают cwd/override, identity, изоляцию логов, все маршруты, ошибки стадий,
старый EXE, блокировки, окружение и обнаружение PowerShell. Ветка `pwsh.exe` проверяется,
если PowerShell 7 установлен. Для Git Bash тесты ожидают стандартный путь установки
`C:\Program Files\Git`. Очистка удаляет только проверенный выделенный временный каталог.
