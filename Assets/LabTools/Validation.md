# Проверки Color, Seam, SandboxParity и Performance

Проверки связывают итог с конкретной сборкой и сохраняют данные до объявления результата.
Поддерживаемая конфигурация: Unity 6000.5.9f1, установленный HDRP 17.5.0,
Windows x64. Для чтения идентичности исходников нужен Git в PATH.

## Запуск

Используйте существующий `tools/check.sh` из свободного изолированного worktree.
Он задаёт `PORTAL_CHECK_NAME`, `PORTAL_CHECK_COMMIT`, `PORTAL_CHECK_PROJECT`,
`PORTAL_CHECK_RUN_ID`, `PORTAL_CHECK_OUTPUT` перед сборкой и запуском Player.
Builders `ColorCheckBuilder.BuildPlayer` и `SeamCheckBuilder.BuildPlayer` совместимы
с этим интерфейсом. Для исходной Sandbox доступны `SandboxParityCheckBuilder.BuildPlayer`
и `PortalPerformanceCheckBuilder.BuildPlayer`. Не запускайте второй Editor для того же checkout.

Все четыре проверки явно добавляют `BuildOptions.CleanBuildCache`. Preprocess callback
отклоняет сертифицированную сборку без этой опции: изменившийся runId не является
зависимостью обычного кэша обработки сцен. Дополнительно callback регистрирует
`Logs/portal-check-build-state.json` через `BuildPipelineContext.DependOnPath`.
При переходе обратно к обычной сборке этот файл меняется на `ordinary-build`,
чтобы не переиспользовать ранее внедрённый контекст. Проверка двух последовательных
Player-сборок остаётся обязательной интеграционной проверкой.
Причина принудительной очистки описана в
[документации Unity по incremental builds](https://unity.com/blog/engine-platform/accelerating-player-builds-with-incremental-build-pipeline).

Identity внедряется только в копию первой сцены выбранной сертифицируемой сборки. В ней находятся
реальный SHA, канонический путь проекта, runId, каталог результата, dirty-флаг,
версии Unity/HDRP и SHA-256 манифеста исходников. Манифест включает пути и хеши
реальных байтов tracked и неигнорируемых untracked-файлов в Assets, Packages,
ProjectSettings; отсутствующие tracked-файлы отмечаются как удалённые.
Dirty-флаг отражает весь Git worktree, а не только эти три каталога.
Ожидаемые SHA и путь обязательно сверяются с реальным checkout до внедрения.
При запуске переменные окружения только проверяются против встроенных значений.
Они не заменяют идентичность бинарного файла.

Без переменных проверки обычный Editor/Play Mode не создаёт контекст.
Legacy checks, включая Ghost/Cross/Rotate, не внедряют identity и не требуют clean
build: они сохраняют прежние диагностические build/run. Их старые логи не являются
сертифицированным результатом; строгий verifier отклоняет отсутствующий контракт.

## Результаты и метрики

Одна строка `[PortalCheckResult] {json}` содержит финальный контракт. Статусы:
Passed, Failed, Blocked. Failed и Blocked завершаются ненулевым кодом. Обнаруженные
Unity Error/Exception/Assert сохраняются как pending failure; успешная метрика их
не отменяет. Realtime watchdog ограничен 180 секундами. Ранний Quit отклоняется
через wantsToQuit и повторяется с ненулевым кодом на следующем Update; fallback
OnApplicationQuit записывает Blocked без рекурсивного Quit. Нативный crash до Awake
не может сформировать runtime-результат и должен отклоняться внешним runner.
До контролируемого retry отклоняются и повторные запросы, включая реентерабельный
запрос из callback итогового лога. Это не исправление нативного D3D12 shutdown crash.

Артефакты сохраняются в уникальном встроенном `PORTAL_CHECK_OUTPUT`:

- `build-identity.json`, `result.json`;
- Color: все исходные PNG-шаги, `color-metrics.csv`, `color-summary.txt`;
- Seam: `seam-metrics.csv`, `seam-summary.txt`, PNG непосредственно перед
  пересечением, на нём и следующих двух кадров.

Color: max RGB mean delta между crossBefore/crossAfter не выше 0.001.
Единицы — нормализованный raw capture RGB из `Texture2D.GetPixels`, **не** доказанная
линейная HDR radiance. Сохраняются все approach-шаги; far остаётся диагностикой
из-за разных volume policy. Пропущенные захваты, неполный набор или NaN дают Failed.
Это сравнение поз: `crossingCount=0`, реальный переход не утверждается.

Seam: требуется ровно одно событие Teleported, предыдущий кадр и минимум два
последующих. Каждый шаг перемещения равен Motion × 1/60 сек; 160 шагов при скорости
3 дают 8 м запрошенного перемещения. Coroutine возобновляется перед LateUpdate,
снимок выполняется после него в EndOfFrame. Первая разность в CSV — NaN, поскольку
предыдущего снимка ещё нет; остальные разности и все luminance должны быть конечными.
Для установленного Cinemachine 3.1.4 Seam временно переводит свой Brain в ManualUpdate
и выполняет `ManualUpdate(cameraTick, 1f/60f)`, сохраняя damping 0.2. Порядок:
traveller/bridge warp (900), clock и итоговая gameplay Camera (950), PortalSystem (1000).
Clock делает один tick за кадр, непрерывно через warmup, settle и walk; Unity Time
не меняется. Прежние update/blend modes восстанавливаются при завершении и disable.
CSV сохраняет прежние пять полей и добавляет `cameraTick`, `cameraSimulatedTime`
(секунды с начала clock, включая warmup/settle), `cameraPositionX/Y/Z` и
`cameraRotationX/Y/Z/W` (world position/quaternion фактической gameplay Camera
в той же EndOfFrame-итерации, что и capture, не CameraHolder).
Корректный набор получает Blocked: визуальный порог ещё не откалиброван.
Отсутствующее/повторное пересечение либо неполные метрики дают Failed.

## API для следующего этапа

Runtime assembly: `Portals.Lab.Validation`. Компонент: `PortalCheckRun`.

```csharp
using Portals.Lab.Validation;

PortalCheckRun run = PortalCheckRun.Current;
if (run != null)
{
    string output = run.OutputDirectory;
    run.RecordProgress(frameCount: 160, crossingCount: 1);
    run.Complete("Seam", "Blocked", 160, 1, "Seam visual threshold not calibrated.");
}
```

Дополнительные probes должны получить собственные acceptance-тесты, регистрацию
в `PortalCheckBuildIdentity.IsMigratedCheck`, clean build options и разрешение
соответствующего check в `PortalCheckSession`. Неизвестный check не получает Passed.
SandboxParity/Performance используют этот контракт и собственные policies ниже.

## SandboxParity

Builder создаёт `BuildSandboxParityCheck/SandboxParityCheck.exe` из исходной
`Assets/portal/Examples/PortalSandbox.unity`. Source scene не сохраняется и не меняется.
`SandboxCheckBuildProcessor` внедряет один probe только в build-copy выбранного check,
после проверки встроенного identity. Обычный Play/build не получает bootstrap/probe.
HDRP Unlit shader передаётся сериализованной ссылкой для runtime-owned marker material.

Windows64 Development, D3D12, 1280×720. Архивные eye poses: through
(40,1.6,-5.98), direct (0,1.6,6.02), yaw180; direct вычисляется через
`PortalMath.EntranceToExit`. ROI `(480,260,320,200)` указан в координатах
`Texture2D.GetPixels` снизу слева. RGB сравнивается в 8-bit capture units, alpha исключён.

Четыре режима: baseline, SSAO off обеих камер, SSAO off только virtual,
regular projection с SSAO ON. В каждом — main AA None и TAA; собственный AA виртуальных
камер сохраняет production policy. Между режимами main frame settings восстанавливаются,
виртуальные камеры пересоздаются. Overrides идут после PortalSystem LateUpdate.
Для каждой пары: 120 settle → through, direct pose →1 settle →direct-first,
120 settle →direct, 120 settle →direct-repeat. Прямые кадры не имеют virtual cameras.

Статический gate относится только к baseline/None: каждая channel MAE≤0.15,
max channel difference≤2. Остальные режимы, TAA и direct-repeat noise диагностические;
они не заменяют baseline и не доказывают качество motion. Missing capture/reference,
неподтверждённые camera settings или неполный набор дают Blocked.

Отдельный leakage control выполняется на 1 м от портала после исходных ROI кадров.
Marker ставится между mapped eye и exit plane по пересечению луча, без выбора знака оси.
HDRP Unlit emission `(8192,0,8192)` рассчитан на яркий маркер при EV11, но его наличие
доказывается пикселями, не параметром material. Сначала normal oblique должен скрыть
marker, затем regular projection обязан показать его. Classifier: R/B≥128,G≤96,
R−G/B−G≥64; существующие magenta pixels исключаются по background соответствующей проекции.
Regular positive count=0 или неполный fixture дают Blocked; видимый marker при oblique
и доказанном positive control даёт Failed. Regular projection не считается исправлением.

Артефакты: `parity-metrics.csv`, `parity-summary.txt`, все PNG/metadata по mode/AA,
per-mode metrics JSON, `leakage-control.json` и leakage PNG/background/fixture.
Все данные сохраняются и при Failed. Один финальный PortalCheckResult идёт после всех
режимов/control; runtime exceptions и watchdog по-прежнему обрабатываются общим контрактом.
Заморозка Rigidbody/отключение движения ограничены диагностическим Player; shared profiles
и чужие scene bodies не меняются.

## Performance

Builder: `BuildPortalPerformanceCheck/PortalPerformanceCheck.exe`, та же неизменённая
Sandbox, Windows64 Development/D3D12. 1920×1080, VSync0, unlimited FPS, runInBackground.
Root `(0,0.1,-3.5)`, eye `(0,1.75,-3.5)`, yaw0 или180 для behind.

В каждом из двух раундов: off, depth2, depth0, depth2-no-aov, depth0-no-aov,
depth2-divider2, behind. В no-aov меняется только `writeContentDepth=false`;
перед сменой параметров portals отключаются на кадр, чтобы AOV requests не остались
на старых камерах. Каждый режим: ровно 180 warmup и360 sample frames.

`performance.csv` содержит median/p95/counts; `round*/<mode>-samples.csv` — реальные
покадровые значения/наличие counters. Time.unscaledDeltaTime хранится отдельно от
FrameTiming; повторные frameStartTimestamp не создают дубликаты. Discovery, логирование,
PNG, ReadPixels/EncodePNG и дисковый I/O выполняются вне timed loop.
Недоступные числовые значения — literal `null`, статистика не подменяется -1/NaN/нулём.
Percentile сохраняет архивный sorted index `floor(n*p)` с ограничением n−1,
без интерполяции. CSV числовой формат InvariantCulture.

`beginCameraRendering` counts не включают AOV. Requests перечисляются отдельно от
executions; execution evidence — CPU `ProfilerRecorderSample.Count`, проверенный
против `Recorder.sampleBlockCount`. Empty CPU recorder допускает zero только при
валидном включённом CPU sampler с zero blocks; disagreement делает assertion unavailable.
Для доказательства отсутствия AOV нужны все360 frame readings, не частичная выборка.
GPU recorder `HDRenderPipelineRenderAOV` использует GpuRecorder и ns→ms; его sample.Count
также сохраняется. `gpuFrameTime` не называется полной стоимостью портального GPU render.
Смысл count описан в [Unity ProfilerRecorderSample.Count](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderSample.Count.html).
Отсутствующий aggregate Draw Calls Count не заменяется суммой несопоставимых counters;
доступные имена/единицы остаются в `available-counters.txt`.

PNG depth2/depth0 снимаются после timed loop в обоих раундах. Опубликованный ROI
сверху слева `(865,420,190,290)` переводится в Texture2D `(865,370,190,290)`:
`1080−420−290=370`. Gate требует exact RGB MAE=0,max=0.

Cost gate: два полных раунда; default depth2 в перспективе имеет1 main+1 virtual и
никаких AOV requests/executions; off/behind имеют только1 main. Текущий production
renderer ожидаемо даёт Failed за избыточные камеры/AOV, а не за аппаратный порог ms.
Недоступное обязательное evidence даёт Blocked; оно не отменяет уже доказанную
избыточную работу. Timings и per-round depth2/depth0 median ratios диагностические,
не утверждение об улучшении production. Дополнительные файлы: mode settings/metadata,
per-round ROI JSON, `performance-contract.txt`, `performance-summary.txt`.

Общий watchdog остаётся180 сек; 7560 warmup/sample frames занимают126 сек при60 FPS
без overhead. Более медленный прогон может дать Blocked до завершения; сокращение
выборки или обход watchdog не выполняются. Ресурсы probes освобождаются при disable/destroy.
Ненулевой native Player exit не игнорируется даже при записанном result.json.

## Сериализация Seam

Лабораторные LookController и PlayerStateMachine разделены на одноимённые файлы;
GUID прежнего LookController сохранён. Это заглушки reflection-моста, не настоящая
интеграция UHFPS. `SeamCheckBuilder.PrepareScene` сохраняет и повторно открывает
сцену, затем проверяет ссылки и постоянные MonoScript. `BuildSavedScene` позволяет
отдельно собрать подготовленную сцену; `BuildPlayer` выполняет обе фазы.
После добавления camera clock нужно один раз выполнить `PrepareScene`, чтобы сохранить
новую ссылку `SeamCheck.gameplayCamera`; затем повторные `BuildSavedScene` используют
ту же сцену без регенерации. Validator сверяет эту ссылку с камерой bridge.
Профили переиспользуют прежние GUID и subassets, а не удаляются через DeleteAsset.

Сериализационные round-trip тесты используют GUID-пути и выполняются только в
изолированном batchmode Editor; в интерактивном Editor пропускаются, чтобы не закрыть
несохранённую пользовательскую сцену. Подтверждение исправления native level0 crash,
реальное визуальное качество и runtime exit-коды требуют отдельного Player build/run.
