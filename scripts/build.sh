#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build Vice.slnx --nologo
dotnet format Vice.slnx --verify-no-changes --no-restore
