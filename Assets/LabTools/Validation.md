# Проверки Color, Seam, SandboxParity, Performance и Visibility

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

Все пять проверок явно добавляют `BuildOptions.CleanBuildCache`. Preprocess callback
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
HDRP Unlit emission `(2048,0,2048)`, exposureWeight=1 рассчитан на маркер при EV11, но его наличие
доказывается пикселями, не параметром material. Сначала normal oblique должен скрыть
marker, затем regular projection обязан показать его. Classifier: R/B≥128,G≤96,
R−G/B−G≥64; существующие magenta pixels исключаются по background соответствующей проекции.
Regular positive count=0 или неполный fixture дают Blocked; видимый marker при oblique
и доказанном positive control даёт Failed. Regular projection не считается исправлением.

Артефакты: `parity-metrics.csv`, `parity-summary.txt`, все PNG/metadata по mode/AA,
per-mode metrics JSON, `leakage-control.json` и leakage PNG/background/fixture.
Regular projection синхронизирует только `_PortalInverseProjection` через public
Portal binding после штатных main-camera callbacks. Текущие depth texture и остальные
свойства сохраняются; baseline/oblique и чужие камеры не изменяются. Подписка
переподключается в LateUpdate(2000) после PortalSystem(1000), включая пересоздание камер.
`*-projection-audit.json` у regular through/positive/background captures и общий
`projection-audit.json` содержат observed main bindings, отдельные count/frame обязательного entrance, root/portal,
depth name, expected/bound GPU inverse и максимальную поэлементную ошибку.
В конце capture frame binding перечитывается без исправления: ошибка >1e-5,
неоднозначный root, отсутствующий depth или отсутствие нового entrance binding в текущем кадре
делают fixture Blocked. Этот runtime audit дополняет, но не заменяет реальные Player captures.
Аудит выбирает contributors через публичный `PortalSystem.HasContentBuffers`, как production
content-depth composite. Offscreen/suspended exit с borrowed/cached ViewTexture не считается
собственным root render и не изменяется. У настоящего contributor отсутствие или неоднозначность
root остаются ошибкой; binding другого портала не заменяет обязательный entrance.
Строгий classifier выше не ослабляется. Реальный Player должен доказать regularPixels>0
и obliquePixels=0; это не исправление production и не отмена baseline gate.
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
на старых камерах. Затем один явно нетаймируемый setup frame позволяет новым камерам
впервые выполнить AOV и зарегистрировать lazy markers. Discovery, запуск recorders,
выделение sample storage и запись available-counters завершаются перед полными
180 warmup; после них сохраняются ровно360 sample frames. `round*/<mode>-window.txt`
фиксирует frame indices завершения setup, начала/конца sampling и оба required counts.

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
Один раз перед warmup режима recorders получают `Reset()` и `Start()`: Reset останавливает
native collection, хотя Valid остаётся true. Затем сбор непрерывный: буфер4096 samples,
без wrap-around. Cursor исключает warmup и читает каждый новый sample ровно один раз;
при переполнении, остановке или потере истории вся серия становится unavailable.
Покадровый Reset не используется: он может разделять native flush и терять arrivals.
Reader читает GetSample, не LastValue. Реальные CPU marker tests проверяют 1/0/3/1 scopes,
сохранение нескольких кадров до чтения, отсутствие повторов и отказ при переполнении.
`*-native-aov.csv` сохраняет все прочитанные CPU scope counts в порядке поступления;
native sample не отождествляется с script frame. CSV покадрового чтения содержит только
последний новый counter sample, а median/p95 используют все новые native samples.
Zero-execution gate учитывает всю прочитанную пачку CPU samples: ранний ненулевой
или больший Count нельзя скрыть последним меньшим Count, даже если legacy sampler
совпадает с последним. Такая пачка делает execution assertion unavailable.
GPU recorder `HDRenderPipelineRenderAOV` использует GpuRecorder и ns→ms; его sample.Count
также сохраняется. Это свежие arrivals после Reset/Start, не время текущего render frame.
`read_frame` — кадр наблюдения; `gpu_source_frame=null`, исходный mode тоже неизвестен:
API не предоставляет source-frame ID. GPU median/p95 группируют arrivals по окну чтения,
но не сертифицируют GPU cost режима. Reset на границе режима может отбросить pending GPU samples;
полнота GPU выборки и latency не доказаны. Нулевой Count остаётся null, а не старым LastValue.
`gpuFrameTime` не называется полной стоимостью портального GPU render.
Смысл count описан в [Unity ProfilerRecorderSample.Count](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderSample.Count.html).
Отсутствующий aggregate Draw Calls Count не заменяется суммой несопоставимых counters;
доступные имена/category/unit/data type/flags остаются в `available-counters.txt`.
Это отфильтрованная inventory, не полный список Unity counters. `round*/<mode>-counters.txt`
содержит до warmup и после sampling Valid/IsRunning/Count/Capacity/WrappedAround,
поддержку GPU recorder, включение legacy sampler и причины отсутствующих samples.
Native GPU acceptance выполняется в реальном Development Player с HDRP AOV;
batch Editor fixture без фактически отрисованных SRP frames не доказывает GPU regression.

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

