#!/bin/bash
# Снимок экрана игры без монитора (Xvfb + программный Vulkan).
# tools/shot.sh <out.png> [параметры игры: --screen=hub --tab=lines --days=25 ...]
# tools/shot.sh <out.png> --arena --cam=broadcast|faceoff|corner   — 3Д-арена
out=$1; shift
cd "$(dirname "$0")/../game" || exit 1
dotnet build -v q 2>&1 | grep -E " error " | sort -u | head -10
scene=""
if [ "$1" == "--arena" ]; then scene="res://Match3D/RinkPreview.tscn"; shift; fi
timeout 400 xvfb-run -a -s "-screen 0 ${RES:-1600x900}x24" godot --rendering-driver vulkan --resolution ${RES:-1600x900} $scene -- --shot="$out" "$@" 2>&1 \
  | grep -E "Кадр|ERROR|SCRIPT|Exception|at HM27" | grep -v -E "ALSA|audio|status < 0" | head -12
