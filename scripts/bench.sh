#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [ "$#" -eq 0 ]; then
  set -- --filter '*' --join
fi
dotnet run -c Release --project bench/Vice.Benchmarks/Vice.Benchmarks.csproj -- "$@"