## Visibility

Запуск из свободного изолированного checkout: `bash tools/check.sh Visibility`.
Builder `PortalVisibilityCheckBuilder.BuildPlayer` создаёт
`BuildPortalVisibilityCheck/PortalVisibilityCheck.exe`: Windows64 Development,
D3D12, 1280×720, исходная Sandbox без сохранения изменений. Только новое имя
`Visibility` получает этот probe; контракты прежних проверок не меняются.

Проверка использует существующие `Recursion_Pair` и `Recursion_Marker`.
Eye (20,1.75,14), yaw −90/+90 смотрит в A/B. Активен root одной стороны,
компонент второго Portal отключён, но его screen остаётся consumer. Для каждой
стороны независимо выполняются R1/O/R2: depth4 с culling opt-out, depth4 с
оптимизацией, повтор opt-out. Depth0 positive выполняется только после triple.
Lens, AA=None, render settings, AOV и разрешение RT не меняются.
Центральный ROI снизу слева (480,200,320,320) содержит 102400 пикселей.
Reference/optimized RGB должны совпадать точно с обоими references. R1/R2 должны
совпадать точно; иначе этот triple остаётся unresolved/Blocked, без допуска на шум.
Разница O с воспроизводимым reference или нарушение lifecycle остаётся Failed,
даже если другой triple оказался невоспроизводимым. Depth0 обязан показать отличие:
max channel difference ≥16 и средняя RGB MAE ≥0.5 в 8-bit units; иначе Blocked.

Перед каждым независимым arm одинаково подготавливается capacity; затем новая
поза, HDCamera.GetOrCreate(main,0).Reset() и PortalSystem.ResetHistory().
Capture приходится на 40-й завершённый main render после reset, не на 40-й
coroutine yield. Часы считают endCameraRendering только main; screenshot остаётся
в соответствующем WaitForEndOfFrame. Time, Cinemachine и эффекты не переопределяются.

Reentry R1/O/R2 использует одинаковую траекторию: visible40, hidden4, first return1,
settled return40. В reference cull=false и Budget0 только на hidden-интервале;
optimized при Budget8 приостанавливается обычным culling. Внутри траектории
нет ручного ResetHistory, disable/recreate. Сравниваются visible/first/settled
с теми же стадиями двух references, а не с исходным статическим кадром.
Разность first/settled каждого arm сохраняется отдельно как диагностика.

Для бюджета создаются ровно три неактивных клона существующей пары в runtime
build-copy, без старых камер. После конфигурации активируется один root каждой
пары. Бюджеты 0/3/1/4/0/3 должны дать callbacks [0,0,0]/[1,1,1]/[0,0,1]/
[1,1,2]/[0,0,0]/[1,1,1]. Приоритет задаёт возрастающий физический размер проёма.
Проверяются фактические beginCameraRendering, deepest-first, оба child binding,
отсутствие feedback, main fallback/depth, cold отсутствие камер и reuse/history
после starvation. Идентичность cloned cameras записана в observation JSON;
общий Sandbox metadata helper не классифицирует runtime-клоны как исходные portals.

