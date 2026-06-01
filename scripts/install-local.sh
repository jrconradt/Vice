#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
PKG_DIR="$(pwd)/artifacts/local-nupkg"
mkdir -p "$PKG_DIR"
dotnet pack src/Vice.Parser/Vice.Parser.csproj -c Release -o "$PKG_DIR" --nologo
dotnet pack src/Vice/Vice.csproj -c Release -o "$PKG_DIR" --nologo
dotnet pack src/Vice.Cli/Vice.Cli.csproj -c Release -o "$PKG_DIR" --nologo
dotnet pack src/Vice.Mux.Cli/Vice.Mux.Cli.csproj -c Release -o "$PKG_DIR" --nologo
install_tool() {
  local pkg="$1"
  if dotnet tool update --global "$pkg" --configfile "$(pwd)/nuget.config"; then
    return 0
  fi
  echo "error: '$pkg' update failed; the previously installed tool (if any) is left intact" >&2
  return 1
}

install_tool Vice.Cli
install_tool Vice.Mux.Cli
echo "Installed Vice.Cli (vice) and Vice.Mux.Cli (vice-mux). Try: vice --help"

if [[ "${VICE_INSTALL_SYSTEMD:-0}" == "1" ]]; then
  UNIT_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"
  mkdir -p "$UNIT_DIR"
  VICE_BIN="$(command -v vice || echo "$HOME/.dotnet/tools/vice")"
  sed "s#@VICE_BIN@#$VICE_BIN#g" packaging/systemd/vice-daemon.service > "$UNIT_DIR/vice-daemon.service"
  systemctl --user daemon-reload
  echo "Installed user systemd unit. Enable with: systemctl --user enable --now vice-daemon.service"
fi
