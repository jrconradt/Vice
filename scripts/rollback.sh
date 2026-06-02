#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -lt 1 || "$1" == "-h" || "$1" == "--help" ]]; then
  echo "usage: scripts/rollback.sh <version> [tool]"
  echo "  pins an installed Vice tool to a prior published version via 'dotnet tool update'"
  echo "  <version>  the version to roll back to, e.g. 0.1.0"
  echo "  [tool]     which tool to roll back: vice (default), vice-mux, or all"
  echo "  set \$VICE_NUGET_FEED to roll back from a non-default feed"
  exit 0
fi

VERSION="$1"
TOOL="${2:-vice}"
FEED_ARGS=()
if [[ -n "${VICE_NUGET_FEED:-}" ]]; then
  FEED_ARGS=(--add-source "$VICE_NUGET_FEED")
fi

rollback_one() {
  local pkg="$1"
  echo "==> rolling $pkg back to $VERSION"
  dotnet tool update --global "$pkg" --version "$VERSION" "${FEED_ARGS[@]}"
}

case "$TOOL" in
  vice)
    rollback_one Vice.Cli
    ;;
  vice-mux)
    rollback_one Vice.Mux.Cli
    ;;
  all)
    rollback_one Vice.Cli
    rollback_one Vice.Mux.Cli
    ;;
  *)
    echo "error: unknown tool '$TOOL' (expected vice, vice-mux, or all)" >&2
    exit 1
    ;;
esac

echo "Rolled $TOOL back to $VERSION. Verify with: vice --version"
