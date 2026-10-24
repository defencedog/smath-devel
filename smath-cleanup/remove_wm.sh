#!/usr/bin/env bash
#
# remove_wm.sh -- wrapper that removes the SMath Studio watermark images
# (top + bottom) from every page of a PDF. Thin launcher around remove_wm.py,
# which contains all of the logic.
#
# The two watermarks are raster image artifacts stamped onto every page:
#   TOP    "Created using a free version of SMath Studio"
#   BOTTOM "Not for commercial use"
# They are drawn via a `/Rxx Do` image operator in each page's content stream;
# the Python side deletes those draw blocks (and cleans up the bitmaps SMath
# flattened the watermark into) so nothing renders.
#
# Usage:
#   ./remove_wm.sh [--self-test] [input.pdf] [output.pdf]
#
#   --self-test   clean as usual, then verify the result: watermarks gone from
#                 the top/bottom bands (palette + OCR), running headers present,
#                 searchable text unchanged, body images untouched. Exits
#                 non-zero if any check fails.
#
# Defaults:
#   input  = sample.pdf
#   output = <input>_clean.pdf
#
# Requirements:
#   python3 -m pip install pymupdf numpy scipy
# or install them system-wide (Debian/Ubuntu):
#   sudo apt install python3-pymupdf python3-numpy python3-scipy
# Optional: tesseract-ocr, for the OCR half of --self-test.
#
# Interpreter: "python3" from PATH by default. Point PYTHON at another one,
# e.g. a virtualenv, with:  PYTHON=/path/to/venv/bin/python ./remove_wm.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PY="${PYTHON:-python3}"
PY_SCRIPT="$SCRIPT_DIR/remove_wm.py"

if ! command -v "$PY" >/dev/null 2>&1; then
  echo "ERROR: python interpreter '$PY' not found (set PYTHON to override)" >&2
  exit 1
fi

if [[ ! -f "$PY_SCRIPT" ]]; then
  echo "ERROR: python script not found at $PY_SCRIPT" >&2
  exit 1
fi

SELF_TEST=0
ARGS=()
for arg in "$@"; do
  case "$arg" in
    --self-test) SELF_TEST=1 ;;
    -h|--help)
      sed -n '2,33p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *) ARGS+=("$arg") ;;
  esac
done

"$PY" "$PY_SCRIPT" ${ARGS[@]+"${ARGS[@]}"}

if [[ "$SELF_TEST" -eq 1 ]]; then
  "$PY" "$PY_SCRIPT" --verify ${ARGS[@]+"${ARGS[@]}"}
fi
