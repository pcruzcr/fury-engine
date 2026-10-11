#!/usr/bin/env bash
# End-to-end netplay check inside Godot: two real headless game instances (host + guest) play a CPU-driven
# multi-round match over UDP on loopback. Both must finish without desync, agree on the confirmed-state hash of
# the first round and report the same round count and score.
# Usage: GODOT=/path/to/godot tools/godot-online-smoke.sh [port]
#        RELAY=1 GODOT=... tools/godot-online-smoke.sh [port]   (peers meet through a local relay server instead)
set -uo pipefail
GODOT="${GODOT:-godot}"
PORT="${1:-7791}"
cd "$(dirname "$0")/.."
tmp="$(mktemp -d)"
common="--fury-port=$PORT --fury-round-seconds=3 --fury-quit=1800 ${EXTRA_ARGS:-}"
relay_pid=""
if [ -n "${RELAY:-}" ]; then
  dotnet build tools/relay/Fury.Relay.csproj -c Release --nologo -v q > "$tmp/relay-build.log" 2>&1 || { echo "FAIL: relay build"; cat "$tmp/relay-build.log"; exit 1; }
  dotnet tools/relay/bin/Release/net9.0/Fury.Relay.dll "$PORT" 127.0.0.1 > "$tmp/relay.log" 2>&1 &
  relay_pid=$!
  trap 'kill "$relay_pid" 2>/dev/null' EXIT
  sleep 2
  common="--fury-relay=127.0.0.1:$PORT --fury-room=SMOKE-ROOM-1 --fury-round-seconds=3 --fury-quit=1800 ${EXTRA_ARGS:-}"
fi

"$GODOT" --headless --path . res://scenes/game/fight.tscn -- --fury-online=host --fury-character=glen --fury-seed=11 $common > "$tmp/host.log" 2>&1 &
host=$!
"$GODOT" --headless --path . res://scenes/game/fight.tscn -- --fury-online=guest --fury-character=kira --fury-seed=22 $common > "$tmp/guest.log" 2>&1 &
guest=$!
wait $host $guest

h=$(grep -o "online-result.*" "$tmp/host.log" | tail -1)
g=$(grep -o "online-result.*" "$tmp/guest.log" | tail -1)
echo "host : $h"
echo "guest: $g"

hh=$(echo "$h" | grep -o "check120=[0-9a-f]*")
gh=$(echo "$g" | grep -o "check120=[0-9a-f]*")
if [ -z "$hh" ] || [ "$hh" = "check120=0000000000000000" ] || [ "$hh" != "$gh" ]; then echo "ONLINE SMOKE FAILED: hashes missing or different"; tail -30 "$tmp/host.log" "$tmp/guest.log"; exit 1; fi
hr=$(echo "$h" | grep -o "round=[0-9]* wins=[0-9]*-[0-9]*")
gr=$(echo "$g" | grep -o "round=[0-9]* wins=[0-9]*-[0-9]*")
if [ -z "$hr" ] || [ "$hr" != "$gr" ]; then echo "ONLINE SMOKE FAILED: peers disagree on rounds/score ($hr vs $gr)"; exit 1; fi
if [ "${hr#round=1 }" != "$hr" ] && ! echo "$hr" | grep -q "wins=[12]-"; then echo "ONLINE SMOKE FAILED: no round was ever completed ($hr)"; exit 1; fi
if echo "$h $g" | grep -q "Desynced"; then echo "ONLINE SMOKE FAILED: desync"; exit 1; fi
if grep -qE "SCRIPT ERROR|Unhandled exception" "$tmp/host.log" "$tmp/guest.log"; then echo "ONLINE SMOKE FAILED: script errors"; exit 1; fi
echo "ONLINE SMOKE PASSED ($hr)"
