# Проверки Color и Seam

Проверки связывают итог с конкретной сборкой и сохраняют данные до объявления результата.
Поддерживаемая конфигурация: Unity 6000.5.9f1, установленный HDRP 17.5.0,
Windows x64. Для чтения идентичности исходников нужен Git в PATH.

## Запуск

Используйте существующий `tools/check.sh` из свободного изолированного worktree.
Он задаёт `PORTAL_CHECK_NAME`, `PORTAL_CHECK_COMMIT`, `PORTAL_CHECK_PROJECT`,
`PORTAL_CHECK_RUN_ID`, `PORTAL_CHECK_OUTPUT` перед сборкой и запуском Player.
Builders `ColorCheckBuilder.BuildPlayer` и `SeamCheckBuilder.BuildPlayer` совместимы
с этим интерфейсом. Не запускайте второй Editor для того же checkout.

Color/Seam явно добавляют `BuildOptions.CleanBuildCache`. Preprocess callback
отклоняет сертифицированную сборку без этой опции: изменившийся runId не является
зависимостью обычного кэша обработки сцен. Дополнительно callback регистрирует
`Logs/portal-check-build-state.json` через `BuildPipelineContext.DependOnPath`.
При переходе обратно к обычной сборке этот файл меняется на `ordinary-build`,
чтобы не переиспользовать ранее внедрённый контекст. Проверка двух последовательных
Player-сборок остаётся обязательной интеграционной проверкой.
Причина принудительной очистки описана в
[документации Unity по incremental builds](https://unity.com/blog/engine-platform/accelerating-player-builds-with-incremental-build-pipeline).

Identity внедряется только в копию первой сцены сборки Color/Seam. В ней находятся
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
В этом этапе SandboxParity/Performance не реализованы.

## Сериализация Seam

Лабораторные LookController и PlayerStateMachine разделены на одноимённые файлы;
GUID прежнего LookController сохранён. Это заглушки reflection-моста, не настоящая
интеграция UHFPS. `SeamCheckBuilder.PrepareScene` сохраняет и повторно открывает
сцену, затем проверяет ссылки и постоянные MonoScript. `BuildSavedScene` позволяет
отдельно собрать подготовленную сцену; `BuildPlayer` выполняет обе фазы.
Профили переиспользуют прежние GUID и subassets, а не удаляются через DeleteAsset.

Сериализационные round-trip тесты используют GUID-пути и выполняются только в
изолированном batchmode Editor; в интерактивном Editor пропускаются, чтобы не закрыть
несохранённую пользовательскую сцену. Подтверждение исправления native level0 crash,
реальное визуальное качество и runtime exit-коды требуют отдельного Player build/run.
