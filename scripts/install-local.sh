#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
PKG_DIR="$(pwd)/artifacts/local-nupkg"
mkdir -p "$PKG_DIR"
dotnet pack src/Vice.Cli/Vice.Cli.csproj -c Release -o "$PKG_DIR" --nologo
dotnet pack src/Vice.Mux.Cli/Vice.Mux.Cli.csproj -c Release -o "$PKG_DIR" --nologo
dotnet tool uninstall --global Vice.Cli 2>/dev/null || true
dotnet tool uninstall --global Vice.Mux.Cli 2>/dev/null || true
dotnet tool install --global Vice.Cli --configfile "$(pwd)/nuget.config"
dotnet tool install --global Vice.Mux.Cli --configfile "$(pwd)/nuget.config"
echo "Installed Vice.Cli (vice) and Vice.Mux.Cli (vice-mux). Try: vice --help"
