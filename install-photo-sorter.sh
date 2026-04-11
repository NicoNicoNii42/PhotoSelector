#!/bin/bash

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "📸 Photo Sorter - Installation Helper"
echo "======================================"

# Configuration
APP_NAME="Photo Sorter"
APP_BUNDLE="Photo Sorter.app"
APP_SOURCE="$SCRIPT_DIR/$APP_BUNDLE"
APP_DEST="/Applications/$APP_BUNDLE"

echo "🔧 Checking current app version..."
if [ ! -f "$APP_SOURCE/Contents/MacOS/PhotoSorterAvalonia" ]; then
    echo "❌ Error: App bundle not found at $APP_SOURCE"
    echo "   Please run './create-mac-app.sh' first to create the app bundle."
    exit 1
fi

echo "✅ Found app bundle: $APP_SOURCE"
echo "   Created: $(stat -f "%Sm" "$APP_SOURCE/Contents/MacOS/PhotoSorterAvalonia")"

# Check if app exists in Applications
if [ -d "$APP_DEST" ]; then
    echo "📋 Found existing app in Applications folder:"
    echo "   Modified: $(stat -f "%Sm" "$APP_DEST/Contents/MacOS/PhotoSorterAvalonia" 2>/dev/null || echo "Unknown")"

    INST_BIN="$APP_DEST/Contents/MacOS/PhotoSorterAvalonia"
    if [ -f "$INST_BIN" ] && command -v lsof >/dev/null 2>&1 && lsof "$INST_BIN" >/dev/null 2>&1; then
        echo ""
        echo "❌ That copy is still in use (the app is running). Quit Photo Sorter, then run this script again."
        echo "   macOS will not reliably replace the bundle while the executable is open."
        exit 1
    fi

    echo ""
    echo "🔄 Replacing with new version..."
    rm -rf "$APP_DEST"
fi

echo "📥 Installing to Applications folder..."
# ditto handles bundles and metadata more reliably than cp for .app replacements.
ditto "$APP_SOURCE" "$APP_DEST"

echo "✅ Installed to: $APP_DEST"
echo "   Modified: $(stat -f "%Sm" "$APP_DEST/Contents/MacOS/PhotoSorterAvalonia")"

SRC_SUM=$(shasum -a 256 "$APP_SOURCE/Contents/MacOS/PhotoSorterAvalonia" | awk '{print $1}')
DST_SUM=$(shasum -a 256 "$APP_DEST/Contents/MacOS/PhotoSorterAvalonia" | awk '{print $1}')
if [ "$SRC_SUM" != "$DST_SUM" ]; then
    echo "❌ Install verification failed: main executable in /Applications does not match the bundle you built."
    echo "   Source sha256: $SRC_SUM"
    echo "   Installed sha256: $DST_SUM"
    exit 1
fi
echo "   Verified: installed binary matches repo bundle (sha256)."

LSREGISTER="/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister"
if [ -x "$LSREGISTER" ]; then
    echo ""
    echo "🔎 Registering installed bundle with Launch Services..."
    "$LSREGISTER" -f "$APP_DEST" 2>/dev/null || true
fi
if command -v mdimport >/dev/null 2>&1; then
    echo "🔎 Queuing Spotlight metadata for installed app..."
    mdimport "$APP_DEST" 2>/dev/null || true
fi

echo ""
echo "🔍 Refreshing Launch Services (so Spotlight opens the new binary)..."
echo "   Note: This may require administrator privileges for full effect."

# Clear LaunchServices database (non-destructive)
"$LSREGISTER" -kill -r -domain local -domain system -domain user 2>/dev/null || true

# Restart Finder to pick up changes (ignore if Finder was not running)
killall Finder 2>/dev/null || true

echo "✅ Launch Services refreshed and Finder restarted"

echo ""
echo "🎯 Installation complete!"
echo ""
echo "To run the Photo Sorter:"
echo "1. Press ⌘ + Space to open Spotlight"
echo "2. Type 'Photo Sorter' and press Enter"
echo "3. Or find it in Finder → Applications → Photo Sorter.app"
echo ""
echo "📝 Verify the version:"
echo "   The app should have EXIF auto-rotation (portrait photos auto-rotate)"
echo "   and responsive UI (zoom works properly for both portrait/landscape)"

echo ""
echo "🔄 If you still see the old version:"
echo "1. Open Terminal"
echo "2. Run: sudo /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -kill -r -domain local -domain system -domain user"
echo "3. Run: killall Finder"
echo "4. Run: sudo mdutil -E /"
echo "5. Wait a few minutes for Spotlight to reindex"

echo ""
echo "💡 Tip: You can also run the app directly from:"
echo "   open \"/Applications/Photo Sorter.app\""