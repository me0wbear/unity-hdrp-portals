#!/usr/bin/env bash
# Сборка и проверка Player по строгому итоговому контракту. См. tools/README.md.
set -u
set -o pipefail

fail() { printf 'ОШИБКА: %s\n' "$*" >&2; exit 1; }

NAME=${1:-}
[[ -n "$NAME" ]] || fail 'Укажите имя проверки, например Seam.'
shift
case "$NAME" in
  Seam)          METHOD=SeamCheckBuilder.BuildPlayer;          DIR=BuildSeamCheck;              EXE=SeamCheck.exe ;;
  Color)         METHOD=ColorCheckBuilder.BuildPlayer;         DIR=BuildColorCheck;             EXE=ColorCheck.exe ;;
  Ghost)         METHOD=GhostCheckBuilder.BuildPlayer;         DIR=BuildGhostCheck;             EXE=GhostCheck.exe ;;
  Rotate)        METHOD=RotateCheckBuilder.BuildPlayer;        DIR=BuildRotateCheck;            EXE=RotateCheck.exe ;;
  Cross)         METHOD=CrossCheckBuilder.BuildPlayer;         DIR=BuildCrossCheck;             EXE=CrossCheck.exe ;;
  Look)          METHOD=LookCheckBuilder.BuildPlayer;          DIR=BuildLookCheck;              EXE=LookCheck.exe ;;
  Cinemachine)   METHOD=CinemachineCheckBuilder.BuildPlayer;   DIR=BuildCinemachineCheck;       EXE=CinemachineCheck.exe ;;
  Bubble)        METHOD=BubbleCheckBuilder.BuildPlayer;        DIR=BuildBubbleCheck;            EXE=BubbleCheck.exe ;;
  Close)         METHOD=CloseCheckBuilder.BuildPlayer;         DIR=BuildCloseCheck;             EXE=CloseCheck.exe ;;
  Light)         METHOD=LightCheckBuilder.BuildPlayer;         DIR=BuildLightCheck;             EXE=LightCheck.exe ;;
  Prefab)        METHOD=PrefabCheckBuilder.BuildPlayer;        DIR=BuildPrefabCheck;            EXE=PrefabCheck.exe ;;
  AutoWire)      METHOD=AutoWireCheckBuilder.BuildPlayer;      DIR=BuildAutoWire;               EXE=AutoWire.exe ;;
  Setup)         METHOD=SetupCheckBuild.BuildPlayer;           DIR=BuildSetupCheck;             EXE=SetupCheck.exe ;;
  SandboxParity) METHOD=SandboxParityCheckBuilder.BuildPlayer; DIR=BuildSandboxParityCheck;     EXE=SandboxParityCheck.exe ;;
  Performance)   METHOD=PortalPerformanceCheckBuilder.BuildPlayer; DIR=BuildPortalPerformanceCheck; EXE=PortalPerformanceCheck.exe ;;
  Visibility)    METHOD=PortalVisibilityCheckBuilder.BuildPlayer; DIR=BuildPortalVisibilityCheck; EXE=PortalVisibilityCheck.exe ;;
  *) fail "Неизвестная проверка: $NAME" ;;
esac

# Значения передаются буквально, без eval. Служебные переменные нельзя подменять.
OVERRIDES=()
for pair in "$@"; do
  [[ "$pair" =~ ^[A-Za-z_][A-Za-z0-9_]*= ]] || fail 'Ожидается аргумент NAME=value.'
  key=${pair%%=*}
  case "${key^^}" in
    PORTAL_CHECK_*|PORTAL_RUNNER_*|PORTAL_PROJECT|PORTAL_UNITY|PATH|PATHEXT|COMSPEC|PSMODULEPATH|HOME|ENV|BASH*|SHELLOPTS|CDPATH|IFS|LD_*|DYLD_*|MSYS*)
      fail "Переменная зарезервирована: $key" ;;
  esac
  [[ "$pair" != *$'\n'* && "$pair" != *$'\r'* ]] || fail 'Переводы строк в аргументах не поддерживаются.'
  OVERRIDES+=("$pair")
done

command -v cygpath >/dev/null 2>&1 || fail 'Требуется Git for Windows Bash с cygpath.'
SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P) || fail 'Не найден каталог tools.'
PROJECT_INPUT=${PORTAL_PROJECT:-"$SCRIPT_DIR/.."}
PROJECT_POSIX=$(cd -- "$(cygpath -u "$PROJECT_INPUT")" && pwd -P) || fail 'Не найден каталог проекта.'
PROJECT=$(cygpath -am "$PROJECT_POSIX") || fail 'Не удалось нормализовать путь проекта.'
[[ -f "$PROJECT_POSIX/ProjectSettings/ProjectVersion.txt" ]] || fail "Не найден ProjectSettings/ProjectVersion.txt: $PROJECT"
[[ ! -e "$PROJECT_POSIX/Temp/UnityLockfile" ]] || fail "Проект занят Unity (Temp/UnityLockfile): $PROJECT"

