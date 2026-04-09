#!/bin/bash

echo "📸 Photo Sorter - Installation Helper"
echo "======================================"

# Configuration
APP_NAME="Photo Sorter"
APP_BUNDLE="Photo Sorter.app"
APP_SOURCE="./$APP_BUNDLE"
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
    
    echo ""
    echo "🔄 Replacing with new version..."
    rm -rf "$APP_DEST"
fi

echo "📥 Installing to Applications folder..."
cp -r "$APP_SOURCE" "$APP_DEST"

echo "✅ Installed to: $APP_DEST"
echo "   Version: $(stat -f "%Sm" "$APP_DEST/Contents/MacOS/PhotoSorterAvalonia")"

echo ""
echo "🔍 Clearing macOS app cache..."
echo "   Note: This requires administrator privileges"

# Clear LaunchServices database (non-destructive)
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -kill -r -domain local -domain system -domain user 2>/dev/null || true

# Restart Finder to pick up changes
killall Finder 2>/dev/null || true

echo "✅ Cache cleared and Finder restarted"

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