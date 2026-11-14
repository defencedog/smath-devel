#!/usr/bin/env bash
# Install the patched WinForms assembly into an SMath Studio directory.
#
# Usage:
#   ./apply.sh [SMath directory]
#
# The default target is the directory used during testing:
#   /home/ukhan/Binaries/SMathStudioDesktop.1_5_0_9678.Mono
#
# Close SMath before running this; restart it afterwards.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TARGET="${1:-/home/ukhan/Binaries/SMathStudioDesktop.1_5_0_9678.Mono}"
PATCHED="$HERE/../patched/System.Windows.Forms.dll"
GAC="/usr/lib/mono/gac/System.Windows.Forms/4.0.0.0__b77a5c561934e089/System.Windows.Forms.dll"
BACKUP_DIR="$TARGET/backup"
BACKUP="$BACKUP_DIR/System.Windows.Forms.dll.orig-4.0.0.0"

[ -d "$TARGET" ] || { echo "error: SMath directory not found: $TARGET" >&2; exit 1; }
[ -f "$PATCHED" ] || { echo "error: patched assembly not found: $PATCHED" >&2; exit 1; }
[ -f "$GAC" ] || { echo "error: Mono GAC assembly not found: $GAC" >&2; exit 1; }

mkdir -p "$BACKUP_DIR"
if [ ! -f "$BACKUP" ]; then
    cp -p "$GAC" "$BACKUP"
    echo "Backed up original GAC assembly to: $BACKUP"
else
    echo "Backup already exists: $BACKUP (left untouched)"
fi

cp "$PATCHED" "$TARGET/System.Windows.Forms.dll"

echo "Installed patched assembly to: $TARGET/System.Windows.Forms.dll"
sha256sum "$TARGET/System.Windows.Forms.dll"

if pgrep -u "$UID" -f 'mono.*Solver\.exe' >/dev/null 2>&1; then
    echo
    echo "Note: SMath is currently running. Close and restart it to load the patch."
fi
