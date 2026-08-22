#!/usr/bin/env bash
# Local Steam-build deploy helper.
#
# Everything machine-specific is an environment variable with the author's setup as the
# default, so this is overridable rather than only working on one machine:
#
#   VVC_TARGET_DIR   where to deploy         (default: the Steam common dir below)
#   VVC_CONTAINER    distrobox container     (default: arch; set empty to use the host dotnet)
#   VVC_TRIMMED      1 to publish trimmed    (default: 0, see note further down)
#
#   ./build.sh                 build and deploy
#   ./build.sh --restart       also restart the app through Steam
set -euo pipefail

RESTART=false
for arg in "$@"; do
    case "$arg" in
        --restart) RESTART=true ;;
        -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
        *) echo "Unknown argument: $arg" >&2; exit 2 ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TARGET_DIR="${VVC_TARGET_DIR:-/run/media/system/Data/Games/Steam/steamapps/common/VRCVideoCacher}"
CONTAINER_NAME="${VVC_CONTAINER-arch}"
TRIMMED="${VVC_TRIMMED:-0}"
STEAM_APP_ID=4296960
TMP_OUT="${SCRIPT_DIR}/output_steam_linux"

# Run dotnet inside the container when one is configured, otherwise straight on the host.
dotnet_run() {
    if [ -n "${CONTAINER_NAME}" ]; then
        distrobox enter "${CONTAINER_NAME}" -- dotnet "$@"
    else
        dotnet "$@"
    fi
}

echo "=== Building yt-dlp-stub ==="
dotnet_run publish "${SCRIPT_DIR}/yt-dlp-stub/yt-dlp-stub.csproj" -c Release -r win-x64
cp "${SCRIPT_DIR}/yt-dlp-stub/bin/Release/net10.0/win-x64/publish/yt-dlp-stub.exe" "${SCRIPT_DIR}/VRCVideoCacher/"

# Deploying loose files rather than a single trimmed binary keeps local iteration fast and
# makes stack traces readable. It does mean this path does not exercise trimming — CI's
# publish job does that, and VVC_TRIMMED=1 reproduces it here when you need to.
echo "=== Building VRCVideoCacher for Steam (Linux x64, trimmed=${TRIMMED}) ==="
rm -rf "${TMP_OUT}"
dotnet_run publish "${SCRIPT_DIR}/VRCVideoCacher/VRCVideoCacher.csproj" \
    -c SteamRelease \
    -r linux-x64 \
    -o "${TMP_OUT}" \
    --self-contained true \
    -p:PublishSingleFile=false \
    -p:PublishTrimmed="$([ "${TRIMMED}" = "1" ] && echo true || echo false)"

echo "=== Deploying to ${TARGET_DIR} ==="
mkdir -p "${TARGET_DIR}"
rsync -av --delete --exclude='CachedAssets' --exclude='logs' "${TMP_OUT}/" "${TARGET_DIR}/"

echo "=== Deployment Complete ==="

if [ "$RESTART" = true ]; then
    echo "=== (Re)starting VRCVideoCacher ==="
    # Match the deployed binary by full path. A bare `pkill -f VRCVideoCacher` also matches
    # this script, an editor with the project open, or a shell sitting in the source tree.
    pkill -9 -f "^${TARGET_DIR}/VRCVideoCacher" 2>/dev/null || true
    sleep 1
    (nohup steam "steam://rungameid/${STEAM_APP_ID}" >/dev/null 2>&1 \
        || nohup xdg-open "steam://rungameid/${STEAM_APP_ID}" >/dev/null 2>&1 &)
    echo "VRCVideoCacher launched via Steam."

    echo "=== Waiting 5s for process status & logs... ==="
    sleep 5
    LOG_DIR="${XDG_CONFIG_HOME:-${HOME}/.config}/VRCVideoCacher/Logs"
    LOG_FILE=$(ls -t "${LOG_DIR}"/VRCVideoCacher*.log 2>/dev/null | head -n 1 || true)
    if [ -n "${LOG_FILE}" ] && [ -f "${LOG_FILE}" ]; then
        echo "=== Last 25 log lines (${LOG_FILE}) ==="
        tail -n 25 "${LOG_FILE}"
    fi

    echo "=== Process Status & Diagnostic Check ==="
    PIDS=$(pgrep -f "^${TARGET_DIR}/VRCVideoCacher" || true)
    if [ -n "${PIDS}" ]; then
        echo "VRCVideoCacher is RUNNING (PIDs: ${PIDS})"
        ps -p "$(echo "${PIDS}" | tr '\n' ',' | sed 's/,$//')" -o pid,user,%cpu,%mem,stat,start,time,command
    else
        echo "WARNING: VRCVideoCacher process is NOT running (Exited or Crashed after 5s)!"
        CRASH_REPORT="${XDG_CONFIG_HOME:-${HOME}/.config}/VRCVideoCacher/CRASH_REPORT.txt"
        if [ -f "${CRASH_REPORT}" ]; then
            echo "=== Found CRASH_REPORT.txt ==="
            cat "${CRASH_REPORT}"
        fi
    fi
fi
