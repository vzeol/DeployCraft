#!/bin/bash
# Startup of Claude Code sessions in the cloud (Linux): installs the .NET 10 SDK and restores
# the dependencies, to build, test and check formatting before pushing.
# On a Windows computer, this script does nothing.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

DOTNET_DIR="$HOME/.dotnet"
export PATH="$DOTNET_DIR:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
# Build the net10.0-windows projects on Linux (service, console, tests).
export EnableWindowsTargeting=true

if ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  installer="$(mktemp)"
  curl -sSL https://dot.net/v1/dotnet-install.sh -o "$installer"
  bash "$installer" --channel 10.0 --install-dir "$DOTNET_DIR" >/dev/null
  rm -f "$installer"
fi

if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export PATH=\"$DOTNET_DIR:\$PATH\""
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1'
    echo 'export EnableWindowsTargeting=true'
  } >> "$CLAUDE_ENV_FILE"
fi

dotnet restore "$CLAUDE_PROJECT_DIR/DeployCraft.slnx" --locked-mode >/dev/null