POWERSHELL=''
for candidate in powershell.exe pwsh.exe; do
  if command -v "$candidate" >/dev/null 2>&1; then POWERSHELL=$(command -v "$candidate"); break; fi
done
[[ -n "$POWERSHELL" ]] || fail 'Не найдены powershell.exe или pwsh.exe; проверка результата невозможна.'
[[ -f "$SCRIPT_DIR/verify-check.ps1" ]] || fail 'Не найден tools/verify-check.ps1.'
UNITY=$(cygpath -u "${PORTAL_UNITY:-C:/Program Files/Unity/Hub/Editor/6000.5.9f1/Editor/Unity.exe}") || fail 'Некорректный путь Unity.'
[[ -f "$UNITY" && -x "$UNITY" ]] || fail "Не найден исполняемый файл Unity: $UNITY"
COMMIT=$(git -C "$PROJECT_POSIX" rev-parse --verify HEAD) || fail 'Не удалось получить SHA проекта.'
[[ "$COMMIT" =~ ^([0-9a-f]{40}|[0-9a-f]{64})$ ]] || fail 'Ожидается полный Git SHA.'

CHECKS_DIR="$PROJECT_POSIX/Logs/checks"
mkdir -p -- "$CHECKS_DIR" || fail 'Не удалось создать каталог Logs/checks.'
LOCK_DIR="$CHECKS_DIR/.runner-lock"
# mkdir атомарен; оставшийся после аварии lock не удаляется по предположению о PID.
mkdir -- "$LOCK_DIR" 2>/dev/null || fail "Другой runner владеет блокировкой: $LOCK_DIR. См. README."
cleanup() { rmdir -- "$LOCK_DIR" 2>/dev/null || printf 'Не удалось освободить блокировку: %s\n' "$LOCK_DIR" >&2; }
trap cleanup EXIT
# При прерывании дочерний Windows-процесс может остаться активным: lock сохраняется.
trap 'trap - EXIT; exit 130' INT
trap 'trap - EXIT; exit 143' TERM

