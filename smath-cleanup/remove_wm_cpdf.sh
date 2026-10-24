#!/usr/bin/env bash
#
# remove_wm_cpdf.sh -- remove SMath Studio watermark images using cpdf only.
#
# Backend: cpdf (no Python). Illustrations are PROTECTED by default through a
# combination of guards, so this is safe for PDFs that contain real images.
#
# The SMath free-version watermarks are two thin, page-wide raster strips that
# repeat on every page:
#   TOP    "Created using a free version of SMath Studio"
#   BOTTOM "Not for commercial use"
#
# A plain `cpdf -draft` would delete ALL images (including illustrations), so we
# instead identify each image and remove ONLY the matching ones with
# `cpdf -draft -draft-remove-only "/Name"`.
#
# ---------------------------------------------------------------------------
# SELECTION GUARDS (an image is removed only if ALL enabled guards pass)
# ---------------------------------------------------------------------------
#   1. GEOMETRY (default ON)  -- the image must be a thin, wide strip:
#          aspect ratio  = Width / Height  >=  --min-aspect   (default 6)
#          absolute height                  <=  --max-height   (default 120 px)
#      Illustrations/charts are normally taller or squarer and will not match.
#
#   2. TEXT-STRIP SAFETY (default ON) -- cpdf sees only each image's size, not
#      its position or pixels. A page-wide 20-120 px strip may be a watermark
#      OR the running header OR worksheet content that SMath rasterised under
#      the watermark. cpdf cannot tell them apart, so auto mode does NOT remove
#      such strips: only rule-like strips (height <= 8 px) are removed. Pass
#      --allow-text-strips if you have checked the file, or name the images
#      explicitly with --names for a full clean.
#
#   3. RECURRING-HEADER PROTECTION (default ON) -- an image present on every
#      page and taller than 8 px is assumed to be the running header and is
#      never removed automatically. SMath emits a fresh watermark image per
#      page, so real watermarks usually do NOT recur; the header does. Use
#      --names to remove a recurring image deliberately.
#
#   4. FILTER / "file extension" (default OFF) -- PDF images carry a compression
#      Filter instead of a filename extension. This is the closest analog to
#      "watermarks are .png, illustrations are .jpg":
#          /FlateDecode      ~ PNG-like, lossless (zlib)  <- SMath watermarks
#          /DCTDecode        ~ JPEG, lossy
#          /JPXDecode        ~ JPEG2000
#          /CCITTFaxDecode, /RunLengthDecode, /LZWDecode ...
#      Use --filter /FlateDecode to ONLY ever delete FlateDecode images, leaving
#      JPEG illustrations untouched. Off by default.
#
#   5. EXPLICIT OVERRIDE -- --names "/R16 /R17" removes exactly those XObject
#      names, bypassing guards entirely. This is the only way to remove text
#      strips with cpdf.
#
# NOTE: cpdf can only delete whole images. If SMath merged the watermark with
# worksheet content into one bitmap, removing it deletes content. Such files
# must be cleaned with remove_wm.py, which inpaints the watermark pixels.
#
# Usage:
#   ./remove_wm_cpdf.sh [options] [input.pdf]
#
# Key options:
#   -o, --output FILE      output PDF (default: <input>-clean.pdf)
#       --dry-run          list candidates only; do not write anything
#       --min-aspect N     geometry guard: min Width/Height (default 6, 0=off)
#       --max-height N     geometry guard: max pixel height (default 120, 0=off)
#       --min-pages-ratio R recurrence guard: min fraction of pages (default 0=off)
#       --filter F         filter guard: only remove images with this Filter
#                          (e.g. /FlateDecode); default: any
#       --names "A B"      explicit names to remove (the only way to remove
#                          text-height strips; bypasses the guards)
#       --protect "A B"    never remove these names
#       --allow-text-strips
#                          let auto mode remove text-height strips too (only do
#                          this after checking the file; may delete the running
#                          header or rasterised worksheet content)
#       --boxes            only with cpdf draft: draw crossed boxes where removed
#   -n, --no-op-guard      allow writing output even when 0 candidates matched
#   -v, --verbose          extra detail
#   -h, --help             this help
#
# Requires: cpdf, jq, and (for page count) pdfinfo or cpdf itself.

