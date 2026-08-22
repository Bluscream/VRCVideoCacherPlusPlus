#!/usr/bin/env bash
# Build pipeline for VRCVideoCacher.
#
# Actions are opt-in flags. They always run in the order listed below regardless of the
# order you pass them, because the dependencies only make sense one way round — there is
# no point tagging a release before the thing has been built, or pushing a commit that
# was never linted. Any action that fails aborts the rest immediately, and dotnet is run
# with warnings promoted to errors throughout.
#
#   --bump      raise the patch component of <Version> in VRCVideoCacher.csproj
#   --lint      locale check, extension check, strict compile, full test suite
#   --build     publish yt-dlp-stub and VRCVideoCacher for Steam (Linux x64)
#   --deploy    rsync the publish output into the Steam directory
#   --commit    commit the working tree      (-m MESSAGE, defaults to "Release vX.Y.Z")
#   --push      push the current branch and any tag created by --bump
#   --release   create the GitHub release for the current version (notes are generated;
#               binaries are attached by the CI publish job, not from here)
#
#   --all       everything except --deploy, which is local-only
#   -n|--dry-run  print each action instead of running the ones that change the world
#
# Running it with no arguments at all builds and deploys, which is what it always did.
# Passing any flag turns that off, so nothing gets deployed unless you asked for it.
#
# Restarting the app is deliberately not one of the actions. It used to be --restart,
# which killed whatever was running, relaunched through Steam and guessed at success
# from a five-second sleep — too blunt for something you usually want to watch happen.
# Start it yourself when you are ready:
#
#   steam steam://rungameid/4296960
#
# Everything machine-specific is an environment variable with the author's setup as the
# default, so this is overridable rather than only working on one machine:
#
#   VVC_TARGET_DIR   where to deploy         (default: the Steam common dir below)
#   VVC_CONTAINER    distrobox container     (default: arch; set empty to use the host dotnet)
#   VVC_TRIMMED      1 to publish trimmed    (default: 0, see note further down)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TARGET_DIR="${VVC_TARGET_DIR:-/run/media/system/Data/Games/Steam/steamapps/common/VRCVideoCacher}"
CONTAINER_NAME="${VVC_CONTAINER-arch}"
TRIMMED="${VVC_TRIMMED:-0}"
TMP_OUT="${SCRIPT_DIR}/output_steam_linux"
CSPROJ="${SCRIPT_DIR}/VRCVideoCacher/VRCVideoCacher.csproj"

DO_BUMP=false
DO_LINT=false
DO_BUILD=false
DO_DEPLOY=false
DO_COMMIT=false
DO_PUSH=false
DO_RELEASE=false
DRY_RUN=false
COMMIT_MESSAGE=""
ARG_COUNT=$#

while [ $# -gt 0 ]; do
    case "$1" in
        --bump)    DO_BUMP=true ;;
        --lint)    DO_LINT=true ;;
        --build)   DO_BUILD=true ;;
        --deploy)  DO_DEPLOY=true ;;
        --commit)  DO_COMMIT=true ;;
        --push)    DO_PUSH=true ;;
        --release) DO_RELEASE=true ;;
        --all)
            DO_BUMP=true; DO_LINT=true; DO_BUILD=true
            DO_COMMIT=true; DO_PUSH=true; DO_RELEASE=true
            ;;
        -n|--dry-run) DRY_RUN=true ;;
        -m|--message)
            [ $# -ge 2 ] || { echo "error: $1 needs a message" >&2; exit 2; }
            COMMIT_MESSAGE="$2"; shift
            ;;
        -m=*|--message=*) COMMIT_MESSAGE="${1#*=}" ;;
        -h|--help) sed -n '2,37p' "$0" | sed 's/^# \?//'; exit 0 ;;
        *) echo "Unknown argument: $1" >&2; exit 2 ;;
    esac
    shift
done

