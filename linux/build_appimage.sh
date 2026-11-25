#!/usr/bin/env bash
# Builds Hesper's AppImage for Linux: the self-contained app (no .NET to install), its icon and menu
# entry, in one file that runs on most distributions. Run on Linux (GitHub Actions does: see
# .github/workflows/linux.yml) from anywhere in the repository:
#
#   linux/build_appimage.sh            → dist/linux/Hesper-<version>-x86_64.AppImage
#   BUILD=7 linux/build_appimage.sh    the build number the updater compares (0 for a build by hand)
#   ARCH=aarch64 …                     an arm64 one (built on an arm64 machine)
#
# Needs the .NET 10 SDK (dotnet on PATH) and curl. appimagetool and the AppImage runtime are fetched
# at pinned versions and checked by their SHA-256.
set -euo pipefail
cd "$(dirname "$0")/.."

ARCH=${ARCH:-$(uname -m)}
BUILD=${BUILD:-0}
VERSION=$(sed -n 's/^version = "\(.*\)"/\1/p' pyproject.toml | head -1)
case "$ARCH" in
  x86_64)  RID=linux-x64
           TOOL_SHA=ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0
           RUNTIME_SHA=2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d ;;
  aarch64) RID=linux-arm64
           TOOL_SHA=f0837e7448a0c1e4e650a93bb3e85802546e60654ef287576f46c71c126a9158
           RUNTIME_SHA=00cbdfcf917cc6c0ff6d3347d59e0ca1f7f45a6df1a428a0d6d8a78664d87444 ;;
  *) echo "Unknown architecture $ARCH"; exit 1 ;;
esac
TOOLS=build-cache/appimage-tools
APPDIR=build-cache/linux-$ARCH/Hesper.AppDir
OUT=dist/linux
mkdir -p "$TOOLS" "$OUT"

fetch() {  # url file sha256
  if [ ! -f "$2" ] || ! echo "$3  $2" | sha256sum -c --status; then
    curl -fsSL -o "$2" "$1"
    echo "$3  $2" | sha256sum -c --quiet || { rm -f "$2"; echo "$2 isn't the expected file"; exit 1; }
  fi
}
fetch "https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-$ARCH.AppImage" "$TOOLS/appimagetool-$ARCH" "$TOOL_SHA"
fetch "https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-$ARCH" "$TOOLS/runtime-$ARCH" "$RUNTIME_SHA"
chmod +x "$TOOLS/appimagetool-$ARCH"

echo "- Hesper $VERSION (build $BUILD) for $ARCH"
rm -rf "$APPDIR"
# Not a single file: inside the AppImage the files are read in place (nothing unpacked into ~/.net)
dotnet publish linux/Hesper/Hesper.Linux.csproj -c Release -r "$RID" --self-contained \
  -p:Version="$VERSION.$BUILD" -p:DebugType=none -p:GenerateDocumentationFile=false -o "$APPDIR/usr/lib/hesper" | grep -E "error|->" || true
test -x "$APPDIR/usr/lib/hesper/Hesper" || { echo "The build failed."; exit 1; }

# Its icon (drawn by the app itself, with cairo) and menu entry
"$APPDIR/usr/lib/hesper/Hesper" --write-icon "$TOOLS/icons"
install -D -m 644 "$TOOLS/icons/hesper.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/hesper.png"
cp "$TOOLS/icons/hesper.png" "$APPDIR/hesper.png"
# The licenses: Hesper's, the credits, and the full texts of what the AppImage carries (.NET, its runtime)
install -D -m 644 -t "$APPDIR/usr/share/doc/hesper" LICENSE THIRD_PARTY_NOTICES.md THIRD_PARTY_LICENSES.txt
ln -sf hesper.png "$APPDIR/.DirIcon"
cat > "$APPDIR/hesper.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=Hesper
GenericName=Meeting notes
Comment=Private meeting notes, fully offline
Exec=Hesper %u
Icon=hesper
Terminal=false
Categories=Office;AudioVideo;Audio;
MimeType=x-scheme-handler/hesper;
StartupWMClass=Hesper
EOF
cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
# Hesper's AppImage: starts the app inside it.
HERE="$(dirname "$(readlink -f "$0")")"
# .NET reads languages and dates with ICU (libicu, on nearly every desktop): without it, Hesper still
# starts, in English formats
if ! (ldconfig -p 2>/dev/null || true) | grep -q 'libicuuc\.so'; then export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1; fi
# NVIDIA's driver and WebKitGTK's DMA-BUF renderer can leave the windows blank
if [ -e /proc/driver/nvidia/version ] && [ -z "${WEBKIT_DISABLE_DMABUF_RENDERER:-}" ]; then export WEBKIT_DISABLE_DMABUF_RENDERER=1; fi
exec "$HERE/usr/lib/hesper/Hesper" "$@"
EOF
chmod +x "$APPDIR/AppRun"

FILE="$OUT/Hesper-$VERSION-$ARCH.AppImage"
rm -f "$FILE"
ARCH=$ARCH "$TOOLS/appimagetool-$ARCH" --appimage-extract-and-run --no-appstream --runtime-file "$TOOLS/runtime-$ARCH" "$APPDIR" "$FILE" >/dev/null
chmod +x "$FILE"
echo "OK: $FILE ($(du -h "$FILE" | cut -f1)), sha256 $(sha256sum "$FILE" | cut -d' ' -f1)"
