#!/usr/bin/env bash
set -euo pipefail

seconds="${1:-70}"
since="$(date +%s)"

systemctl start thaliak
sleep "$seconds"

systemctl status thaliak --no-pager
echo "--- fresh-log-tail ---"
journalctl -u thaliak --since="@$since" --no-pager | tail -n 220
echo "--- error-scan ---"
journalctl -u thaliak --since="@$since" --no-pager |
    grep -E "ERR|WRN|Exception|UNIQUE|SSL|NoValidAccount" || true
echo "--- memory ---"
systemctl show thaliak -p ActiveState -p SubState -p MainPID -p MemoryCurrent -p MemoryPeak
echo "--- db ---"
ls -lh /srv/thaliak/db