Дополнительный parented ordinary контроль использует один runtime-клон исходной
пары и её маркер. Eye (13.25,11.5,27.75), rotation (17,31,7); Player находится под
parent с position (2,3,4), rotation (11,23,5). Side-by-side depth2 обязан дать
reference3/optimized1/reference3 и exact RGB; независимый Budget0 no-view capture
обязан пройти те же positive thresholds. Высота оставляет fixture над Sandbox
ground. Это не сертификация arbitrary custom views или рекурсивных viewport edges.

Артефакты schema2: 30 PNG с metadata/observation JSON, `visibility-evidence.json`,
шесть `*-triple.json`, три `reentry-*-first-vs-settled.json`,
`visibility-contract.txt` и стандартный result. В observation добавлены main и
virtual camera IDs, активность, history до/после, completed renders, точные
float-массивы pose/view/projection, Unity/HDRP time и настройки temporal effects.
Основной metadata gate сопоставляет main и общие активные уровни каждого triple:
конечные и одинаковые pose/matrices, равные history epochs и completed counts.
Неактивные child уровни parented 1-vs-3 не сравниваются. Различное абсолютное
время допустимо и записывается для диагностики. Missing/mismatched metadata —
Blocked; нарушения наблюдаемых lifecycle-инвариантов — Failed.
Архивные 15-capture файлы и исходный policy Evaluate не изменяются.
Editor tests не заменяют этот actual Player-контроль.

Диагностика начальной AO history: `tools/check.sh Visibility VISIBILITY_REINITIALIZE_AO_HISTORY=1`.
Для Unity 6000.5.9f1/HDRP 17.5.0 штатный HDCamera.Reset сохраняет regular AO buffers.
Контроль вызывает точный метод ReleaseHistoryFrameRT(int) у владельца HDCamera
только перед независимыми arms, затем обычный SSAO сам инициализирует историю.
Заимствованные RTHandle напрямую не освобождаются, AOV histories не изменяются.
Внутри hide/reentry/starvation нет дополнительного сброса, эффекты и пороги прежние.
Файлы `regular-ao-history-control.txt` и `regular-ao-history-preparation.json`
показывают режим, IDs текущей/предыдущей history перед освобождением и результат
свежих запросов после него. Обычная последовательность Reset не меняется и не
повторяется дополнительно. Это отдельный эксперимент, не production-исправление.
Без аргумента остаётся штатный Reset; неизвестная версия/API даёт Blocked.

### Ограниченный Visibility edge sweep

Запуск main из Git Bash в свободном изолированном checkout:

```bash
bash tools/check.sh Visibility VISIBILITY_EDGE_SWEEP=1 VISIBILITY_REINITIALIZE_AO_HISTORY=1
```

Это отдельный schema3 маршрут из **16 captures**, не дополнение к штатным 30.
Без `VISIBILITY_EDGE_SWEEP=1` прежний маршрут и центральный ROI не меняются.
Edge-флаг без AO preparation, неизвестное значение флага или неподдерживаемый
owner API дают Blocked. Эффекты, RGB-пороги, AA, AOV и разрешение не меняются.
Исходная пара отключается; её неактивный клон создаётся только после удаления
старых virtual cameras. Все изменения fixture выполняются лишь в build-copy.

Каждая строка — независимые R1/O/R2 и затем pixel-positive, через прежние
`StaticTriple/BeginArm/CaptureAt`: одинаковые resets и ровно 40 completed main
renders в каждом arm. Reference всегда full-prefix depth2 с culling=false.

| Случай | R1/O/R2/positive virtual renders | Positive | ROI снизу слева |
| --- | --- | --- | --- |
| `edge-recursion-inside`, x=1.499 | 3/3/3/1 | Depth0 | (768,200,320,320) |
| `edge-recursion-outside`, x=1.6 | 3/2/3/1 | Depth0 | (768,200,320,320) |
| `edge-custom-viewport` | 3/1/3/0 | Budget0 | (960,200,320,320) |
| `edge-custom-far` | 3/1/3/0 | Budget0 | (480,200,320,320) |

