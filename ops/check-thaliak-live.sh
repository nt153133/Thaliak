#!/usr/bin/env bash
set -euo pipefail

window_seconds="${1:-300}"
since_epoch="$(($(date +%s) - window_seconds))"

echo "--- status ---"
systemctl status thaliak --no-pager
echo "--- recent focused logs ---"
journalctl -u thaliak --since="@$since_epoch" --no-pager |
    grep -E "Sqex|Boot|boot|patch process|Waiting|download|Downloaded|error|ERR|WRN|Exception|Queued|Sending batched|Discord|Global|game patches|complete|next execution" |
    tail -n 220 || true
echo "--- error-scan ---"
journalctl -u thaliak --since="@$since_epoch" --no-pager |
    grep -E "ERR|Exception|fail|failed|SSL|UNIQUE|NoValidAccount" || true
echo "--- files ---"
find /srv/thaliak/installs -maxdepth 5 -type f -printf "%p %s bytes\n" | head -n 20
find /srv/thaliak/patches -maxdepth 4 -type f -printf "%p %s bytes\n" | head -n 20
echo "--- memory ---"
systemctl show thaliak -p ActiveState -p SubState -p MemoryCurrent -p MemoryPeak
echo "--- queue-ish logs ---"
journalctl -u thaliak --since="@$since_epoch" --no-pager |
    grep -Ei "download|waiting|boot needs|patch process|complete|error|exception|queue|queued|Downloader" || true
echo "--- all files ---"
find /srv/thaliak -type f -printf "%p %s bytes\n" | sort | head -n 80
