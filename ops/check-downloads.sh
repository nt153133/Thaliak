#!/usr/bin/env bash
set -euo pipefail

window_seconds="${1:-600}"
since_epoch="$(($(date +%s) - window_seconds))"

echo "--- download logs ---"
journalctl -u thaliak --since="@$since_epoch" --no-pager |
    grep -E "Adding to download queue|Starting download|Download complete|Download failed|Queued|SqexPollerService: poll complete|TraditionalChinesePollerService: poll complete|ShandaPollerService: poll complete|ERR|Exception" |
    tail -n 240 || true
echo "--- status ---"
systemctl show thaliak -p ActiveState -p SubState -p MainPID -p MemoryCurrent -p MemoryPeak
echo "--- disk ---"
df -h /srv/thaliak
du -sh /srv/thaliak/patches /srv/thaliak/boot 2>/dev/null || true
echo "--- recent patch files ---"
find /srv/thaliak/patches -type f -printf "%T@ %s %p\n" | sort -nr | head -n 20
