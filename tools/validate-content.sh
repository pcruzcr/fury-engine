#!/usr/bin/env bash
# Validates every character pack and stage pack (manifests, image sizes, scenes, sounds) and exits 1 on any error.
# Warnings (a missing optional animation, an unused clip...) are printed but never fail the run.
# Usage: GODOT=/path/to/godot tools/validate-content.sh [--fury-templates]
#   --fury-templates  also check the '_template' packs shipped as starting points
set -uo pipefail
GODOT="${GODOT:-godot}"
cd "$(dirname "$0")/.."

"$GODOT" --headless --path . --build-solutions --quit > /dev/null 2>&1
out=$("$GODOT" --headless --path . res://scenes/tools/content_check.tscn -- "$@" 2>&1)
status=$?
echo "$out" | grep -E "^(ERROR|warning):|checked:" || true
if [ "$status" -ne 0 ] || ! echo "$out" | grep -q " 0 error(s)"; then echo "CONTENT CHECK FAILED"; exit 1; fi
echo "CONTENT CHECK PASSED"
