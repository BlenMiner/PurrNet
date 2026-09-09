#!/usr/bin/env bash
# A fallback Library cache can contain stale SBP scene/MonoScript dependencies.
# Validate both targets before deleting either; never follow a redirected cache path.
set -euo pipefail

fail() { echo "::error::$*" >&2; exit 1; }
[[ $# -eq 1 ]] || fail 'Expected one explicit Unity workspace directory'
workspace_input=${1%/}
[[ -n "$workspace_input" && -d "$workspace_input" && ! -L "$workspace_input" ]] ||
  fail 'Workspace must be an existing directory, not a symlink'
workspace=$(realpath -e -- "$workspace_input")
[[ "$workspace" != / && -d "$workspace/Assets" && -d "$workspace/ProjectSettings" ]] ||
  fail 'Workspace must contain Assets and ProjectSettings'

library="$workspace/Library"
if [[ ! -e "$library" && ! -L "$library" ]]; then
  echo 'No Library directory exists; no content caches to reset.'
  exit 0
fi
[[ -d "$library" && ! -L "$library" ]] || fail 'Library must be a directory, not a symlink or file'
[[ $(realpath -e -- "$library") == "$library" ]] || fail 'Library resolves outside its expected workspace path'

targets=()
for name in BuildCache com.unity.addressables; do
  target="$library/$name"
  [[ ! -L "$target" ]] || fail "Refusing symlinked content cache: Library/$name"
  [[ -e "$target" ]] || continue
  [[ -d "$target" ]] || fail "Content cache is not a directory: Library/$name"
  resolved=$(realpath -e -- "$target")
  [[ "$resolved" == "$workspace/Library/$name" ]] ||
    fail "Content cache resolves outside its expected workspace path: Library/$name"
  targets+=("$resolved")
done

for target in "${targets[@]}"; do
  rm -rf -- "$target"
  echo "Cleared stale content cache: Library/$(basename "$target")"
done