Обычная parented камера: eye=(13.25,11.5,27.75), R=Euler(17,31,7), parent как
в прежнем parented control. Пара находится в eye+R*(x,0,±2), A rotation=R*Y180,
B rotation=R; оба физических проёма 2×3, маркер между ними. В координатах камеры
root left=(x−1)/2, child2 right=(x+1)/10; их пересечение заканчивается при x=1.5.
ROI включает этот край и видимую область первого child. Counts не корректируются
по результатам run: неустойчивая геометрия требует отчёта и измеренного изменения
fixture main, а не ослабления assertions.

Custom controls используют side-by-side exit со смещением 30 вдоль R.right,
где полезен только root. Перед настройкой каждого arm сбрасываются raw view и
culling matrix. Затем linear A=Scale(1.4,1,−2)*Rotate(R).transpose;
main.worldToCameraMatrix получает A с намеренной translation=(111,222,333),
а cullingMatrix=P*A*T(−eye) — без этой translation. P остаётся штатной lens projection.
Viewport case: z=2, x=2*z*tan(FOV/2)*aspect/1.4; центр проёма лежит на правой
границе effective frustum, часть проёма видна. Far case: x=0, z=5.1,
relative yaw=135°, farClip=10; глубина центра 2*z=10.2, проём пересекает far plane.
Это сравнение оптимизации с существующим full-prefix, не общее утверждение о
корректности arbitrary custom views. Parent, pose, camera far/raw view/culling
восстанавливаются в finally; аварийное завершение использует тот же cleanup.

`EvaluateEdges` отдельно требует все 16 modes/counts, 40 completed renders,
валидные main/virtual metadata и совпадение histories/pose/matrices общих активных
камер каждой тройки. Неиспользуемые child уровни reference не сопоставляются с
отсутствующими optimized уровнями. Все четыре R1/R2 обязаны иметь RGB diff=0;
иначе Blocked/unresolved. Каждый O сравнивается точно с обоими references.
Каждый positive обязан иметь max channel difference ≥16 и среднюю RGB MAE ≥0.5;
нулевые или отсутствующие pixels не подтверждают fixture. Доказанная регрессия
в отдельном воспроизводимом triple остаётся Failed даже при noise в другом.

Артефакты: `visibility-edge-evidence.json` с edge/AO flags, 16 PNG и обычные
observation/metadata JSON, четыре `static-edge-*-triple.json`, четыре
`edge-*-fixture.txt` с ROI/pose/raw/culling/projection, прежний AO preparation audit.
Исторические Logs не меняются. Полный GPU EditMode на этой реализации:
411/411, без пропусков, native exit0 (`Logs/task1-edge-full-main-20260831`).
Этот результат не заменяет actual Player-проверку граничных пикселей.

Для legacy build-only используйте `PortalVisibilityCheckBuilder.BuildLegacy`,
без `PORTAL_CHECK_NAME`, с `PORTAL_LEGACY_CHECK=Rotate|Cross|Ghost` и абсолютным
`PORTAL_LEGACY_OUTPUT`. Builder собирает сохранённую сцену и не вызывает её
генератор, SaveAssets или DeleteAsset. Players со screenshot capture запускаются
видимо; background Editor — скрыто. Missing scripts, timeout и отсутствие итогового
JSON остаются отдельными ограничениями legacy, даже при native exit 0.

## Обычная Sandbox-сборка для cache control

Execute method: `SandboxOrdinaryCheckBuilder.BuildPlayer`. Выход:
`BuildSandboxOrdinary/SandboxOrdinary.exe`, Windows64 Development/D3D12-first.
Переменная окружения `PORTAL_CHECK_NAME` должна отсутствовать, иначе builder
останавливается. Entry point вызывает существующий `Build(null, ...)`: не добавляет
CleanBuildCache, probe, embedded check identity или runtime bootstrap.
Это обычный Player, без финального PortalCheckResult; его нельзя считать Passed check.

Entry point должен уже присутствовать до certified Sandbox build. Для cache control
сначала собирают certified Sandbox, затем ordinary Sandbox теми же source bytes,
без добавления/изменения кода или исходной сцены между сборками. Реальные build/run,
проверку отсутствия probe и native exit выполняет main; EditMode не заменяет этот контроль.

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
