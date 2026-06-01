#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet restore Vice.slnx --locked-mode
dotnet test Vice.slnx --nologo --no-restore \
  --collect:"XPlat Code Coverage" \
  --results-directory artifacts/test-results \
  --logger "console;verbosity=minimal" \
  --logger "junit;LogFilePath=artifacts/test-results/{assembly}.junit.xml"
