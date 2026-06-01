#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

PROPS="Directory.Build.props"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROPS" | head -n1)"
if [[ -z "$VERSION" ]]; then
  echo "error: could not read <Version> from $PROPS" >&2
  exit 1
fi

export CI=true

FEED="${VICE_NUGET_FEED:-https://api.nuget.org/v3/index.json}"
OUT_DIR="$(pwd)/artifacts/release/$VERSION"
PUSH=0
VERIFY_TAG=0

while [[ "$#" -gt 0 ]]; do
  case "$1" in
    --push)
      PUSH=1
      ;;
    --verify-tag)
      VERIFY_TAG=1
      ;;
    -h|--help)
      echo "usage: scripts/release.sh [--push] [--verify-tag]"
      echo "  packs Vice, Vice.Parser, Vice.Cli, and Vice.Mux.Cli at version $VERSION into $OUT_DIR"
      echo "  --push        push the produced .nupkg files to \$VICE_NUGET_FEED (default nuget.org)"
      echo "  --verify-tag  require an annotated git tag v$VERSION at HEAD before packing"
      echo "  push requires \$NUGET_API_KEY in the environment"
      exit 0
      ;;
    *)
      echo "error: unknown argument: $1" >&2
      exit 1
      ;;
  esac
  shift
done

if [[ "$VERIFY_TAG" -eq 1 ]]; then
  TAG="v$VERSION"
  if ! git rev-parse -q --verify "refs/tags/$TAG" >/dev/null; then
    echo "error: tag $TAG does not exist; create it with: git tag -a $TAG -m \"$TAG\"" >&2
    exit 1
  fi
  if [[ "$(git rev-parse "$TAG^{commit}")" != "$(git rev-parse HEAD)" ]]; then
    echo "error: tag $TAG does not point at HEAD; release must be built from the tagged commit" >&2
    exit 1
  fi
  if ! git diff --quiet || ! git diff --cached --quiet; then
    echo "error: working tree has uncommitted changes; release must be packed from a clean tree (EmbedUntrackedSources embeds local edits)" >&2
    exit 1
  fi
  if [[ -n "$(git ls-files --others --exclude-standard)" ]]; then
    echo "error: working tree has untracked files; release must be packed from a clean tree" >&2
    exit 1
  fi
fi

scripts/test.sh

rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR"

dotnet pack src/Vice.Parser/Vice.Parser.csproj -c Release -o "$OUT_DIR" --nologo -p:RestoreLockedMode=true
dotnet pack src/Vice/Vice.csproj -c Release -o "$OUT_DIR" --nologo -p:RestoreLockedMode=true
dotnet pack src/Vice.Cli/Vice.Cli.csproj -c Release -o "$OUT_DIR" --nologo -p:RestoreLockedMode=true
dotnet pack src/Vice.Mux.Cli/Vice.Mux.Cli.csproj -c Release -o "$OUT_DIR" --nologo -p:RestoreLockedMode=true

echo "Packed version $VERSION:"
ls -1 "$OUT_DIR"

if [[ "$PUSH" -eq 1 ]]; then
  if [[ -z "${NUGET_API_KEY:-}" ]]; then
    echo "error: --push requires NUGET_API_KEY in the environment" >&2
    exit 1
  fi
  NUGET_CONFIG_DIR="$(mktemp -d)"
  trap 'rm -rf "$NUGET_CONFIG_DIR"' EXIT
  NUGET_CONFIG="$NUGET_CONFIG_DIR/nuget.config"
  printf '%s\n' '<?xml version="1.0" encoding="utf-8"?>' '<configuration />' > "$NUGET_CONFIG"
  dotnet nuget setapikey "$NUGET_API_KEY" --source "$FEED" --configfile "$NUGET_CONFIG" >/dev/null
  for pkg in "$OUT_DIR"/*.nupkg; do
    dotnet nuget push "$pkg" --source "$FEED" --configfile "$NUGET_CONFIG" --skip-duplicate
  done
  echo "Pushed $VERSION to $FEED"
else
  echo "Dry run (no --push). To publish: NUGET_API_KEY=... scripts/release.sh --push"
fi
