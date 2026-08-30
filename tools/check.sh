#!/usr/bin/env bash
# Собирает и прогоняет одну чек-сцену лаборатории, печатает её строки из лога.
#
# Использование:
#   tools/check.sh Seam
#   tools/check.sh Seam PORTAL_NODEPTH=1
#
# Проверка сама завершает плеер через Application.Quit, поэтому скрипт ждёт
# его выхода и не требует таймаута.
set -u

UNITY="/c/Program Files/Unity/Hub/Editor/6000.5.9f1/Editor/Unity.exe"
PROJECT="C:/Users/faust/Documents/PortalsHDRP"

NAME="${1:?нужно имя проверки, например Seam}"
shift || true

case "$NAME" in
  Seam)        METHOD=SeamCheckBuilder.BuildPlayer;        DIR=BuildSeamCheck;        EXE=SeamCheck.exe        ;;
  Color)       METHOD=ColorCheckBuilder.BuildPlayer;       DIR=BuildColorCheck;       EXE=ColorCheck.exe       ;;
  Ghost)       METHOD=GhostCheckBuilder.BuildPlayer;       DIR=BuildGhostCheck;       EXE=GhostCheck.exe       ;;
  Rotate)      METHOD=RotateCheckBuilder.BuildPlayer;      DIR=BuildRotateCheck;      EXE=RotateCheck.exe      ;;
  Cross)       METHOD=CrossCheckBuilder.BuildPlayer;       DIR=BuildCrossCheck;       EXE=CrossCheck.exe       ;;
  Look)        METHOD=LookCheckBuilder.BuildPlayer;        DIR=BuildLookCheck;        EXE=LookCheck.exe        ;;
  Cinemachine) METHOD=CinemachineCheckBuilder.BuildPlayer; DIR=BuildCinemachineCheck; EXE=CinemachineCheck.exe ;;
  Bubble)      METHOD=BubbleCheckBuilder.BuildPlayer;      DIR=BuildBubbleCheck;      EXE=BubbleCheck.exe      ;;
  Close)       METHOD=CloseCheckBuilder.BuildPlayer;       DIR=BuildCloseCheck;       EXE=CloseCheck.exe       ;;
  Light)       METHOD=LightCheckBuilder.BuildPlayer;       DIR=BuildLightCheck;       EXE=LightCheck.exe       ;;
  Prefab)      METHOD=PrefabCheckBuilder.BuildPlayer;      DIR=BuildPrefabCheck;      EXE=PrefabCheck.exe      ;;
  AutoWire)    METHOD=AutoWireCheckBuilder.BuildPlayer;    DIR=BuildAutoWire;         EXE=AutoWire.exe         ;;
  Setup)       METHOD=SetupCheckBuild.BuildPlayer;         DIR=BuildSetupCheck;       EXE=SetupCheck.exe       ;;
  *) echo "неизвестная проверка: $NAME"; exit 2 ;;
esac

# Переменные вида PORTAL_NODEPTH=1 передаются сборщику сцены: он читает их
# через Environment.GetEnvironmentVariable и настраивает порталы соответственно.
for pair in "$@"; do
  export "${pair?}"
done

LOWER=$(echo "$NAME" | tr '[:upper:]' '[:lower:]')
BUILD_LOG="$PROJECT/${LOWER}build.log"
RUN_LOG="$PROJECT/${LOWER}run.log"

echo "=== сборка $NAME ==="
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -executeMethod "$METHOD" -logFile "$BUILD_LOG" -quit
BUILD_CODE=$?
if [ $BUILD_CODE -ne 0 ]; then
  echo "сборка упала, код $BUILD_CODE"
  tr -d '\000' < "$BUILD_LOG" | grep -aE "error CS|Error building|BuildResult|Exception" | head -30
  exit $BUILD_CODE
fi

echo "=== прогон $NAME ==="
cd "$PROJECT" || exit 1
"./$DIR/$EXE" -logfile "$RUN_LOG" -screen-width 1280 -screen-height 720 -screen-fullscreen 0
RUN_CODE=$?

echo "=== результат $NAME (код $RUN_CODE) ==="
# Проверки печатают многострочные отчёты одним Debug.Log, поэтому вывод идёт
# от строки с тегом и до конца сообщения: следом Unity дописывает стек вызовов
# и строку Filename, на них печать и обрывается.
tr -d '\000\r' < "$RUN_LOG" | awk '
    /^\[[A-Za-z]+(Check|Probe|Capture|Runner)\]/ { show = 1 }
    /^UnityEngine\.|^\(Filename:|^$/             { show = 0 }
    show'

PROBLEMS=$(tr -d '\000\r' < "$RUN_LOG" | grep -aE "Exception|NullReference|error CS" | head -20)
if [ -n "$PROBLEMS" ]; then
  echo "=== проблемы в логе ==="
  echo "$PROBLEMS"
fi

# Успех проверки определяется тем, что она успела записать свой отчёт, а не
# кодом выхода плеера. Плеер иногда падает уже после того, как все замеры
# сделаны, при выгрузке графики; замеры от этого не портятся, но код выхода
# становится ненулевым и прятал бы настоящий результат.
if tr -d '\000\r' < "$RUN_LOG" | grep -qaE "^\[[A-Za-z]+(Check|Probe|Capture)\]"; then
  if [ $RUN_CODE -ne 0 ]; then
    echo "ВНИМАНИЕ: замеры записаны, но плеер завершился с кодом $RUN_CODE (падение при выгрузке)"
  fi
  exit 0
fi

echo "ОШИБКА: проверка не записала ни одной строки отчёта, код выхода $RUN_CODE"
exit "${RUN_CODE:-1}"
