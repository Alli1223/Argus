#!/usr/bin/env bash
# Builds the agent for every supported platform and packages it for manual installs.
#
#   build/package-agent.sh [output-dir]        (default: artifacts/agent)
#
# Produces <output>/<runtime>/argus-agent[.exe] (what the server offers under /downloads/agent), one
# archive per platform with the binary and its install scripts, the bare binaries under names that
# tell the platforms apart (servers fetch agent updates from these), and SHA256SUMS for all of them.
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${1:-$ROOT/artifacts/agent}"
VERSION="$(sed -n 's:.*<VersionPrefix>\(.*\)</VersionPrefix>.*:\1:p' "$ROOT/Directory.Build.props")"
RUNTIMES=(linux-x64 linux-arm64 win-x64)

rm -rf "$OUT"
mkdir -p "$OUT"

for runtime in "${RUNTIMES[@]}"; do
  echo "==> Publishing argus-agent $VERSION for $runtime"
  dotnet publish "$ROOT/src/Argus.Agent/Argus.Agent.csproj" \
    --configuration Release --runtime "$runtime" --output "$OUT/$runtime" --nologo --verbosity quiet \
    -p:DebugType=none
done

LINUX_SCRIPTS="$ROOT/deploy/agent/linux"
for runtime in linux-x64 linux-arm64; do
  tar -czf "$OUT/argus-agent-$VERSION-$runtime.tar.gz" \
    -C "$OUT/$runtime" argus-agent \
    -C "$LINUX_SCRIPTS" install.sh uninstall.sh argus-agent.service argus-agent-update.service argus-agent-update.path
done

python3 - "$OUT" "$VERSION" "$ROOT/deploy/agent/windows" <<'PY'
import sys, zipfile, pathlib
out, version, scripts = pathlib.Path(sys.argv[1]), sys.argv[2], pathlib.Path(sys.argv[3])
with zipfile.ZipFile(out / f"argus-agent-{version}-win-x64.zip", "w", zipfile.ZIP_DEFLATED) as archive:
    archive.write(out / "win-x64" / "argus-agent.exe", "argus-agent.exe")
    for script in ("install.ps1", "uninstall.ps1"):
        archive.write(scripts / script, script)
PY

cp "$OUT/linux-x64/argus-agent" "$OUT/argus-agent-linux-x64"
cp "$OUT/linux-arm64/argus-agent" "$OUT/argus-agent-linux-arm64"
cp "$OUT/win-x64/argus-agent.exe" "$OUT/argus-agent-win-x64.exe"
(cd "$OUT" && sha256sum argus-agent-* > SHA256SUMS)

echo "==> Done:"
ls -lh "$OUT"/argus-agent-* "$OUT/SHA256SUMS"