set -u

# ------------------------------------------------------------------ defaults
OUT=""
DRY_RUN=0
MIN_ASPECT="6"
MAX_HEIGHT="120"
MIN_PAGES_RATIO="0"
FILTER=""
EXPLICIT_NAMES=""
PROTECT_NAMES=""
BOXES=0
ALLOW_EMPTY=0
ALLOW_TEXT=0
VERBOSE=0
IN=""

# Strips no taller than this are treated as rule-like (watermark hairlines) and
# are safe to remove automatically. Anything taller may be the running header
# or rasterised worksheet content, which cpdf cannot distinguish.
RULE_MAX_HEIGHT=8

usage() { sed -n '2,80p' "$0" | sed 's/^# \{0,1\}//'; exit "${1:-0}"; }

die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }

need() { command -v "$1" >/dev/null 2>&1 || die "required tool '$1' not found"; }

# ------------------------------------------------------------------ parse args
while [[ $# -gt 0 ]]; do
  case "$1" in
    -o|--output)        OUT="${2:-}"; shift 2 ;;
    --dry-run)          DRY_RUN=1; shift ;;
    --min-aspect)       MIN_ASPECT="${2:-}"; shift 2 ;;
    --max-height)       MAX_HEIGHT="${2:-}"; shift 2 ;;
    --min-pages-ratio)  MIN_PAGES_RATIO="${2:-}"; shift 2 ;;
    --filter)           FILTER="${2:-}"; shift 2 ;;
    --names)            EXPLICIT_NAMES="${2:-}"; shift 2 ;;
    --protect)          PROTECT_NAMES="${2:-}"; shift 2 ;;
    --allow-text-strips) ALLOW_TEXT=1; shift ;;
    --boxes)            BOXES=1; shift ;;
    -n|--no-op-guard)   ALLOW_EMPTY=1; shift ;;
    -v|--verbose)       VERBOSE=1; shift ;;
    -h|--help)          usage 0 ;;
    -*)                 die "unknown option: $1 (try --help)" ;;
    *)                  [[ -z "$IN" ]] && IN="$1" || die "unexpected extra argument: $1"; shift ;;
  esac
done

IN="${IN:-$HOME/PDF/smath-demo.pdf}"
[[ -f "$IN" ]] || die "input PDF not found: $IN"
[[ -z "$OUT" ]] && OUT="${IN%.pdf}-clean.pdf"

need cpdf
need jq

# ------------------------------------------------------------- page count
PAGECOUNT="$(pdfinfo "$IN" 2>/dev/null | awk '/^Pages:/{print $2}')"
if [[ -z "${PAGECOUNT:-}" ]]; then
  # Fallback: derive from highest page number seen in the image list.
  PAGECOUNT="$(cpdf -list-images-json "$IN" 2>/dev/null \
    | jq '[.[].Pages[]] | max // 0')"
fi
[[ "${PAGECOUNT:-0}" -ge 1 ]] 2>/dev/null || die "could not determine page count for $IN"

# ------------------------------------------------------------- enumerate images
IMAGES_JSON="$(cpdf -list-images-json "$IN" 2>/dev/null || echo '[]')"
[[ "$(jq 'length' <<<"$IMAGES_JSON" 2>/dev/null)" == "0" ]] && {
  echo "No images found in $IN -- nothing to remove."
  exit 0
}

# Emit TSV: name  w  h  filter  npages  pagescsv
readarray -t ROWS < <(
  jq -r '.[] | [ .Name,
                   (.Width|tostring), (.Height|tostring),
                   (.Filter // "/none"),
                   (.Pages|length|tostring),
                   (.Pages|map(tostring)|join(",")) ] | @tsv' <<<"$IMAGES_JSON"
)

