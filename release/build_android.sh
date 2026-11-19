#!/usr/bin/env bash
# VALEBOUND — one-command Android build pipeline.
#
# Usage:
#   ./build_android.sh apk        # debug/test APK (signs with debug key)
#   ./build_android.sh aab        # signed release App Bundle for Google Play
#   ./build_android.sh run        # build + install + launch on connected device
#
# Prereqs (this machine):
#   - Unity Hub with Unity 2022.3 LTS + Android Build Support
#     (Editor: OpenJDK + Android SDK & NDK Tools, both checked)
#   - A keystore for release signing (see CREATE_KEYSTORE below)
set -euo pipefail

MODE="${1:-aab}"
UNITY_VERSION="2022.3.62f1"
PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"   # .../game
BUILD_OUT="$PROJECT_DIR/build"
KEYSTORE="$PROJECT_DIR/release/valebound.keystore"
KEYSTORE_PASS="CHANGE_ME_before_release"
KEY_ALIAS="valebound"

# --- locate Unity ---
UNITY=""
for c in \
  "/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity" \
  "/usr/local/bin/unity-editor" \
  "C:/Program Files/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity.exe" \
  "$HOME/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity"; do
  [ -x "$c" ] && UNITY="$c" && break
done
[ -z "$UNITY" ] && { echo "ERROR: Unity $UNITY_VERSION not found. Install via Unity Hub with Android Build Support."; exit 1; }

mkdir -p "$BUILD_OUT"

sign_args() {
  # release AAB signing happens through gradle (Player Settings keystore fields)
  echo ""
}

run_unity() {
  local method="$1"; local output="$2"
  echo "==> Unity batch: $method"
  "$UNITY" -batchmode -quit -projectPath "$PROJECT_DIR" \
    -executeMethod "$method" \
    -buildOutput "$output" \
    -logFile "$BUILD_OUT/build.log"
  echo "==> build log: $BUILD_OUT/build.log"
}

case "$MODE" in
  apk)
    run_unity "Valebound.EditorTools.BuildScript.BuildDebugApk" "$BUILD_OUT/VALEBOUND-debug.apk"
    echo "DONE: $BUILD_OUT/VALEBOUND-debug.apk"
    ;;
  aab)
    if [ ! -f "$KEYSTORE" ]; then
      echo "ERROR: release keystore missing at $KEYSTORE"
      echo "Create one (one time):"
      echo "  keytool -genkey -v -keystore $KEYSTORE -alias $KEY_ALIAS -keyalg RSA -keysize 2048 -validity 10000"
      exit 1
    fi
    run_unity "Valebound.EditorTools.BuildScript.BuildReleaseAab" "$BUILD_OUT/VALEBOUND-release.aab"
    echo "DONE: $BUILD_OUT/VALEBOUND-release.aab"
    echo "Upload to: Play Console > Production > Create release"
    ;;
  run)
    run_unity "Valebound.EditorTools.BuildScript.BuildAndRun" "$BUILD_OUT/VALEBOUND-debug.apk"
    ;;
  *)
    echo "Usage: $0 [apk|aab|run]"
    exit 1
    ;;
esac