# Backwards compatibility: bare ./build.sh is a deploy. Keyed on there being no arguments
# at all rather than on "no action flags selected", so that a flag which selects no action
# can never fall through to here and deploy something nobody asked to deploy.
if [ "$ARG_COUNT" -eq 0 ]; then
    DO_BUILD=true
    DO_DEPLOY=true
fi

step() { echo; echo "=== $* ==="; }
fail() { echo "error: $*" >&2; exit 1; }

# Guards a command that changes something outside this working tree.
run() {
    if [ "$DRY_RUN" = true ]; then
        echo "[dry-run] $*"
        return 0
    fi
    "$@"
}

# Run dotnet inside the container when one is configured, otherwise straight on the host.
dotnet_run() {
    if [ -n "${CONTAINER_NAME}" ]; then
        distrobox enter "${CONTAINER_NAME}" -- dotnet "$@"
    else
        dotnet "$@"
    fi
}

read_version() {
    sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$CSPROJ" | head -n 1
}

VERSION="$(read_version)"
[ -n "$VERSION" ] || fail "could not read <Version> from ${CSPROJ}"

require_clean_tree() {
    git -C "$SCRIPT_DIR" diff --quiet && git -C "$SCRIPT_DIR" diff --cached --quiet \
        || fail "working tree is dirty; commit or stash first"
}

# --- bump -------------------------------------------------------------------------------
# Versioning is YEAR.MONTH.RELEASE, so rolling into a new month restarts the counter
# rather than carrying the old month's patch number forward.
if [ "$DO_BUMP" = true ]; then
    step "Bumping version (currently ${VERSION})"

    IFS='.' read -r OLD_YEAR OLD_MONTH OLD_PATCH <<< "$VERSION"
    NOW_YEAR="$(date +%Y)"
    NOW_MONTH="$(date +%-m)"

    if [ "$OLD_YEAR" = "$NOW_YEAR" ] && [ "$OLD_MONTH" = "$NOW_MONTH" ]; then
        NEW_VERSION="${NOW_YEAR}.${NOW_MONTH}.$((OLD_PATCH + 1))"
    else
        NEW_VERSION="${NOW_YEAR}.${NOW_MONTH}.0"
    fi

    if [ "$DRY_RUN" = true ]; then
        echo "[dry-run] ${VERSION} -> ${NEW_VERSION}"
    else
        # Anchored on the exact old value so this cannot touch a PackageReference Version.
        sed -i "s:<Version>${VERSION}</Version>:<Version>${NEW_VERSION}</Version>:" "$CSPROJ"
        [ "$(read_version)" = "$NEW_VERSION" ] || fail "version bump did not apply"
        echo "${VERSION} -> ${NEW_VERSION}"
    fi
    VERSION="$NEW_VERSION"
fi

TAG="v${VERSION}"

# --- lint -------------------------------------------------------------------------------
if [ "$DO_LINT" = true ]; then
    step "Checking locales"
    python3 "${SCRIPT_DIR}/scripts/lint-locales.py" || fail "locale check failed"

    step "Checking browser extension"
    "${SCRIPT_DIR}/BrowserExtension/build.sh" --check || fail "browser extension check failed"

    step "Compiling with warnings as errors"
    dotnet_run build "${SCRIPT_DIR}/VRCVideoCacher.sln" -c Release -warnaserror \
        || fail "build produced warnings or errors"

    step "Running tests"
    dotnet_run test "${SCRIPT_DIR}/VRCVideoCacher.Tests/VRCVideoCacher.Tests.csproj" -c Release \
        || fail "tests failed"
fi

