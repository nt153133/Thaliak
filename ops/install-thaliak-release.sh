#!/usr/bin/env bash
set -euo pipefail

archive="${1:-/tmp/thaliak-linux-x64.tgz}"
if [[ ! -f "$archive" ]]; then
    echo "release archive not found: $archive" >&2
    exit 1
fi

release="$(date -u +%Y%m%d%H%M%S)"
release_dir="/opt/thaliak/releases/$release"

install -d -m 750 -o thaliak -g thaliak /srv/thaliak/installs
if [[ -d /srv/thaliak/boot && ! -e /srv/thaliak/installs/global ]]; then
    mv /srv/thaliak/boot /srv/thaliak/installs/global
fi
install -d -m 750 -o thaliak -g thaliak \
    /srv/thaliak/installs/global \
    /srv/thaliak/installs/china \
    /srv/thaliak/installs/tc
chown -R thaliak:thaliak /srv/thaliak/installs

mkdir -p "$release_dir"
tar -xzf "$archive" -C "$release_dir"
rm -f "$archive"

chown -R root:root "$release_dir"
chmod -R u=rwX,go=rX "$release_dir"
chmod 755 "$release_dir/Thaliak.Service.Poller"

rm -rf /opt/thaliak/current
ln -s "$release_dir" /opt/thaliak/current

install -d -m 750 -o root -g thaliak /etc/thaliak
cat >/etc/thaliak/thaliak.env <<'EOF'
DOTNET_ENVIRONMENT=Production
ConnectionStrings__sqlite=Data Source=/srv/thaliak/db/thaliak.db
Directories__Boot=/srv/thaliak/installs/global
Directories__Patches=/srv/thaliak/patches
Installations__Enabled=false
Installations__Root=/srv/thaliak/installs
Installations__Regions__0=Global
Installations__Regions__1=China
Installations__Regions__2=TC
Polling__DisableKoreaChecks=true
Polling__DailyCheckTimePacific=09:00
Polling__MaintenanceActivePollMinutes=2
Polling__TraditionalChineseMaintenancePollMinutes=30
Notifications__QuietWindowMinutes=3
Notifications__NotifyScrapedPatches=false
Notifications__SuppressBootPatchAlerts=true
ENABLE_DOWNLOADS=true
TMPDIR=/srv/thaliak/tmp
EOF
chown root:thaliak /etc/thaliak/thaliak.env
chmod 640 /etc/thaliak/thaliak.env

cat >/etc/systemd/system/thaliak.service <<'EOF'
[Unit]
Description=Thaliak V1 poller
Wants=network-online.target
After=network-online.target

[Service]
Type=simple
User=thaliak
Group=thaliak
WorkingDirectory=/opt/thaliak/current
EnvironmentFile=/etc/thaliak/thaliak.env
ExecStart=/opt/thaliak/current/Thaliak.Service.Poller
Restart=on-failure
RestartSec=10
TimeoutStopSec=30
KillSignal=SIGINT
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ReadWritePaths=/srv/thaliak
UMask=0077
LimitNOFILE=65535

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
echo "Installed Thaliak release $release at $release_dir"
