#!/usr/bin/env bash
# Builds the tester package: the exported Windows game + a self-contained relay server (Windows and Linux) + the tester guide, zipped.
# Usage: GODOT=/path/to/Godot_console.exe tools/package-alpha.sh [version-label]
# Output: build/DualFury-alpha-<label>-windows.zip
set -euo pipefail
GODOT="${GODOT:-godot}"
LABEL="${1:-$(git rev-parse --short HEAD 2>/dev/null || echo dev)}"
cd "$(dirname "$0")/.."

# Stray MSBuild worker nodes from earlier builds can keep Godot's own build from exiting: do not leave any behind.
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0

out="build/alpha"
rm -rf "$out" && mkdir -p "$out/DualFury-alpha/relay-windows" "$out/DualFury-alpha/relay-linux"

echo "== certification (the build must not ship if the simulation drifted)"
dotnet run --project cert -c Release --nologo 2>&1 | tail -2

echo "== exporting the Windows game"
"$GODOT" --headless --path . --build-solutions --quit > /dev/null 2>&1
"$GODOT" --headless --path . --export-release "Windows Desktop" "$out/DualFury-alpha/DualFury.exe" > "$out/export.log" 2>&1
test -f "$out/DualFury-alpha/DualFury.exe" -a -f "$out/DualFury-alpha/DualFury.pck" || { echo "EXPORT FAILED"; tail -20 "$out/export.log"; exit 1; }

echo "== publishing the relay server (self-contained)"
dotnet publish tools/relay/Fury.Relay.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None --nologo -v q -o "$out/DualFury-alpha/relay-windows" > /dev/null
dotnet publish tools/relay/Fury.Relay.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None --nologo -v q -o "$out/DualFury-alpha/relay-linux" > /dev/null

sed "s/@VERSION@/$LABEL/g" docs/ALPHA_TESTER_GUIDE.txt > "$out/DualFury-alpha/LEEME-ALPHA.txt"

zip_name="DualFury-alpha-$LABEL-windows.zip"
echo "== zipping $zip_name"
if command -v zip > /dev/null 2>&1; then
  (cd "$out" && zip -qr "../$zip_name" DualFury-alpha)
else
  powershell -NoProfile -Command "Compress-Archive -Path '$(cygpath -w "$out/DualFury-alpha")' -DestinationPath '$(cygpath -w "build/$zip_name")' -Force"
fi
ls -la "build/$zip_name"
echo "ALPHA PACKAGE READY: build/$zip_name"
