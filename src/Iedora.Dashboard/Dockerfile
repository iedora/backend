# syntax=docker/dockerfile:1

# iedora admin dashboard — Blazor WebAssembly (standalone) published to static files
# and served by nginx. A pure API consumer: no server, no secrets. The API base URL
# is injected at container start into appsettings.json (the WASM app fetches it at
# runtime), so one image points at any environment. Build context = the repo root
# (docker build -f src/Iedora.Dashboard/Dockerfile .).

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY openapi/ openapi/
COPY src/Iedora.Identity.Contracts/ src/Iedora.Identity.Contracts/
COPY src/Iedora.Dashboard/ src/Iedora.Dashboard/
RUN dotnet restore src/Iedora.Dashboard/Iedora.Dashboard.csproj
RUN dotnet publish src/Iedora.Dashboard/Iedora.Dashboard.csproj -c Release -o /publish --no-restore

# ---- runtime ----
FROM nginx:alpine AS runtime
# The published WASM app is served from wwwroot.
COPY --from=build /publish/wwwroot /usr/share/nginx/html
COPY src/Iedora.Dashboard/nginx/default.conf /etc/nginx/conf.d/default.conf
# Writes appsettings.json (Api:BaseUrl) from $API_BASE_URL before nginx starts.
COPY src/Iedora.Dashboard/nginx/40-appsettings.sh /docker-entrypoint.d/40-appsettings.sh
RUN chmod +x /docker-entrypoint.d/40-appsettings.sh
EXPOSE 80
