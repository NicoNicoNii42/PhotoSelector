cd "${HOME}/Pictures/DCIMTest/100LEICA" && find good verygood sortedout -name "*.DNG" -type f -exec mv {} . \; 2>/dev/null || true && echo "Reset complete! All .DNG files moved back to top level."
