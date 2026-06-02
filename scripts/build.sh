#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet restore Vice.slnx
dotnet build Vice.slnx --nologo --no-restore
dotnet format Vice.slnx --verify-no-changes --no-restore
