# syntax=docker/dockerfile:1

# The Argus server image: the API, the built web app and agent builds for every platform, running
# as a non-root user on a runtime image with no shell or package manager.
#
#   docker build -t argus .
#
# The build stages run on the build machine's own platform; the result runs on amd64 and arm64 alike.

ARG DOTNET_VERSION=10.0

# ---- Web app -------------------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM node:24-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# ---- Server and agents ---------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-noble AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

# Restore from the project files alone first, so this layer survives source changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Argus.Contracts/Argus.Contracts.csproj src/Argus.Contracts/
COPY src/Argus.Server/Argus.Server.csproj src/Argus.Server/
COPY src/Argus.Agent/Argus.Agent.csproj src/Argus.Agent/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/Argus.Server/Argus.Server.csproj

COPY src/ src/
COPY deploy/agent/ deploy/agent/

# Framework-dependent and without a native launcher, so the same output runs on any architecture.
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/Argus.Server/Argus.Server.csproj --configuration Release --no-restore \
      --output /out/server -p:UseAppHost=false

# The agents the server offers at /downloads/agent: self-contained single files, one per platform.
RUN --mount=type=cache,target=/root/.nuget/packages \
    for runtime in linux-x64 linux-arm64 win-x64; do \
      dotnet publish src/Argus.Agent/Argus.Agent.csproj --configuration Release --runtime "$runtime" \
        --output "/out/agents/$runtime" -p:DebugType=none || exit 1; \
    done

# ---- Runtime -------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /out/server ./
COPY --from=build /out/agents ./agents
COPY --from=web /src/web/dist ./wwwroot

ENV ASPNETCORE_HTTP_PORTS=8080 \
    Argus__Downloads__AgentDirectory=/app/agents
EXPOSE 8080
USER $APP_UID

# The server checks its own readiness (database included); the image has no curl to do it.
HEALTHCHECK --interval=30s --timeout=10s --start-period=90s --retries=3 \
  CMD ["dotnet", "/app/Argus.Server.dll", "health-check"]

ENTRYPOINT ["dotnet", "/app/Argus.Server.dll"]
