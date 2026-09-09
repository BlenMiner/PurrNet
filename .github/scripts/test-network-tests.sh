#!/usr/bin/env bash
# Exercise runner/result validation without Unity or network connections.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEST_DIR=$(mktemp -d "${TMPDIR:-/tmp}/purrnet-network-tests.XXXXXX")
cleanup() {
  # Only remove the test directory created above, using the same shell throughout.
  local resolved parent
  resolved=$(realpath "$TEST_DIR")
  parent=$(realpath "${TMPDIR:-/tmp}")
  [[ "$resolved" == "$parent"/purrnet-network-tests.* ]] && rm -rf -- "$resolved"
}
trap cleanup EXIT
mkdir "$TEST_DIR/bin"
cat > "$TEST_DIR/bin/xvfb-run" <<'MOCK_XVFB'
#!/usr/bin/env bash
while [[ "${1:-}" == -* ]]; do shift; done
exec "$@"
MOCK_XVFB
cat > "$TEST_DIR/bin/player" <<'MOCK_PLAYER'
#!/usr/bin/env bash
set -eu
role= scenario= results= log=
while (( $# )); do
  case "$1" in
    -role) role=$2; shift 2 ;;
    -scenario) scenario=$2; shift 2 ;;
    -results) results=$2; shift 2 ;;
    -logFile) log=$2; shift 2 ;;
    *) shift ;;
  esac
done
fault=
[[ "$results" != */client-1.json ]] || fault=${MOCK_FAULT:-}
[[ "${MOCK_FAULT:-}" != duplicated ]] || fault=duplicated
[[ "${MOCK_FAULT:-}" != extra-result ]] || fault=extra-result
[[ "${MOCK_FAULT:-}" != all-reordered ]] || fault=all-reordered
[[ "${MOCK_FAULT:-}" != all-wrong-name ]] || fault=all-wrong-name
printf '%s\n' 'Mock player completed' > "$log"
if [[ "$fault" == fatal-log ]]; then
  printf '%s\n' 'NullReferenceException: simulated hard failure' >> "$log"
fi
if [[ "$fault" == missing-script ]]; then
  printf '%s\n' "The referenced script on this Behaviour (Game Object 'OrdinaryJoinSceneX') is missing!" >> "$log"
fi
case "$fault" in
  missing-log) rm -- "$log" ;;
  other-exception) printf '%s\n' 'System.IO.IOException: unexpected failure' >> "$log" ;;
  assertion) printf '%s\n' 'Assertion failed: invalid state' >> "$log" ;;
  write-failure) printf '%s\n' 'Failed to write results' >> "$log" ;;
esac
if [[ "$fault" != missing-file ]]; then
  jq -n --arg scenario "$scenario" --arg fault "$fault" --slurpfile config "$SCENARIO_CONFIG" '
    (if $scenario == "" then
      ($config[0].runs[] | select(.scenario == "")) as $full |
      $full.expectedScenarios
    else ["Bootstrap", $scenario] end) |
    map({name: ., result: {success: true}}) |
    if $fault == "reordered" then reverse
    elif $fault == "truncated" then .[:-1]
    elif $fault == "wrong-name" then .[0].name = "WrongScenario"
    elif $fault == "failed" then .[1].result.success = false
    elif $fault == "duplicated" then . + [.[-1]]
    elif $fault == "extra-result" then . + [{name:"UnexpectedScenario", result:{success:true}}]
    elif $fault == "all-reordered" then reverse
    elif $fault == "all-wrong-name" then .[4].name = "UnexpectedScenario"
    else . end' > "$results"
fi
# Keep the authority alive while the runner starts its clients.
sleep 0.3
MOCK_PLAYER
chmod +x "$TEST_DIR/bin/xvfb-run" "$TEST_DIR/bin/player"
export PATH="$TEST_DIR/bin:$PATH"
export BIN="$TEST_DIR/bin/player" CLIENT_COUNT=3 CONNECT_DELAY=0
# Signal/PID ownership is exercised separately by test-host-loss.py on Linux.
jq '.runs |= map(select(.fault == null))' "$SCRIPT_DIR/../network-scenarios.json" > "$TEST_DIR/scenarios.json"
jq '.runs |= map(select(.scenario == ""))' "$SCRIPT_DIR/../network-scenarios.json" > "$TEST_DIR/full-only.json"
jq 'del(.runs[0].expectedScenarios)' "$TEST_DIR/full-only.json" > "$TEST_DIR/missing-expected.json"
export SCENARIO_CONFIG="$TEST_DIR/scenarios.json"

