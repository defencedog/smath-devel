#!/usr/bin/env bash
# Build the patched System.Windows.Forms.dll from PatchShown.cs.
#
# Usage:
#   ./build.sh [original.dll] [output.dll]
#
# Environment overrides:
#   MCS=...    Mono C# compiler (default: mcs)
#   MONO=...   Mono runtime (default: mono)
#   CECIL=...  Mono.Cecil.dll path
#   ORIG=...   input System.Windows.Forms.dll
#   OUT=...    output assembly path
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

MCS="${MCS:-mcs}"
MONO="${MONO:-mono}"
CECIL="${CECIL:-/usr/lib/mono/gac/Mono.Cecil/0.11.1.0__0738eb9f132ed756/Mono.Cecil.dll}"
ORIG="${1:-${ORIG:-/usr/lib/mono/gac/System.Windows.Forms/4.0.0.0__b77a5c561934e089/System.Windows.Forms.dll}}"
OUT="${2:-${OUT:-$HERE/../patched/System.Windows.Forms.dll}}"

for f in "$CECIL" "$ORIG"; do
    if [ ! -f "$f" ]; then
        echo "error: file not found: $f" >&2
        exit 1
    fi
done

mkdir -p "$(dirname "$OUT")"

"$MCS" -r:"$CECIL" -out:"$HERE/PatchShown.exe" "$HERE/PatchShown.cs"
"$MONO" "$HERE/PatchShown.exe" "$ORIG" "$OUT"

echo
echo "Output: $OUT"
sha256sum "$OUT"
