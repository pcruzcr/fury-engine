#!/usr/bin/env bash
# Headless smoke test: builds the C# project with Godot and loads every scene for a few frames.
# Fails on any script error, unhandled exception or class that Godot cannot instantiate.
# Usage: GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 tools/godot-smoke.sh
set -uo pipefail
GODOT="${GODOT:-godot}"
cd "$(dirname "$0")/.."

echo "== Building C# solution with Godot =="
"$GODOT" --headless --path . --build-solutions --quit 2>&1 | grep -v "update_scripts_classes" | grep -v '^\[ *[0-9]*% \]' | tail -15
test -f .godot/mono/temp/bin/Debug/Fury.Game.dll || { echo "FAIL: Fury.Game.dll was not produced"; exit 1; }

fail=0
run_scene() {
  local scene="$1"; shift
  echo "== $scene $* =="
  local out
  out=$("$GODOT" --headless --path . "$scene" --quit-after 300 "$@" 2>&1) || true
  if echo "$out" | grep -E "^ERROR:|SCRIPT ERROR|Unhandled exception|Cannot instantiate|Failed to load|Parse Error" ; then
    echo "FAIL: $scene"; fail=1
  fi
}

run_scene res://scenes/boot/boot.tscn
run_scene res://scenes/menu/main_menu.tscn
run_scene res://scenes/menu/options.tscn
run_scene res://scenes/menu/controls.tscn
run_scene res://scenes/menu/online.tscn
run_scene res://scenes/menu/replays.tscn
run_scene res://scenes/select/character_select.tscn
run_scene res://scenes/tools/frame_data_editor.tscn
run_scene res://scenes/tools/stage_editor.tscn
run_scene res://scenes/game/fight.tscn -- --fury-demo
run_scene res://scenes/game/fight.tscn -- --fury-demo --fury-view=1
run_scene res://scenes/game/fight.tscn -- --fury-demo --fury-view=2
run_scene res://scenes/game/fight.tscn -- --fury-demo --fury-training --fury-dummy=block
# Art pipeline: the template pack (sprite strips) in the 2D and 2.5D views, and the content validator.
run_scene res://scenes/game/fight.tscn -- --fury-demo --fury-templates --fury-character=template --fury-opponent=kira
run_scene res://scenes/game/fight.tscn -- --fury-demo --fury-templates --fury-character=template --fury-opponent=kira --fury-view=1
echo "== content validator =="
check=$("$GODOT" --headless --path . res://scenes/tools/content_check.tscn -- --fury-templates 2>&1) || true
echo "$check" | tail -5
if ! echo "$check" | grep -q " 0 error(s)"; then echo "FAIL: content validator reported errors"; fail=1; fi

if [ "$fail" -ne 0 ]; then echo "SMOKE TEST FAILED"; exit 1; fi
echo "SMOKE TEST PASSED"
