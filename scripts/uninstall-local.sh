#!/usr/bin/env bash
set -euo pipefail
dotnet tool uninstall --global Vice.Cli 2>/dev/null || true
dotnet tool uninstall --global Vice.Mux.Cli 2>/dev/null || true
rm -rf "$(dirname "$0")/../artifacts/local-nupkg"
echo "Uninstalled."
