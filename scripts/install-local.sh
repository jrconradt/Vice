#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
PKG_DIR="$(pwd)/artifacts/local-nupkg"
mkdir -p "$PKG_DIR"
dotnet pack src/Vice.Net/Vice.Net.csproj -c Release -o "$PKG_DIR" --nologo
dotnet tool uninstall --global Vice.Net 2>/dev/null || true
dotnet tool install --global Vice.Net --add-source "$PKG_DIR"
echo "Installed. Try: vice --help"
