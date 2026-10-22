#!/bin/sh
set -eu
mkdir -p /tmp/tls
if [ -r /certs/server.crt ] && [ -r /certs/server.key ]; then
    cp /certs/server.crt /tmp/tls/server.crt
    cp /certs/server.key /tmp/tls/server.key
else
    echo 'Using a temporary self-signed localhost certificate. Mount /certs/server.crt and /certs/server.key for a trusted certificate.'
    openssl req -x509 -nodes -newkey rsa:2048 -days 30 \
        -subj '/CN=localhost' -addext 'subjectAltName=DNS:localhost,IP:127.0.0.1' \
        -keyout /tmp/tls/server.key -out /tmp/tls/server.crt 2>/dev/null
fi
chmod 600 /tmp/tls/server.key
