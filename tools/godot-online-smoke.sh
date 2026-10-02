#!/usr/bin/env bash
# End-to-end netplay check inside Godot: two real headless game instances (host + guest) play a CPU-driven
# match over UDP on loopback. Both must finish without desync and report the same confirmed-state hash.
# Usage: GODOT=/path/to/godot tools/godot-online-smoke.sh [port]
set -uo pipefail
GODOT="${GODOT:-godot}"
PORT="${1:-7791}"
cd "$(dirname "$0")/.."
tmp="$(mktemp -d)"

"$GODOT" --headless --path . res://scenes/game/fight.tscn -- --fury-online=host --fury-character=glen --fury-seed=11 --fury-port="$PORT" --fury-quit=700 > "$tmp/host.log" 2>&1 &
host=$!
"$GODOT" --headless --path . res://scenes/game/fight.tscn -- --fury-online=guest --fury-character=kira --fury-seed=22 --fury-port="$PORT" --fury-quit=700 > "$tmp/guest.log" 2>&1 &
guest=$!
wait $host $guest

h=$(grep -o "online-result.*" "$tmp/host.log" | tail -1)
g=$(grep -o "online-result.*" "$tmp/guest.log" | tail -1)
echo "host : $h"
echo "guest: $g"

hh=$(echo "$h" | grep -o "check120=[0-9a-f]*")
gh=$(echo "$g" | grep -o "check120=[0-9a-f]*")
if [ -z "$hh" ] || [ "$hh" = "check120=0000000000000000" ] || [ "$hh" != "$gh" ]; then echo "ONLINE SMOKE FAILED: hashes missing or different"; cat "$tmp/host.log" "$tmp/guest.log" | tail -30; exit 1; fi
if echo "$h $g" | grep -q "Desynced"; then echo "ONLINE SMOKE FAILED: desync"; exit 1; fi
if grep -qE "SCRIPT ERROR|Unhandled exception" "$tmp/host.log" "$tmp/guest.log"; then echo "ONLINE SMOKE FAILED: script errors"; exit 1; fi
echo "ONLINE SMOKE PASSED"
