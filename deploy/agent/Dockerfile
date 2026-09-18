# syntax=docker/dockerfile:1

# The Argus agent as a container, for machines that cannot run it as a service: NAS boxes and other
# appliances without systemd. It watches the machine through its filesystem, mounted read-only at /host.
#
#   docker build -f deploy/agent/Dockerfile -t argus-agent .
#
# See docs/configuration.md, "The agent in a container", for what each part of the run command is for.

ARG DOTNET_VERSION=10.0

# ---- Agent ---------------------------------------------------------------------------------------
# Built on the build machine's own platform and published for the target's, so no emulator is needed.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-noble AS build
ARG TARGETARCH
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Argus.Contracts/Argus.Contracts.csproj src/Argus.Contracts/
COPY src/Argus.Agent/Argus.Agent.csproj src/Argus.Agent/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/Argus.Agent/Argus.Agent.csproj --runtime "linux-$( [ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64 )"

COPY src/ src/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/Argus.Agent/Argus.Agent.csproj --configuration Release --no-restore \
      --runtime "linux-$( [ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64 )" --output /out

# ---- Runtime -------------------------------------------------------------------------------------
# Self-contained, so only the base system libraries are needed. It runs as root: reading the machine's
# /proc and /sys through /host, and Docker's socket, are root's to do.
FROM mcr.microsoft.com/dotnet/runtime-deps:${DOTNET_VERSION}-noble-chiseled AS runtime
COPY --from=build /out/argus-agent /usr/local/bin/argus-agent

# The chiseled image runs as an unprivileged user, which cannot read the machine's files or use
# Docker's socket. Run the container as another user to give it less than this, and it reads less.
USER root

# Where the machine's own filesystem is mounted, and where the agent keeps its identity between restarts.
ENV ARGUS_HOSTROOT=/host \
    ARGUS_STATEDIRECTORY=/var/lib/argus-agent

ENTRYPOINT ["/usr/local/bin/argus-agent"]
CMD ["run"]
