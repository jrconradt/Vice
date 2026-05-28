#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet test Vice.slnx --nologo --logger "console;verbosity=minimal"
