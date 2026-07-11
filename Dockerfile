# syntax=docker/dockerfile:1

# iedora backend — ONE image, three entrypoints (Iedora.Api / Iedora.Worker /
# Iedora.MigrationService). Kamal selects the role with `cmd:` (default = the API);
# the migrator runs one-off via scripts/migrate.sh. Every module DLL ships in the
# one image — the process that runs decides whether it's the public API, the
# background worker, or the one-off migrator. CI builds amd64 (the prod VM); the
# Dockerfile itself is arch-agnostic.

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the build config + the project trees the three entrypoints reference, mirroring
# the repo layout so ProjectReferences + Central Package Management resolve. (No AppHost,
# tests, or Dashboard — they aren't part of the backend image.)
COPY Iedora.slnx Directory.Build.props Directory.Packages.props global.json ./
COPY Iedora.ServiceDefaults/ Iedora.ServiceDefaults/
COPY framework/ framework/
COPY src/ src/

# Restore first so the layer caches until a csproj or the package manifest changes.
RUN dotnet restore src/Iedora.Api/Iedora.Api.csproj \
 && dotnet restore src/Iedora.Worker/Iedora.Worker.csproj \
 && dotnet restore src/Iedora.MigrationService/Iedora.MigrationService.csproj

# Publish the three entrypoints into one directory. Shared module/framework DLLs are
# identical across them (one solution, single package versions), so the overlap is a
# deterministic overwrite; each keeps its own <Proj>.dll + .deps.json + .runtimeconfig.json.
RUN dotnet publish src/Iedora.Api/Iedora.Api.csproj -c Release -o /app --no-restore \
 && dotnet publish src/Iedora.Worker/Iedora.Worker.csproj -c Release -o /app --no-restore \
 && dotnet publish src/Iedora.MigrationService/Iedora.MigrationService.csproj -c Release -o /app --no-restore

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# kamal-proxy speaks HTTP to the app on 8080; TLS terminates upstream at Cloudflare.
# FORWARDEDHEADERS_ENABLED so ASP.NET honours the proxy's X-Forwarded-* (the app also
# calls UseForwardedHeaders, gated to its configured trusted proxies).
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE 8080

# Default role = the public API. Uses CMD (not ENTRYPOINT) so Kamal's `cmd:` REPLACES
# it to select the worker (dotnet Iedora.Worker.dll) or the one-off migrator
# (dotnet Iedora.MigrationService.dll) — with ENTRYPOINT it would be appended as args instead.
CMD ["dotnet", "Iedora.Api.dll"]