# --- build ------------------------------------------------------------------------------
if [ "$DO_BUILD" = true ]; then
    step "Building yt-dlp-stub"
    dotnet_run publish "${SCRIPT_DIR}/yt-dlp-stub/yt-dlp-stub.csproj" -c Release -r win-x64 -warnaserror
    cp "${SCRIPT_DIR}/yt-dlp-stub/bin/Release/net10.0/win-x64/publish/yt-dlp-stub.exe" "${SCRIPT_DIR}/VRCVideoCacher/"

    # Deploying loose files rather than a single trimmed binary keeps local iteration fast
    # and makes stack traces readable. It does mean this path does not exercise trimming —
    # CI's publish job does that, and VVC_TRIMMED=1 reproduces it here when you need to.
    step "Building VRCVideoCacher for Steam (Linux x64, trimmed=${TRIMMED}, v${VERSION})"
    rm -rf "${TMP_OUT}"
    dotnet_run publish "$CSPROJ" \
        -c SteamRelease \
        -r linux-x64 \
        -o "${TMP_OUT}" \
        --self-contained true \
        -warnaserror \
        -p:PublishSingleFile=false \
        -p:PublishTrimmed="$([ "${TRIMMED}" = "1" ] && echo true || echo false)"
fi

# --- deploy -----------------------------------------------------------------------------
if [ "$DO_DEPLOY" = true ]; then
    [ -d "${TMP_OUT}" ] || fail "nothing to deploy: ${TMP_OUT} does not exist (run --build)"

    step "Deploying to ${TARGET_DIR}"
    run mkdir -p "${TARGET_DIR}"
    run rsync -av --delete --exclude='CachedAssets' --exclude='logs' "${TMP_OUT}/" "${TARGET_DIR}/"
    echo "=== Deployment Complete ==="
fi

# --- commit -----------------------------------------------------------------------------
if [ "$DO_COMMIT" = true ]; then
    step "Committing"
    if git -C "$SCRIPT_DIR" diff --quiet && git -C "$SCRIPT_DIR" diff --cached --quiet; then
        echo "Nothing to commit."
    else
        MESSAGE="${COMMIT_MESSAGE:-Release ${TAG}}"
        run git -C "$SCRIPT_DIR" add -A
        run git -C "$SCRIPT_DIR" commit -m "$MESSAGE" || fail "commit failed"
    fi

    if [ "$DO_BUMP" = true ]; then
        if git -C "$SCRIPT_DIR" rev-parse -q --verify "refs/tags/${TAG}" >/dev/null; then
            echo "Tag ${TAG} already exists, leaving it alone."
        else
            run git -C "$SCRIPT_DIR" tag -a "$TAG" -m "$TAG" || fail "could not create tag ${TAG}"
        fi
    fi
fi

# --- push -------------------------------------------------------------------------------
if [ "$DO_PUSH" = true ]; then
    step "Pushing"
    # Uncommitted work would be silently left behind by a push that appears to succeed.
    [ "$DRY_RUN" = true ] || require_clean_tree
    BRANCH="$(git -C "$SCRIPT_DIR" rev-parse --abbrev-ref HEAD)"
    run git -C "$SCRIPT_DIR" push origin "$BRANCH" || fail "push failed"
    if git -C "$SCRIPT_DIR" rev-parse -q --verify "refs/tags/${TAG}" >/dev/null; then
        run git -C "$SCRIPT_DIR" push origin "$TAG" || fail "pushing tag ${TAG} failed"
    fi
fi

# --- release ----------------------------------------------------------------------------
if [ "$DO_RELEASE" = true ]; then
    step "Creating GitHub release ${TAG}"
    command -v gh >/dev/null || fail "gh is not installed; cannot create a release"

    if gh release view "$TAG" >/dev/null 2>&1; then
        fail "release ${TAG} already exists"
    fi

    # The tag has to be on the remote first or gh will create one from whatever the
    # default branch happens to point at.
    if [ "$DRY_RUN" = false ] && ! git -C "$SCRIPT_DIR" ls-remote --exit-code --tags origin "$TAG" >/dev/null 2>&1; then
        fail "tag ${TAG} is not on origin; run --push first"
    fi

    run gh release create "$TAG" \
        --title "$TAG" \
        --generate-notes \
        || fail "creating the release failed"
fi