# ------------------------------------------------------------- classify
# Build candidate list (names with leading slash) + a printable report.
CANDIDATES=()
REPORT=""
TEXT_AMBIG=0
while IFS=$'\t' read -r NAME W H FILTERV NPAGES PAGESCSV; do
  [[ -z "$NAME" ]] && continue
  decision="KEEP"
  reason=""

  if [[ -n "$PROTECT_NAMES" && " $PROTECT_NAMES " == *" $NAME "* ]]; then
    reason="protected (--protect)"
  elif [[ -n "$EXPLICIT_NAMES" ]]; then
    # explicit mode
    if [[ " $EXPLICIT_NAMES " == *" $NAME "* ]]; then
      decision="REMOVE"; reason="explicit --names"
    else
      decision="KEEP"; reason="not in --names"
    fi
  else
    # geometry guard (aspect + height); a value of 0 disables that sub-guard
    geo_ok=1; greason=""
    if [[ "$MIN_ASPECT" != "0" ]]; then
      awk -v w="$W" -v h="$H" -v m="$MIN_ASPECT" 'BEGIN{exit !((h>0) && (w/h >= m))}' || { geo_ok=0; greason+="aspect"; }
    fi
    if [[ "$MAX_HEIGHT" != "0" ]]; then
      awk -v h="$H" -v m="$MAX_HEIGHT" 'BEGIN{exit !(h <= m)}' || { geo_ok=0; greason+="${greason:+,}height"; }
    fi
    # recurrence guard
    rec_ok=1
    awk -v n="$NPAGES" -v p="$PAGECOUNT" -v r="$MIN_PAGES_RATIO" 'BEGIN{exit !((p>0) && (n/p >= r))}' || rec_ok=0
    # filter guard
    filt_ok=1
    [[ -n "$FILTER" && "$FILTERV" != "$FILTER" ]] && filt_ok=0

    if [[ $geo_ok -eq 1 && $rec_ok -eq 1 && $filt_ok -eq 1 ]]; then
      if [[ "$H" -gt "$RULE_MAX_HEIGHT" && "$NPAGES" -eq "$PAGECOUNT" ]]; then
        # Same shape on every page: that is the running header in SMath's
        # export (watermarks are emitted per page), so never guess.
        decision="KEEP"; reason="recurs on every page (running header?) - use --names"
        TEXT_AMBIG=$((TEXT_AMBIG+1))
      elif [[ "$H" -gt "$RULE_MAX_HEIGHT" && $ALLOW_TEXT -eq 0 ]]; then
        # Could be watermark wording, the header, or content SMath rasterised
        # under the watermark. cpdf cannot tell, so leave it alone.
        decision="KEEP"; reason="text strip - verify and use --names"
        TEXT_AMBIG=$((TEXT_AMBIG+1))
      else
        decision="REMOVE"; reason="thin strip${FILTER:+ + filter $FILTERV}"
      fi
    else
      decision="KEEP"
      [[ $geo_ok -eq 0 ]] && reason+="not-thin($greason) "
      [[ $rec_ok -eq 0 ]] && reason+="not-recurring($NPAGES/$PAGECOUNT) "
      [[ $filt_ok -eq 0 ]] && reason+="filter!=$FILTER "
      [[ -z "$reason" ]] && reason="no-match"
    fi
  fi

  if [[ "$decision" == "REMOVE" ]]; then CANDIDATES+=("$NAME"); fi
  REPORT+="$(printf '%-8s %10s %-9s %-14s %5s/%-3s  %-6s %s\n' \
      "$NAME" "${W}x${H}" \
      "$(awk -v w="$W" -v h="$H" 'BEGIN{printf (h>0? "%.1f":"-"), w/(h>0?h:1)}')" \
      "$FILTERV" "$NPAGES" "$PAGECOUNT" "$decision" "$reason")"$'\n'
done < <(printf '%s\n' "${ROWS[@]}")

