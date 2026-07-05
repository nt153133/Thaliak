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
echo "--- installation io ---"
main_pid="$(systemctl show thaliak -p MainPID --value)"
if [[ -r "/proc/$main_pid/io" ]]; then
    grep -E "^(read_bytes|write_bytes|cancelled_write_bytes):" "/proc/$main_pid/io"
fi
for region in global china tc; do
    region_path="/srv/thaliak/installs/$region"
    if [[ -d "$region_path" ]]; then
        allocated_bytes="$(du -sB1 "$region_path" | cut -f1)"
        apparent_bytes="$(du -sB1 --apparent-size "$region_path" | cut -f1)"
        printf "%s allocated_bytes=%s apparent_bytes=%s\n" \
            "$region" "$allocated_bytes" "$apparent_bytes"
    fi
done
echo "--- queue-ish logs ---"
journalctl -u thaliak --since="@$since_epoch" --no-pager |
    grep -Ei "download|waiting|boot needs|patch process|complete|error|exception|queue|queued|Downloader" || true
echo "--- all files ---"
find /srv/thaliak -type f -printf "%p %s bytes\n" | sort | head -n 80
