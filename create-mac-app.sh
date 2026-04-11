#!/bin/bash

# Photo Sorter - macOS Application Bundle Creator
# This script builds the Photo Sorter application and creates a macOS .app bundle

set -e  # Exit on error

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

INSTALL_TO_APPLICATIONS=false
for arg in "$@"; do
    case "$arg" in
        --install) INSTALL_TO_APPLICATIONS=true ;;
    esac
done

echo "📸 Photo Sorter - macOS Application Bundle Creator"
echo "=================================================="

# Configuration
APP_NAME="Photo Sorter"
APP_IDENTIFIER="com.photoselector.PhotoSorter"
VERSION="1.0.0"
# Unique per build so Launch Services / Spotlight treat replaced bundles as new (not stale cache).
VERSION_BUILD="${VERSION}.$(date +%Y%m%d.%H%M%S)"
BUILD_DIR="test-build"  # Updated to match the publish output directory
APP_BUNDLE_DIR="Photo Sorter.app"
APP_CONTENTS_DIR="$APP_BUNDLE_DIR/Contents"
APP_MACOS_DIR="$APP_CONTENTS_DIR/MacOS"
APP_RESOURCES_DIR="$APP_CONTENTS_DIR/Resources"
APP_ICON="AppIcon.icns"

# Same removals as "Uninstall Photo Sorter.command", run up front so --install always
# replaces a clean slot (no-op if nothing was installed).
if [ "$INSTALL_TO_APPLICATIONS" = true ]; then
    echo "🧹 Preparing for install (remove previous Applications copy and Desktop alias if any)..."
    rm -rf "$HOME/Desktop/Photo Sorter" 2>/dev/null || true

    APP_SYS="/Applications/Photo Sorter.app"
    if [ -d "$APP_SYS" ]; then
        INST_BIN="$APP_SYS/Contents/MacOS/PhotoSorterAvalonia"
        if [ -f "$INST_BIN" ] && command -v lsof >/dev/null 2>&1 && lsof "$INST_BIN" >/dev/null 2>&1; then
            echo "❌ Photo Sorter is still running from /Applications. Quit it, then run:"
            echo "   ./create-mac-app.sh --install"
            exit 1
        fi
        rm -rf "$APP_SYS"
        echo "   Removed: $APP_SYS"
    else
        echo "   (No existing app in /Applications.)"
    fi
    echo ""
fi

echo "🔧 Building the application..."

# Clean previous builds
echo "🧹 Cleaning previous builds..."
rm -rf "$BUILD_DIR" "$APP_BUNDLE_DIR" 2>/dev/null || true

# Build the application for macOS
echo "🏗️  Building for macOS (Release mode)..."
cd PhotoSorterAvalonia
# Force a full rebuild so publish output is never silently reused from disk cache.
dotnet clean -c Release >/dev/null
rm -rf bin obj
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output ../test-build
cd ..

echo "📦 Creating macOS application bundle..."

# Create the .app bundle structure
mkdir -p "$APP_MACOS_DIR"
mkdir -p "$APP_RESOURCES_DIR"