RUN_ID=$("$POWERSHELL" -NoLogo -NoProfile -NonInteractive -Command '[Guid]::NewGuid().ToString("N")') || fail 'Не удалось создать runId.'
RUN_ID=${RUN_ID//$'\r'/}
[[ "$RUN_ID" =~ ^[0-9a-f]{32}$ ]] || fail 'Некорректный runId.'
TIMESTAMP=$(date -u +%Y%m%dT%H%M%SZ) || fail 'Не удалось получить время запуска.'
OUTPUT_POSIX="$CHECKS_DIR/$NAME-$TIMESTAMP-$RUN_ID"
mkdir -- "$OUTPUT_POSIX" || fail 'Не удалось создать уникальный каталог запуска.'
OUTPUT=$(cygpath -am "$OUTPUT_POSIX") || fail 'Некорректный путь логов.'
WARMUP_LOG="$OUTPUT/warmup.log"
BUILD_LOG="$OUTPUT/build.log"
RUN_LOG="$OUTPUT/player.log"
touch -- "$OUTPUT_POSIX/warmup.log" "$OUTPUT_POSIX/build.log" "$OUTPUT_POSIX/player.log" || fail 'Не удалось создать логи.'
printf 'Проверка: %s\nПроект: %s\nCommit: %s\nRun ID: %s\nЛоги: %s\n' "$NAME" "$PROJECT" "$COMMIT" "$RUN_ID" "$OUTPUT"
printf 'warmup: %s\nbuild: %s\nplayer: %s\n' "$WARMUP_LOG" "$BUILD_LOG" "$RUN_LOG"

export PORTAL_CHECK_NAME="$NAME" PORTAL_CHECK_COMMIT="$COMMIT" PORTAL_CHECK_PROJECT="$PROJECT"
export PORTAL_CHECK_RUN_ID="$RUN_ID" PORTAL_CHECK_OUTPUT="$OUTPUT"
cd -- "$PROJECT_POSIX" || fail 'Не удалось перейти в проект.'
[[ ! -e "$PROJECT_POSIX/Temp/UnityLockfile" ]] || fail 'Unity заняла проект до запуска warmup.'

# Bash обрезает Windows exit code до 8 бит (256 становится 0).
# PowerShell сохраняет полный код в отдельном файле; отсутствие кода означает отказ.
run_native() {
  local stage=$1 executable=$2 log_path=$3 wrapper_code shell_code exit_file
  exit_file="$OUTPUT_POSIX/$stage.exitcode"
  NATIVE_EXIT=1
  env "${OVERRIDES[@]}" PORTAL_RUNNER_STAGE="$stage" \
    PORTAL_RUNNER_EXE="$(cygpath -am "$executable")" PORTAL_RUNNER_LOG="$log_path" \
    PORTAL_RUNNER_METHOD="$METHOD" PORTAL_RUNNER_WIDTH="${WIDTH:-1280}" PORTAL_RUNNER_HEIGHT="${HEIGHT:-720}" \
    "$POWERSHELL" -NoLogo -NoProfile -NonInteractive -Command '
      $ErrorActionPreference = "Stop"
      try {
        if ($env:PORTAL_RUNNER_STAGE -eq "player") {
          $processArgs = @("-logfile", $env:PORTAL_RUNNER_LOG, "-screen-width", $env:PORTAL_RUNNER_WIDTH,
            "-screen-height", $env:PORTAL_RUNNER_HEIGHT, "-screen-fullscreen", "0")
        } else {
          $processArgs = @("-batchmode", "-nographics", "-projectPath", $env:PORTAL_CHECK_PROJECT,
            "-quit", "-logFile", $env:PORTAL_RUNNER_LOG)
          if ($env:PORTAL_RUNNER_STAGE -eq "build") { $processArgs += @("-executeMethod", $env:PORTAL_RUNNER_METHOD) }
        }
        & $env:PORTAL_RUNNER_EXE @processArgs | Out-Host
        $nativeExit = $LASTEXITCODE
        if ($null -eq $nativeExit) { throw "Native exit code is unavailable." }
        [IO.File]::WriteAllText(($env:PORTAL_CHECK_OUTPUT + "/" + $env:PORTAL_RUNNER_STAGE + ".exitcode"),
          $nativeExit.ToString([Globalization.CultureInfo]::InvariantCulture))
        if ($nativeExit -ne 0) { exit 1 }
        exit 0
      } catch {
        Write-Output ("Process launch failed: " + $_.Exception.Message)
        exit 1
      }
    '
  wrapper_code=$?
  if [[ -f "$exit_file" ]]; then NATIVE_EXIT=$(< "$exit_file"); fi
  if [[ ! "$NATIVE_EXIT" =~ ^-?[0-9]+$ ]]; then NATIVE_EXIT=1; fi
  if (( wrapper_code != 0 && NATIVE_EXIT == 0 )); then NATIVE_EXIT=1; fi
  shell_code=$(( (NATIVE_EXIT % 256 + 256) % 256 ))
  if (( NATIVE_EXIT != 0 && shell_code == 0 )); then shell_code=1; fi
  return "$shell_code"
}

printf 'Компиляция: %s\n' "$WARMUP_LOG"
run_native warmup "$UNITY" "$WARMUP_LOG"
WARMUP_CODE=$?
if (( WARMUP_CODE != 0 )); then
  printf 'Warmup завершился с Windows-кодом %s. Лог: %s\n' "$NATIVE_EXIT" "$WARMUP_LOG" >&2
  exit "$WARMUP_CODE"
fi

PLAYER="$PROJECT_POSIX/$DIR/$EXE"
# Удаляем только конкретный старый EXE внутри проверенного каталога сборки.
# Остальные файлы сборки и логи предыдущих запусков сохраняются.
if [[ -e "$PLAYER" || -L "$PLAYER" ]]; then
  BUILD_DIR=$(cd -- "$PROJECT_POSIX/$DIR" && pwd -P) || fail 'Не удалось проверить каталог сборки.'
  [[ "$BUILD_DIR" == "$PROJECT_POSIX/$DIR" && -f "$PLAYER" && ! -L "$PLAYER" ]] || fail 'Небезопасный путь старого EXE.'
  rm -- "$PLAYER" || fail "Не удалось удалить старый EXE: $PLAYER"
fi
printf 'Сборка %s: %s\n' "$NAME" "$BUILD_LOG"
run_native build "$UNITY" "$BUILD_LOG"
BUILD_CODE=$?
if (( BUILD_CODE != 0 )); then
  printf 'Сборка завершилась с Windows-кодом %s. Лог: %s\n' "$NATIVE_EXIT" "$BUILD_LOG" >&2
  exit "$BUILD_CODE"
fi
[[ -f "$PLAYER" && -x "$PLAYER" && ! -L "$PLAYER" ]] || fail "Сборка не создала новый EXE: $PLAYER. Лог: $BUILD_LOG"

WIDTH=1280; HEIGHT=720
if [[ "$NAME" == Performance ]]; then WIDTH=1920; HEIGHT=1080; fi
printf 'Прогон %s: %s\n' "$NAME" "$RUN_LOG"
run_native player "$PLAYER" "$RUN_LOG"
RUN_CODE=$?
"$POWERSHELL" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass \
  -File "$(cygpath -am "$SCRIPT_DIR/verify-check.ps1")" -LogPath "$RUN_LOG" \
  -ExpectedCheck "$NAME" -ExpectedCommit "$COMMIT" -ExpectedProjectPath "$PROJECT" \
  -ExpectedRunId "$RUN_ID" -PlayerExitCode "$NATIVE_EXIT"
VERIFY_CODE=$?
if (( RUN_CODE != 0 )); then exit "$RUN_CODE"; fi
if (( VERIFY_CODE != 0 )); then exit 1; fi
exit 0
