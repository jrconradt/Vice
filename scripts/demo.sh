#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

WITH_NET=0
[[ "${1:-}" == "--with-net" ]] && WITH_NET=1

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

run_step() {
  local label="$1"
  shift
  echo "==> $label"
  if "$@"; then
    echo "--- ok"
  else
    local rc=$?
    echo "--- failed (exit $rc), continuing"
  fi
  echo "----------------------------------------"
}

echo "==> build"
"$SCRIPT_DIR/build.sh"
echo "----------------------------------------"

echo "==> install-local"
"$SCRIPT_DIR/install-local.sh"
echo "----------------------------------------"

run_step "vice --help" vice --help
run_step "vice --version" vice --version
run_step "vice search files by type csharp in ./src" \
  vice search files by type csharp in ./src

if [[ "$WITH_NET" -eq 1 ]]; then
  run_step "vice search \"topological insulator\" on source arxiv --limit 1" \
    vice search "topological insulator" on source arxiv --limit 1
else
  echo "==> (skipped) network demos; pass --with-net to enable"
  echo "----------------------------------------"
fi

echo "==> uninstall-local"
"$SCRIPT_DIR/uninstall-local.sh"
echo "----------------------------------------"
echo "Demo complete."
