#!/usr/bin/env bash
# Use only temporary fixtures; never touch the repository's actual Library directory.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEST_DIR=$(mktemp -d "${TMPDIR:-/tmp}/purrnet-content-cache-tests.XXXXXX")
cleanup() {
  local resolved parent
  resolved=$(realpath "$TEST_DIR")
  parent=$(realpath "${TMPDIR:-/tmp}")
  [[ "$resolved" == "$parent"/purrnet-content-cache-tests.* ]] && rm -rf -- "$resolved"
}
trap cleanup EXIT

make_workspace() {
  workspace="$TEST_DIR/$1"
  mkdir -p "$workspace/Assets" "$workspace/ProjectSettings" "$workspace/Library/Bee"
  printf 'keep Bee\n' > "$workspace/Library/Bee/sentinel"
  printf 'keep other Library data\n' > "$workspace/Library/other-sentinel"
}
expect_refused() {
  local target=$1
  if bash "$SCRIPT_DIR/reset-addressable-build-caches.sh" "$target" > "$TEST_DIR/refused.log" 2>&1; then
    echo "Cache cleanup should have refused $target" >&2
    exit 1
  fi
  grep -q '::error::' "$TEST_DIR/refused.log"
}
assert_retained() {
  [[ -f "$workspace/Library/Bee/sentinel" && -f "$workspace/Library/other-sentinel" ]]
  [[ -f "$TEST_DIR/outside/sentinel" ]]
}
mkdir "$TEST_DIR/outside"
printf 'outside workspace\n' > "$TEST_DIR/outside/sentinel"

make_workspace normal
mkdir -p "$workspace/Library/BuildCache/subdir" "$workspace/Library/com.unity.addressables"
touch "$workspace/Library/BuildCache/subdir/stale" "$workspace/Library/com.unity.addressables/stale"
bash "$SCRIPT_DIR/reset-addressable-build-caches.sh" "$workspace"
[[ ! -e "$workspace/Library/BuildCache" && ! -e "$workspace/Library/com.unity.addressables" ]]
assert_retained
echo 'PASS clears only the two content caches'

bash "$SCRIPT_DIR/reset-addressable-build-caches.sh" "$workspace"
assert_retained
echo 'PASS absent caches are harmless'

make_workspace partial
mkdir "$workspace/Library/BuildCache"
bash "$SCRIPT_DIR/reset-addressable-build-caches.sh" "$workspace"
[[ ! -e "$workspace/Library/BuildCache" ]]
assert_retained
echo 'PASS a single existing content cache is cleared'

mkdir -p "$TEST_DIR/no-library/Assets" "$TEST_DIR/no-library/ProjectSettings"
bash "$SCRIPT_DIR/reset-addressable-build-caches.sh" "$TEST_DIR/no-library"
[[ ! -e "$TEST_DIR/no-library/Library" ]]
echo 'PASS missing Library is harmless'

for name in BuildCache com.unity.addressables; do
  make_workspace "symlink-$name"
  other=BuildCache
  [[ "$name" != BuildCache ]] || other=com.unity.addressables
  mkdir "$workspace/Library/$other"
  touch "$workspace/Library/$other/stale"
  ln -s "$TEST_DIR/outside" "$workspace/Library/$name"
  [[ -L "$workspace/Library/$name" ]]
  expect_refused "$workspace"
  [[ -L "$workspace/Library/$name" && -f "$workspace/Library/$other/stale" ]]
  assert_retained
  echo "PASS refuses symlinked $name before deleting either cache"
done

mkdir -p "$TEST_DIR/symlink-library/Assets" "$TEST_DIR/symlink-library/ProjectSettings"
ln -s "$TEST_DIR/outside" "$TEST_DIR/symlink-library/Library"
expect_refused "$TEST_DIR/symlink-library"
[[ -L "$TEST_DIR/symlink-library/Library" && -f "$TEST_DIR/outside/sentinel" ]]
echo 'PASS refuses symlinked Library'

make_workspace symlink-workspace-target
ln -s "$workspace" "$TEST_DIR/symlink-workspace"
expect_refused "$TEST_DIR/symlink-workspace"
assert_retained
echo 'PASS refuses symlinked workspace'

make_workspace file-cache
mkdir "$workspace/Library/BuildCache"
touch "$workspace/Library/BuildCache/stale" "$workspace/Library/com.unity.addressables"
expect_refused "$workspace"
[[ -f "$workspace/Library/BuildCache/stale" && -f "$workspace/Library/com.unity.addressables" ]]
assert_retained
echo 'PASS refuses file target without partially clearing valid cache'

mkdir "$TEST_DIR/not-unity"
expect_refused "$TEST_DIR/not-unity"
echo 'PASS refuses a directory without Unity workspace markers'
echo 'All 10 content cache checks passed.'
