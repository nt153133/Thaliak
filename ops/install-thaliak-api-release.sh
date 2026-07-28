#!/usr/bin/env bash
set -euo pipefail

archive="${1:-/tmp/thaliak-api-linux-x64.tgz}"
if [[ ! -f "$archive" ]]; then
    echo "release archive not found: $archive" >&2
    exit 1
fi

release="$(date -u +%Y%m%d%H%M%S)"
release_dir="/opt/thaliak/api-releases/$release"

install -d -m 750 -o thaliak -g thaliak /srv/thaliak/artifacts
install -d -m 750 -o root -g thaliak /etc/thaliak

mkdir -p "$release_dir"
tar -xzf "$archive" -C "$release_dir"
rm -f "$archive"

chown -R root:root "$release_dir"
chmod -R u=rwX,go=rX "$release_dir"
chmod 755 "$release_dir/Thaliak.Service.Api"
ln -sfnT "$release_dir" /opt/thaliak/api-current

env_file=/etc/thaliak/thaliak.env
if [[ ! -f "$env_file" ]]; then
    echo "missing shared Thaliak environment file: $env_file" >&2
    exit 1
fi

set_env_default() {
    local key="$1"
    local value="$2"
    if ! grep -q "^${key}=" "$env_file"; then
        printf '%s=%s\n' "$key" "$value" >>"$env_file"
    fi
}

set_env_default Artifacts__Enabled false
set_env_default Artifacts__Root /srv/thaliak/artifacts
set_env_default Artifacts__PatchRoot /srv/thaliak/patches
set_env_default Artifacts__PollIntervalSeconds 300
set_env_default Artifacts__Compression Brotli
set_env_default Artifacts__PublicBaseUrl https://api.llamashepherd.com

chown root:thaliak "$env_file"
chmod 640 "$env_file"

cat >/etc/systemd/system/thaliak-api.service <<'EOF'
[Unit]
Description=Thaliak V1 compatibility API
Wants=network-online.target
After=network-online.target thaliak.service

[Service]
Type=simple
User=thaliak
Group=thaliak
WorkingDirectory=/opt/thaliak/api-current
EnvironmentFile=/etc/thaliak/thaliak.env
Environment=ASPNETCORE_URLS=http://127.0.0.1:5080
ExecStart=/opt/thaliak/api-current/Thaliak.Service.Api
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
systemctl enable thaliak-api.service
echo "Installed Thaliak API release $release at $release_dir"