# ------------------------------------------------------------- report
echo "==========================================================================="
echo " input        : $IN  ($PAGECOUNT page(s))"
echo " output       : $OUT"
echo " guards       : aspect>=$MIN_ASPECT  height<=$MAX_HEIGHT  pages-ratio>=$MIN_PAGES_RATIO${FILTER:+  filter=$FILTER}"
echo " safety       : rules<=${RULE_MAX_HEIGHT}px auto; text strips $([[ $ALLOW_TEXT -eq 1 ]] && echo 'allowed (--allow-text-strips)' || echo 'need --names'); header protection on"
[[ -n "$EXPLICIT_NAMES" ]] && echo " explicit     : $EXPLICIT_NAMES"
[[ -n "$PROTECT_NAMES" ]] && echo " protected    : $PROTECT_NAMES"
echo "---------------------------------------------------------------------------"
printf '%-8s %10s %-9s %-14s %8s  %-6s %s\n' NAME SIZE ASPECT FILTER PAGES DECISION REASON
echo "---------------------------------------------------------------------------"
printf '%s' "$REPORT"
echo "---------------------------------------------------------------------------"
REFUSE=0
[[ $TEXT_AMBIG -gt 0 && $ALLOW_TEXT -eq 0 ]] && REFUSE=1
echo " candidates to remove: ${#CANDIDATES[@]}   ${CANDIDATES[*]:-}$([[ $REFUSE -eq 1 ]] && echo '  (withheld)')"
echo "==========================================================================="
if [[ $REFUSE -eq 1 ]]; then
  echo "NOTE: $TEXT_AMBIG strip(s) are text-height, so cpdf cannot tell watermark wording"
  echo "      from the running header or rasterised worksheet content."
  echo ""
  echo "      * Pass --names \"/Rxx /Ryy ...\" for the strips you have inspected"
  echo "        (cpdf -list-images shows the names)."
  echo "      * Or --allow-text-strips after checking the file yourself."
  echo "      * If SMath merged the watermark into content bitmaps (such as sample.pdf),"
  echo "        no cpdf selection is safe -- use remove_wm.py, which inpaints the"
  echo "        watermark pixels and keeps the content."
  echo "==========================================================================="
  if [[ $DRY_RUN -eq 1 ]]; then
    echo "DRY RUN -- no file written."
    exit 0
  fi
  echo "REFUSING to write: removing only the hairline rules would leave the watermark"
  echo "wording in place, and guessing on the text strips could delete the header or"
  echo "rasterised content. Nothing was modified (exit 2)." >&2
  exit 2
fi

if [[ ${#CANDIDATES[@]} -eq 0 ]]; then
  echo "No images matched the guards -- nothing removed (illustrations preserved)."
  if [[ $ALLOW_EMPTY -eq 1 && $DRY_RUN -eq 0 ]]; then
    cp -- "$IN" "$OUT"; echo "Wrote unchanged copy: $OUT"
  fi
  exit 0
fi

if [[ $DRY_RUN -eq 1 ]]; then
  echo "DRY RUN -- no file written. Re-run without --dry-run to apply."
  exit 0
fi

# ------------------------------------------------------------- remove (chained)
# cpdf honours only the LAST -draft-remove-only per invocation, so chain runs.
TMP="$(mktemp --suffix=.pdf)"
trap 'rm -f -- "$TMP" "$TMP.step"' EXIT
cp -- "$IN" "$TMP"
i=0
for NAME in "${CANDIDATES[@]}"; do
  i=$((i+1))
  [[ $VERBOSE -eq 1 ]] && echo "  [$i/${#CANDIDATES[@]}] removing $NAME"
  if [[ $BOXES -eq 1 ]]; then
    cpdf -draft -boxes -draft-remove-only "$NAME" "$TMP" -o "$TMP.step" || die "cpdf failed on $NAME"
  else
    cpdf -draft -draft-remove-only "$NAME" "$TMP" -o "$TMP.step" || die "cpdf failed on $NAME"
  fi
  mv -f -- "$TMP.step" "$TMP"
done

cp -- "$TMP" "$OUT"

# ------------------------------------------------------------- verify
BEFORE="$(jq 'length' <<<"$IMAGES_JSON")"
AFTER="$(cpdf -list-images-json "$OUT" 2>/dev/null | jq 'length')"
echo "Removed ${#CANDIDATES[@]} watermark image(s). Image instances: $BEFORE -> $AFTER."
echo "Wrote: $OUT"
