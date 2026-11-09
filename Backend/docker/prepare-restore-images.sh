#!/bin/sh
set -eu

# Run on the same daemon and with the same restore configuration as the API.
# Inspect before pulling: pinned/cached images also work without registry access.
docker info >/dev/null
settings="${1:-/app/appsettings.json}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT HUP INT TERM
runtime="${Configuration__ReloadFile:-/config/appsettings.Runtime.json}"
if [ -f "$runtime" ]; then
    jq -s '.[0] * .[1]' "$settings" "$runtime" > "$work/settings.json"
else
    cp "$settings" "$work/settings.json"
fi
jq -er '
    .RestoreTests
    | [paths(type == "string") as $path
       | select($path[0] | test("^(PostgresImage|MySqlImage|MariaDbImage|SqlServerImage|PostgresImages|MySqlImages|MariaDbImages)$"))
       | env["RestoreTests__" + ($path | join("__"))] // getpath($path)]
    | unique | .[] | select(length > 0)
' "$work/settings.json" > "$work/images.txt"
while IFS= read -r image; do
    if ! docker image inspect "$image" >/dev/null 2>&1; then
        docker image pull "$image"
    fi
done < "$work/images.txt"
echo "Restore images are available on the configured Docker daemon."