# Copy the published files
echo "📁 Copying application files..."
cp -r "$BUILD_DIR"/* "$APP_MACOS_DIR/"

# Create Info.plist
echo "📝 Creating Info.plist..."
cat > "$APP_CONTENTS_DIR/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundleDisplayName</key>
    <string>$APP_NAME</string>
    <key>CFBundleIdentifier</key>
    <string>$APP_IDENTIFIER</string>
    <key>CFBundleVersion</key>
    <string>$VERSION_BUILD</string>
    <key>CFBundleShortVersionString</key>
    <string>$VERSION</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleSignature</key>
    <string>????</string>
    <key>CFBundleExecutable</key>
    <string>PhotoSorterAvalonia</string>
    <key>CFBundleIconFile</key>
    <string>$APP_ICON</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
    <key>NSSupportsAutomaticTermination</key>
    <true/>
    <key>NSHumanReadableCopyright</key>
    <string>Copyright © 2024 NicoNicoNii. All rights reserved.</string>
</dict>
</plist>
EOF

# Create a simple app icon (if one doesn't exist)
if [ ! -f "$APP_RESOURCES_DIR/$APP_ICON" ]; then
    echo "🎨 Creating placeholder app icon..."
    # Create a simple PNG icon using sips
    sips --help > /dev/null 2>&1
    if [ $? -eq 0 ]; then
        # Create a simple blue square as placeholder icon
        mkdir -p "icon.iconset"
        
        # Create a 1024x1024 blue PNG
        sips --setProperty format png --padToHeightWidth 1024 1024 -c 1024 1024 /System/Library/CoreServices/CoreTypes.bundle/Contents/Resources/GenericApplicationIcon.icns --out icon.iconset/icon_512x512@2x.png 2>/dev/null || true
        
        # Copy to all sizes
        cp icon.iconset/icon_512x512@2x.png icon.iconset/icon_512x512.png
        cp icon.iconset/icon_512x512@2x.png icon.iconset/icon_256x256@2x.png
        cp icon.iconset/icon_512x512@2x.png icon.iconset/icon_256x256.png
        cp icon.iconset/icon_512x512@2x.png icon.iconset/icon_128x128@2x.png
        cp icon.iconset/icon_512x512@2x.png icon.iconset/icon_128x128.png
        cp icon.iconset/icon_512x512@2x.png icon.iconset/icon_32x32@2x.png
        cp icon.iconset/icon_512x512@2x.png icon.iconset/icon_32x32.png
        cp icon.iconset/icon_512x512@2x.png icon.iconset/icon_16x16@2x.png
        cp icon.iconset/icon_512x512@2x.png icon.iconset/icon_16x16.png
        
        # Create .icns file
        iconutil -c icns icon.iconset -o "$APP_RESOURCES_DIR/$APP_ICON" 2>/dev/null || true
        
        # Clean up
        rm -rf icon.iconset 2>/dev/null || true
    fi
    
    # If icon creation failed, remove the icon reference from Info.plist
    if [ ! -f "$APP_RESOURCES_DIR/$APP_ICON" ]; then
        echo "⚠️  Could not create app icon, using default macOS icon"
        # Remove CFBundleIconFile from Info.plist
        sed -i '' '/CFBundleIconFile/d' "$APP_CONTENTS_DIR/Info.plist"
    fi
fi

# Make the main executable executable
chmod +x "$APP_MACOS_DIR/PhotoSorterAvalonia"

# Create PkgInfo
echo "📄 Creating PkgInfo..."
echo "APPL????" > "$APP_CONTENTS_DIR/PkgInfo"

echo "✅ Application bundle created: $APP_BUNDLE_DIR"

# Register the bundle with Launch Services and import metadata so Spotlight (⌘Space)
# can find it by display name without waiting for a full volume reindex.
LSREGISTER="/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister"
APP_ABS="$SCRIPT_DIR/$APP_BUNDLE_DIR"
if [ -x "$LSREGISTER" ]; then
    echo "🔎 Registering app with Launch Services (Spotlight / Open With)..."
    "$LSREGISTER" -f "$APP_ABS" 2>/dev/null || true
fi
if command -v mdimport >/dev/null 2>&1; then
    echo "🔎 Queuing Spotlight metadata import..."
    mdimport "$APP_ABS" 2>/dev/null || true
fi

APP_IN_APPLICATIONS="/Applications/Photo Sorter.app"
if [ "$INSTALL_TO_APPLICATIONS" = true ]; then
    echo ""
    echo "📥 --install: updating copy in /Applications (what Spotlight usually opens)..."
    bash "$SCRIPT_DIR/install-photo-sorter.sh"
elif [ -d "$APP_IN_APPLICATIONS" ]; then
    echo ""
    echo "⚠️  You still have: $APP_IN_APPLICATIONS"
    echo "   Spotlight and Open often use that copy, not the bundle in this repo."
    echo "   Re-run with:  ./create-mac-app.sh --install"
    echo "   Or install:   ./install-photo-sorter.sh"
fi

# Optional: Copy to Applications folder
echo ""
echo "📂 Installation options:"
echo "1. Run from current location: open \"$APP_BUNDLE_DIR\""
echo "2. Copy to Applications folder (recommended for Spotlight):"
echo "   ./create-mac-app.sh --install"
echo "   # or: ./install-photo-sorter.sh"
echo "3. Create alias on Desktop:"
echo "   ln -s \"$SCRIPT_DIR/$APP_BUNDLE_DIR\" \"$HOME/Desktop/Photo Sorter\""
echo ""
echo "🎯 To run the application:"
echo "   Double-click on \"$APP_BUNDLE_DIR\" in Finder"
echo ""
echo "🔧 Build completed successfully!"

# Create a simple uninstall script
cat > "Uninstall Photo Sorter.command" << 'EOF'
#!/bin/bash
echo "Removing Photo Sorter application..."
rm -rf "/Applications/Photo Sorter.app" 2>/dev/null
rm -rf "$HOME/Desktop/Photo Sorter" 2>/dev/null
echo "✅ Photo Sorter has been removed."
EOF
chmod +x "Uninstall Photo Sorter.command"

echo ""
echo "📝 Created 'Uninstall Photo Sorter.command' for easy removal."