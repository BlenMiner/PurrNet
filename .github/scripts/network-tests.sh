#!/usr/bin/env bash
# Run each configured batch in fresh processes against the same built player.
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG="${SCENARIO_CONFIG:-$SCRIPT_DIR/../network-scenarios.json}"
BIN="${BIN:-$PWD/build/StandaloneLinux64/PurrNetTests}"
MODE="${MODE:?MODE must be server or host}"
TOTAL_COUNT="${CLIENT_COUNT:?CLIENT_COUNT must specify the total player count}"
CONNECT_DELAY="${CONNECT_DELAY:-1}"
SCENARIO="${SCENARIO:-}"
RESULTS_ROOT="${RESULTS_ROOT:-$PWD/test-results}"
LOGS_ROOT="${LOGS_ROOT:-$PWD/test-logs}"

command -v jq >/dev/null || { echo "::error::jq is required to validate complete results"; exit 1; }
command -v timeout >/dev/null || { echo "::error::timeout is required to bound player runs"; exit 1; }
[[ "$MODE" == server || "$MODE" == host ]] || { echo "::error::Invalid MODE: $MODE"; exit 1; }
[[ "$TOTAL_COUNT" =~ ^[0-9]+$ ]] || { echo "::error::CLIENT_COUNT must be an integer"; exit 1; }
[[ "$SCENARIO" =~ ^[A-Za-z0-9_]*$ ]] || { echo "::error::Invalid scenario component name"; exit 1; }
TOTAL_COUNT=$((10#$TOTAL_COUNT))
EXTERNAL_CLIENTS=$TOTAL_COUNT
if [[ "$MODE" == host ]]; then
  EXTERNAL_CLIENTS=$((TOTAL_COUNT - 1))
fi
if (( EXTERNAL_CLIENTS < 1 )); then
  echo "::error::$MODE mode requires at least one external client"
  exit 1
fi

if [[ -n "$SCENARIO" ]]; then
  RUNS=$(jq -c --arg scenario "$SCENARIO" '
    [.runs[] | select(.scenario == $scenario)] |
    if length > 0 then . else [{name: $scenario, scenario: $scenario,
      minimumPlayers: 2, timeoutSeconds: 300}] end' "$CONFIG") || exit 1
else
  RUNS=$(jq -c '.runs' "$CONFIG") || exit 1
fi
# A targeted run always consists of bootstrap and its selected scenario.
RUNS=$(jq -c 'map(if .scenario != "" then .expectedScenarios = ["Bootstrap", .scenario] else . end)' <<< "$RUNS") || exit 1
if ! jq -e --argjson players "$TOTAL_COUNT" '
  type == "array" and length > 0 and
  all(.[]; (.minimumPlayers <= $players) and
    (.expectedScenarios | type == "array" and length > 0 and
      all(.[]; type == "string" and length > 0) and length == (unique | length)) and
    ((.fault // "") == "" or (.fault == "kill-authority" and .scenario == "UnexpectedHostLossMigrationScenario")) and
    (.timeoutSeconds > 0) and (.name | test("^[A-Za-z0-9_-]+$")))' <<< "$RUNS" >/dev/null; then
  echo "::error::Invalid run configuration or insufficient players; default batches need at least 3 players"
  exit 1
fi
mkdir -p "$RESULTS_ROOT" "$LOGS_ROOT" || exit 1

# timeout owns the process group, including xvfb-run and its Unity child.
ACTIVE_PIDS=()
cleanup() {
  local pid
  for pid in "${ACTIVE_PIDS[@]}"; do
    kill "$pid" 2>/dev/null || true
  done
  for pid in "${ACTIVE_PIDS[@]}"; do
    wait "$pid" 2>/dev/null || true
  done
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

validate_result() {
  local file=$1 spec=$2
  if [[ ! -s "$file" ]]; then
    echo "::error::Missing or empty result file: $file"
    return 1
  fi
  if ! jq -e --argjson spec "$spec" '
    type == "array" and map(.name) == $spec.expectedScenarios and
    all(.[]; .result.success == true)' "$file" >/dev/null; then
    echo "::error::$file does not contain successful complete ordered scenario results"
    return 1
  fi
}

run_batch() {
  local spec=$1 label scenario seconds results_dir logs_dir authority_pid pid code file i fault
  local fail=0
  label=$(jq -r '.name' <<< "$spec")
  scenario=$(jq -r '.scenario' <<< "$spec")
  seconds=$(jq -r '.timeoutSeconds' <<< "$spec")
  fault=$(jq -r '.fault // ""' <<< "$spec")
  results_dir="$RESULTS_ROOT/$label"
  logs_dir="$LOGS_ROOT/$label"
  # Pre-connection scenarios coordinate through this directory. Never reuse a stale marker.
  mkdir "$results_dir" "$logs_dir" || return 1
  local scenario_args=()
  [[ -z "$scenario" ]] || scenario_args=(-scenario "$scenario")
  local run=(timeout --signal=TERM --kill-after=15s "${seconds}s"
    xvfb-run -a --server-args="-screen 0 1024x768x24")

  echo "::group::$label — launching $MODE and $EXTERNAL_CLIENTS external clients"
  if [[ "$fault" == kill-authority ]]; then
    python3 "$SCRIPT_DIR/run-host-loss.py" --binary "$BIN" --mode "$MODE" --players "$TOTAL_COUNT" \
      --scenario "$scenario" --results "$results_dir" --logs "$logs_dir" \
      --timeout "$seconds" --connect-delay "$CONNECT_DELAY" &
    pid=$!
    ACTIVE_PIDS=("$pid")
    wait "$pid" || fail=1
  else
  "${run[@]}" "$BIN" -batchmode -nographics -role "$MODE" -count "$TOTAL_COUNT" \
    "${scenario_args[@]}" -results "$results_dir/$MODE.json" -logFile "$logs_dir/$MODE.log" &
  authority_pid=$!
  ACTIVE_PIDS=("$authority_pid")
  sleep "$CONNECT_DELAY"
  if ! kill -0 "$authority_pid" 2>/dev/null; then
    echo "::error::$MODE process died before clients could start ($label)"
    wait "$authority_pid" || true
    ACTIVE_PIDS=()
    tail -n 200 "$logs_dir/$MODE.log" || true
    echo "::endgroup::"
    return 1
  fi

  local client_pids=()
  for ((i = 1; i <= EXTERNAL_CLIENTS; i++)); do
    "${run[@]}" "$BIN" -batchmode -nographics -role client \
      "${scenario_args[@]}" -results "$results_dir/client-$i.json" -logFile "$logs_dir/client-$i.log" &
    pid=$!
    client_pids+=("$pid")
    ACTIVE_PIDS+=("$pid")
  done
  for pid in "${client_pids[@]}" "$authority_pid"; do
    if wait "$pid"; then
      echo "Player PID $pid exited 0"
    else
      code=$?
      echo "::error::Player PID $pid exited $code ($label)"
      fail=1
    fi
  done
  fi
  ACTIVE_PIDS=()
  echo "::endgroup::"

  if [[ "$fault" != kill-authority ]]; then
    validate_result "$results_dir/$MODE.json" "$spec" || fail=1
  fi
  for ((i = 1; i <= EXTERNAL_CLIENTS; i++)); do
    validate_result "$results_dir/client-$i.json" "$spec" || fail=1
  done

  local fatal_pattern="\b([A-Za-z_][A-Za-z0-9_.]*)?Exception\b|Assertion failed|\bFATAL\b|CompleteSpawn: CreatePrototype failed|SceneID '[0-9]+' not found|Address already in use|The referenced script on this Behaviour .* is missing!|Segmentation fault|SIGSEGV|Crash!!!|Native Crash Reporting|Failed to write results"
  local logs=("$logs_dir/$MODE.log")
  for ((i = 1; i <= EXTERNAL_CLIENTS; i++)); do logs+=("$logs_dir/client-$i.log"); done
  for file in "${logs[@]}"; do
    if [[ ! -f "$file" ]]; then
      echo "::error::Missing player log: $file"
      fail=1
    elif grep -E -i -n "$fatal_pattern" "$file"; then
      echo "::error::$file contains fatal log patterns"
      fail=1
    fi
  done
  return "$fail"
}

FAIL=0
while IFS= read -r spec; do
  run_batch "$spec" || FAIL=1
done < <(jq -c '.[]' <<< "$RUNS")
exit "$FAIL"
