#!/usr/bin/env sh
set -eu

os_name="$(uname -s 2>/dev/null || printf unknown)"
cpu_name="$(uname -m 2>/dev/null || printf unknown)"

if [ "$os_name" != "Linux" ]; then
  printf '%s\n' "KM Suite Linux Server CLI requires Linux (detected: $os_name)." >&2
  exit 1
fi

case "$cpu_name" in
  x86_64|amd64) ;;
  *)
    printf '%s\n' "This package requires Linux x86-64 (detected: $cpu_name)." >&2
    exit 1
    ;;
esac

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
if [ ! -x "$script_dir/aph-havoc" ]; then
  chmod +x "$script_dir/aph-havoc"
fi

exec "$script_dir/aph-havoc" "$@"
