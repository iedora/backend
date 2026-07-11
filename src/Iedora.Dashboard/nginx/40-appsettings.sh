#!/bin/sh
# Runs in nginx:alpine's /docker-entrypoint.d before nginx starts. Writes the WASM
# app's runtime config (Api:BaseUrl) from $API_BASE_URL, so one image serves any
# environment. The Blazor app fetches appsettings.json at boot.
set -e
: "${API_BASE_URL:?API_BASE_URL is required (e.g. https://dotnet-api.iedora.com)}"
cat > /usr/share/nginx/html/appsettings.json <<EOF
{ "Api": { "BaseUrl": "${API_BASE_URL}" } }
EOF
echo "appsettings.json -> Api:BaseUrl=${API_BASE_URL}"
