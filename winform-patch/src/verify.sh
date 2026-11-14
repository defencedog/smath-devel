#!/usr/bin/env bash
# Verify an installed patched System.Windows.Forms.dll.
#
# Usage:
#   ./verify.sh [SMath directory]
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TARGET="${1:-/home/ukhan/Binaries/SMathStudioDesktop.1_5_0_9678.Mono}"
APP="$TARGET/System.Windows.Forms.dll"
EXPECTED="10bc8f08b661c6574809d8065b1c2c15bf497e14e98d0436a2b7f12816908cf8"
ORIG_SHA="62439ae638d994b7983d3a033fdd4a4b2d91934528038e3c3d2b51f86bfa8460"

if [ ! -f "$APP" ]; then
    echo "error: not installed: $APP" >&2
    exit 1
fi

ACTUAL="$(sha256sum "$APP" | awk '{print $1}')"
if [ "$ACTUAL" != "$EXPECTED" ]; then
    echo "error: checksum mismatch" >&2
    echo "  expected: $EXPECTED" >&2
    echo "  actual:   $ACTUAL" >&2
    exit 1
fi
echo "SHA-256 OK: $ACTUAL"

# Capture tool output first. With `set -o pipefail`, `monodis | grep -q`
# can fail because grep exits early and monodis receives SIGPIPE.
METHODS="$(monodis --method "$APP" 2>/dev/null)"
if ! grep -q 'OnDialogShown' <<<"$METHODS"; then
    echo "error: FileDialog.OnDialogShown not found" >&2
    exit 1
fi
echo "FileDialog.OnDialogShown: present"

IL="$(monodis "$APP" 2>/dev/null)"
if ! awk '/default void HideSubPopups/,/end of method MenuTracker::HideSubPopups/' <<<"$IL" \
    | grep 'brfalse.s IL_009a' >/dev/null; then
    echo "error: HideSubPopups null-check not found" >&2
    exit 1
fi
echo "MenuTracker.HideSubPopups null-check: present"

BACKUP="$TARGET/backup/System.Windows.Forms.dll.orig-4.0.0.0"
if [ -f "$BACKUP" ]; then
    B="$(sha256sum "$BACKUP" | awk '{print $1}')"
    if [ "$B" = "$ORIG_SHA" ]; then
        echo "Original backup SHA-256 OK: $B"
    else
        echo "warning: backup checksum differs from the tested original: $B" >&2
    fi
fi

echo "verification passed"
