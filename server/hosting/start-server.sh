#!/usr/bin/env bash
set -euo pipefail
umask 077
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
export KUPOPOP_ROOT="$PWD"
chmod +x ./Kupopop.Server
if [[ "${1:-}" == "--local" ]]; then
  echo 'Local development TLS: trust data/localhost.cer on the testing machine using its normal certificate store.'
  exec ./Kupopop.Server --local
fi
hostname=''
if [[ -f hostname.txt ]]; then hostname="$(cat hostname.txt)"; fi
if [[ -z "$hostname" ]]; then
  echo 'Kupopop Draw server - Linux x64'
  echo 'Point a public DNS hostname here. Allow inbound TCP 80 and 443.'
  read -r -p 'Public DNS hostname: ' hostname
fi
if [[ ! "$hostname" =~ ^[a-zA-Z0-9][a-zA-Z0-9.-]*[a-zA-Z0-9]$ ]] || [[ "$hostname" == localhost ]] || [[ "$hostname" =~ ^[0-9.]+$ ]]; then
  echo 'Enter a public DNS hostname without a scheme, port, path, or IP address.' >&2; exit 1
fi
printf '%s' "$hostname" > hostname.txt
version='2.11.7'
base="https://github.com/caddyserver/caddy/releases/download/v${version}"
if [[ ! -f ./caddy ]]; then
  command -v curl >/dev/null
  command -v sha256sum >/dev/null
  command -v tar >/dev/null
  curl --fail --location --proto '=https' --tlsv1.2 "$base/caddy_${version}_checksums.txt" -o caddy-checksums.txt
  echo '5d27b76b95f496638d79208c55250317b8755fd9e90005f7466aa32956944a90  caddy-checksums.txt' | sha256sum --check --status
  archive="caddy_${version}_linux_amd64.tar.gz"
  curl --fail --location --proto '=https' --tlsv1.2 "$base/$archive" -o "$archive"
  grep " ${archive}\$" caddy-checksums.txt | sha256sum --check --status
  tar -xzf "$archive" caddy
fi
chmod +x ./caddy
printf '%s {\n    reverse_proxy 127.0.0.1:5080\n}\n' "$hostname" > Caddyfile
mkdir -p logs
echo "Plugin server URL: https://$hostname"
echo 'Caddy manages certificates. If binding ports is denied, grant the Caddy binary permission to bind 80/443 or run under your host service manager.'
./caddy run --config Caddyfile --adapter caddyfile >logs/caddy-out.log 2>logs/caddy-error.log &
proxy_pid=$!
cleanup() { kill "$proxy_pid" 2>/dev/null || true; }
trap cleanup EXIT INT TERM
sleep 1
kill -0 "$proxy_pid" || { echo 'Caddy stopped. Check logs/caddy-error.log.' >&2; exit 1; }
./Kupopop.Server --proxy