run_case() {
  local name=$1 mode=$2 scenario=$3 fault=$4 expected=$5 pattern=${6:-}
  local status=0
  MODE="$mode" SCENARIO="$scenario" MOCK_FAULT="$fault" \
    RESULTS_ROOT="$TEST_DIR/$name/results" LOGS_ROOT="$TEST_DIR/$name/logs" \
    bash "$SCRIPT_DIR/network-tests.sh" > "$TEST_DIR/$name.log" 2>&1 || status=$?
  if [[ "$expected" == pass && "$status" != 0 ]] || [[ "$expected" == fail && "$status" == 0 ]]; then
    cat "$TEST_DIR/$name.log"
    echo "Unexpected runner status for $name: $status" >&2
    exit 1
  fi
  if [[ -n "$pattern" ]] && ! grep -q "$pattern" "$TEST_DIR/$name.log"; then
    cat "$TEST_DIR/$name.log"
    echo "Expected diagnostic not found for $name: $pattern" >&2
    exit 1
  fi
  echo "PASS $name"
}

run_case full-server server '' '' pass
run_case full-host host '' '' pass
normal_batches=$(jq '.runs | length' "$SCENARIO_CONFIG")
[[ $(find "$TEST_DIR/full-server/results" -name '*.json' | wc -l) -eq $((4 * normal_batches)) ]]
[[ $(find "$TEST_DIR/full-host/results" -name '*.json' | wc -l) -eq $((3 * normal_batches)) ]]
run_case targeted host OrdinaryJoinDuplicateScenesScenario '' pass
[[ $(find "$TEST_DIR/targeted/results" -name '*.json' | wc -l) -eq 3 ]]
run_case unknown-target server AnotherScenario '' pass
run_case missing-file host OrdinaryJoinDuplicateScenesScenario missing-file fail 'Missing or empty result file'
run_case truncated host OrdinaryJoinDuplicateScenesScenario truncated fail 'complete ordered scenario results'
run_case reordered host OrdinaryJoinDuplicateScenesScenario reordered fail 'complete ordered scenario results'
run_case wrong-name host OrdinaryJoinDuplicateScenesScenario wrong-name fail 'complete ordered scenario results'
run_case extra-result host MigrationFailureCleanupScenario extra-result fail 'complete ordered scenario results'
run_case failed host OrdinaryJoinDuplicateScenesScenario failed fail 'complete ordered scenario results'
run_case fatal-log host OrdinaryJoinDuplicateScenesScenario fatal-log fail 'fatal log patterns'
run_case missing-script host OrdinaryJoinAddressableSceneScenario missing-script fail 'fatal log patterns'
run_case missing-log host OrdinaryJoinDuplicateScenesScenario missing-log fail 'Missing player log'
run_case other-exception host OrdinaryJoinDuplicateScenesScenario other-exception fail 'fatal log patterns'
run_case assertion host OrdinaryJoinDuplicateScenesScenario assertion fail 'fatal log patterns'
run_case write-failure host OrdinaryJoinDuplicateScenesScenario write-failure fail 'fatal log patterns'
SCENARIO_CONFIG="$TEST_DIR/full-only.json" run_case full-wrong-name host '' wrong-name fail 'complete ordered scenario results'
SCENARIO_CONFIG="$TEST_DIR/full-only.json" run_case full-reordered host '' reordered fail 'complete ordered scenario results'
SCENARIO_CONFIG="$TEST_DIR/full-only.json" run_case full-duplicated host '' duplicated fail 'complete ordered scenario results'
SCENARIO_CONFIG="$TEST_DIR/full-only.json" run_case full-all-reordered host '' all-reordered fail 'complete ordered scenario results'
SCENARIO_CONFIG="$TEST_DIR/full-only.json" run_case full-all-wrong-name host '' all-wrong-name fail 'complete ordered scenario results'
SCENARIO_CONFIG="$TEST_DIR/missing-expected.json" run_case missing-expected-list host '' '' fail 'Invalid run configuration'
CLIENT_COUNT=2 run_case insufficient-players host '' '' fail 'insufficient players'
# Reusing a previous directory must fail instead of accepting stale JSON/ready markers.
run_case targeted host OrdinaryJoinDuplicateScenesScenario '' fail
echo 'All runner checks passed.'
