#!/usr/bin/env bash
#
# longimg2a4pdf.sh - split a long (tall) image into A4 pages and combine them
#                    into a single PDF.
#
# Typical use: a Mathcad / spreadsheet-style screenshot that is one very tall
# image.  The image is scaled to the width of the A4 text area, cut into
# page-height slices, and every slice is placed on a white A4 canvas with
# equal ("normal") margins on all four sides.
#
# Highlights
#   * Page breaks snap to a blank (near-white) row close to the nominal cut,
#     so equation blocks / figures / boxes are not sliced in half.
#   * Optional forced page count: --pages N balances the content over exactly
#     N pages (the default fills pages, using as few as fit).
#   * A final sliver of content is rebalanced into two evenly sized pages
#     instead of leaving a nearly empty last page.
#   * Pages are assembled losslessly (Flate); if cpdf is present the media
#     box of every page is normalised to exactly 595.276 x 841.89 pt (A4).
#
# Requirements: bash, coreutils, ImageMagick 7 (`magick`) or 6 (`convert`).
# Optional:     cpdf (exact A4 media box), pdfinfo (verification).
#
# Examples
#   ./longimg2a4pdf.sh sheet.png
#   ./longimg2a4pdf.sh -m 20 -d 200 sheet.png sheet-a4.pdf
#   ./longimg2a4pdf.sh --no-smart-cut -d 150 tall.png
#
set -euo pipefail

PROG=${0##*/}

# ------------------------------- defaults ------------------------------------
MARGIN_MM=25.4          # "normal" margin (1 inch / Word default) on all 4 sides
DPI=300                 # raster resolution of the output pages
BG=white                # page background / padding colour
SMART_CUT=1             # snap page breaks to blank rows when possible
WIN_PCT=15              # smart-cut search window, % of the content height
BLANK_MEAN=254          # 0-255: rows at least this bright count as "blank"
FORCE_PAGES=            # -p/--pages: requested page count (empty = automatic)
KEEP_TMP=0              # keep the generated per-page PNGs
A4_WIDTH_MM=210
A4_HEIGHT_MM=297
A4_PT_W=595.276         # exact A4 in PostScript points (210 mm)
A4_PT_H=841.89          # exact A4 in PostScript points (297 mm)

# ------------------------------- helpers -------------------------------------
die()  { printf '%s: error: %s\n'   "$PROG" "$*" >&2; exit 1; }
warn() { printf '%s: warning: %s\n' "$PROG" "$*" >&2; }
info() { printf '%s\n' "$*"; }

usage() {
	cat <<EOF
$PROG - split a long image into A4 pages and combine them into one PDF

Usage: $PROG [options] INPUT_IMAGE [OUTPUT_PDF]

The image is scaled to the width of the A4 text area (margins excluded) and
cut vertically into page-height slices.  Each slice is placed on a white A4
canvas with equal margins on all four sides.  Page breaks snap to a blank
row near the nominal cut unless --no-smart-cut is given.

By default the output uses as few pages as fit (pages are filled to the text
height).  With --pages N the content is balanced over exactly N pages, each
N-1 cuts being placed so all pages get nearly equal heights.

Options:
  -p, --pages N        use exactly N pages (balanced); --cuts N is an alias
                       (default: automatic, as few pages as fit)
  -m, --margin MM      margin on each side, in mm              (default: $MARGIN_MM)
  -d, --dpi N          page raster resolution                  (default: $DPI)
  -b, --background C   background / padding colour             (default: $BG)
  -s, --no-smart-cut   cut at exact page-height multiples
  -w, --cut-window P   smart-cut search window, % of content h (default: $WIN_PCT)
  -t, --blank-level N  row mean brightness (0-255) counted as blank
                                                               (default: $BLANK_MEAN)
  -k, --keep-temp      keep the generated per-page PNGs
  -h, --help           show this help

Examples:
  $PROG sheet.png
  $PROG -m 20 -d 200 sheet.png sheet-a4.pdf
  $PROG --no-smart-cut tall.png
EOF
}

# ------------------------------- arguments -----------------------------------
INPUT=
OUTPUT=
while (( $# )); do
	case $1 in
		-m|--margin)      [[ ${2:-} ]] || die "$1 needs a value"; MARGIN_MM=$2; shift 2 ;;
		-d|--dpi)         [[ ${2:-} ]] || die "$1 needs a value"; DPI=$2;       shift 2 ;;
		-b|--background)  [[ ${2:-} ]] || die "$1 needs a value"; BG=$2;        shift 2 ;;
		-p|--pages|--cuts) [[ ${2:-} ]] || die "$1 needs a value"; FORCE_PAGES=$2; shift 2 ;;
		-s|--no-smart-cut) SMART_CUT=0; shift ;;
		-w|--cut-window)  [[ ${2:-} ]] || die "$1 needs a value"; WIN_PCT=$2;   shift 2 ;;
		-t|--blank-level) [[ ${2:-} ]] || die "$1 needs a value"; BLANK_MEAN=$2; shift 2 ;;
		-k|--keep-temp)   KEEP_TMP=1; shift ;;
		-h|--help)        usage; exit 0 ;;
		--)               shift; while (( $# )); do
		                      if   [[ -z $INPUT  ]]; then INPUT=$1
		                      elif [[ -z $OUTPUT ]]; then OUTPUT=$1
		                      else die "unexpected extra argument: $1"; fi
		                      shift
		                  done ;;
		-*)               die "unknown option: $1 (try --help)" ;;
		*)                if   [[ -z $INPUT  ]]; then INPUT=$1
		                  elif [[ -z $OUTPUT ]]; then OUTPUT=$1
		                  else die "unexpected extra argument: $1"; fi
		                  shift ;;
	esac
done

[[ -n $INPUT ]] || { usage >&2; exit 2; }
[[ -f $INPUT ]] || die "input file not found: $INPUT"

[[ $MARGIN_MM =~ ^[0-9]+([.][0-9]+)?$ ]] || die "invalid margin: $MARGIN_MM"
[[ $WIN_PCT   =~ ^[0-9]+([.][0-9]+)?$ ]] || die "invalid cut window: $WIN_PCT"
if ! { [[ $DPI =~ ^[0-9]+$ ]] && (( DPI >= 30 )); }; then
	die "invalid dpi: $DPI"
fi
if ! { [[ $BLANK_MEAN =~ ^[0-9]+$ ]] && (( BLANK_MEAN <= 255 )); }; then
	die "invalid blank level: $BLANK_MEAN"
fi
if [[ -n $FORCE_PAGES ]]; then
	if ! { [[ $FORCE_PAGES =~ ^[0-9]+$ ]] && (( FORCE_PAGES >= 1 )); }; then
		die "invalid page count: $FORCE_PAGES"
	fi
fi

if [[ -z $OUTPUT ]]; then
	base=${INPUT##*/}
	if [[ $base == *.* && $base != .* ]]; then stem=${base%.*}; else stem=$base; fi
	dir=${INPUT%/*}; [[ $dir == "$INPUT" ]] && dir=""
	OUTPUT=${dir:+$dir/}${stem}.pdf
fi
[[ $OUTPUT != "$INPUT" ]] || die "output would overwrite the input; name the PDF explicitly"
[[ -d $(dirname -- "$OUTPUT") ]] || die "output directory does not exist: $(dirname -- "$OUTPUT")"

# ------------------------------- tool chain ----------------------------------
if command -v magick >/dev/null 2>&1; then
	MAGICK=(magick); IDENTIFY=(magick identify)
elif command -v convert >/dev/null 2>&1; then
	MAGICK=(convert); IDENTIFY=(identify)
else
	die "ImageMagick (magick or convert) not found"
fi

# ------------------------------- geometry ------------------------------------
# A4 page and margins in pixels at the chosen DPI.
read -r PW PH OFF < <(awk -v dpi="$DPI" -v m="$MARGIN_MM" \
                              -v aw="$A4_WIDTH_MM" -v ah="$A4_HEIGHT_MM" 'BEGIN{
	printf "%d %d %d\n", int(aw/25.4*dpi+0.5), int(ah/25.4*dpi+0.5), int(m/25.4*dpi+0.5)
}') || die "internal error: page geometry"
(( OFF > 0 ))            || die "margin too small for this dpi"
(( 2*OFF < PW && 2*OFF < PH )) || die "margin ${MARGIN_MM} mm too large for A4"

CW=$(( PW - 2*OFF ))    # content (text area) width
CH=$(( PH - 2*OFF ))    # content (text area) height = nominal slice height
MIN_RUN=$(( DPI / 30 )) # min. blank-run length for a "good" cut (~0.85 mm)
(( MIN_RUN < 3 )) && MIN_RUN=3
WIN=$(awk -v ch="$CH" -v p="$WIN_PCT" 'BEGIN{ printf "%d", ch*p/100+0.5 }')
(( WIN < 1 )) && WIN=1

# ------------------------------- temp dir ------------------------------------
TMP=$(mktemp -d "${TMPDIR:-/tmp}/longimg2a4pdf.XXXXXX")
# shellcheck disable=SC2329  # invoked via the EXIT trap
cleanup() { (( KEEP_TMP )) || rm -rf -- "$TMP"; }
trap cleanup EXIT
BASE=$TMP/base.png
mkdir -p "$TMP/pages"

# ------------------------------- render base ---------------------------------
read -r SRC_W SRC_H < <("${IDENTIFY[@]}" -format '%w %h\n' "${INPUT}[0]") \
	|| die "cannot read input image: $INPUT"

"${MAGICK[@]}" "${INPUT}[0]" -auto-orient \
	-background "$BG" -alpha remove -alpha off \
	-resize "${CW}x" -units PixelsPerInch -density "$DPI" \
	"$BASE"
read -r BW BH < <("${IDENTIFY[@]}" -format '%w %h\n' "$BASE") \
	|| die "internal error: base image render"
(( BW > 0 && BH > 0 )) || die "could not read the input image"

# ---------------------- smart cut: find a blank row --------------------------
# Prints a row in ]y, y+CH] close to `target` that lies in a blank run,
# or returns 1 when nothing suitable was found.
# Globals used: BASE BW BH CH WIN BLANK_MEAN MIN_RUN CUT_FLOOR
find_blank_cut() {
	local y=$1 target=$2 lo hi h cut
	lo=$(( target - WIN ))
	hi=$(( target + WIN ))
	(( lo < y + 1 ))   && lo=$(( y + 1 ))
	(( lo < CUT_FLOOR )) && lo=$CUT_FLOOR
	(( hi > y + CH ))  && hi=$(( y + CH ))
	(( hi > BH - 1 ))  && hi=$(( BH - 1 ))
	(( lo > hi )) && return 1

	h=$(( hi - lo + 1 ))
	cut=$("${MAGICK[@]}" "$BASE" \
		-crop "${BW}x${h}+0+${lo}" +repage \
		-colorspace gray -alpha off -depth 8 \
		-scale "1x${h}!" txt:- 2>/dev/null |
		awk -v lo="$lo" -v target="$target" -v thresh="$BLANK_MEAN" -v minrun="$MIN_RUN" '
		BEGIN { inrun=0; best_run=-1; best_any=-1; best_run_d=0; best_any_d=0 }
		function flush(   len, c, d) {
			if (!inrun) return
			len = rend - rstart + 1
			c = lo + int((rstart + rend) / 2 + 0.5)
			d = c - target; if (d < 0) d = -d
			if (len >= minrun) {
				if (best_run == -1 || d < best_run_d || (d == best_run_d && c > best_run)) {
					best_run = c; best_run_d = d
				}
			}
			if (best_any == -1 || d < best_any_d || (d == best_any_d && c > best_any)) {
				best_any = c; best_any_d = d
			}
			inrun = 0
		}
		/^[0-9]+,[0-9]+:/ {
			colon = index($0, ":")
			split(substr($0, 1, colon - 1), rc, ",")
			row = rc[2] + 0
			lp = index($0, "("); rp = index($0, ")")
			if (lp > 0 && rp > lp) {
				s = 0
				n = split(substr($0, lp + 1, rp - lp - 1), vv, ",")
				for (i = 1; i <= n; i++) s += vv[i] + 0
				v = s / n
				if (v >= thresh) { if (!inrun) { inrun = 1; rstart = row }; rend = row }
				else flush()
			}
		}
		END { flush(); print (best_run != -1) ? best_run : ((best_any != -1) ? best_any : -1) }
	')
	[[ $cut =~ ^[0-9]+$ ]] && (( cut > y && cut <= y + CH )) && printf '%s\n' "$cut"
}

# ------------------------------- pagination ----------------------------------
MIN_PAGES=$(( (BH + CH - 1) / CH ))     # fewest pages that can hold the content
if [[ -n $FORCE_PAGES ]]; then
	N=$FORCE_PAGES
	(( N <= BH )) || die "cannot make ${N} pages from an image ${BH} px tall"
	(( N >= MIN_PAGES )) || die "cannot fit ${BH} px of content into ${N} page(s); the minimum is ${MIN_PAGES} page(s)"
else
	N=$MIN_PAGES
fi

BALANCED=0
BOUNDS=()
if (( N == MIN_PAGES )); then
	# Fill every page up to the content height; the remainder goes on the
	# last page (if that would be a sliver, the last two pages share evenly).
	for (( k = 1; k < N; k++ )); do BOUNDS+=( $(( k * CH )) ); done
	R=$(( BH - (N - 1) * CH ))      # height of the nominal last page (1..CH)
	if (( N > 1 && R * 3 < CH )); then
		idx=$(( N - 2 ))
		BOUNDS[idx]=$(( idx * CH + (CH + R) / 2 ))
	fi
else
	# User-requested page count: balance the content over N almost equal pages.
	BALANCED=1
	for (( k = 1; k < N; k++ )); do BOUNDS+=( $(( k * BH / N )) ); done
fi

# Phase 1: decide the actual cuts (smart cut / no smart cut).
PAGE_TOP=(); PAGE_BOT=(); PAGE_SNAP=()
y=0
for (( k = 0; k < N - 1; k++ )); do
	target=${BOUNDS[k]}
	(( target > y + CH )) && target=$(( y + CH ))
	(( target < y + 1  )) && target=$(( y + 1 ))
	cut=$target
	note=""
	if (( SMART_CUT )); then
		if (( k == N - 2 )); then CUT_FLOOR=$(( BH - CH )); else CUT_FLOOR=1; fi
		if c=$(find_blank_cut "$y" "$target"); then
			cut=$c
			(( cut != target )) && note="   (snapped to a blank line)"
		fi
	fi
	PAGE_TOP+=("$y"); PAGE_BOT+=("$cut"); PAGE_SNAP+=("$note")
	y=$cut
done
if (( BH > y )); then
	PAGE_TOP+=("$y"); PAGE_BOT+=("$BH"); PAGE_SNAP+=("")
fi
NPAGES=${#PAGE_TOP[@]}

# ------------------------------- plan output ---------------------------------
SCALE=$(awk -v a="$BW" -v b="$SRC_W" 'BEGIN{ printf "%.3f", a/b }')
info "Input : ${INPUT}  (${SRC_W} x ${SRC_H} px)"
info "Page  : A4 ${A4_WIDTH_MM} x ${A4_HEIGHT_MM} mm @ ${DPI} dpi -> ${PW} x ${PH} px"
info "        margin ${MARGIN_MM} mm on all four sides"
info "Scaled: fitted to text width -> ${BW} x ${BH} px  (x${SCALE})"
pg_note=""
if (( BALANCED )); then pg_note="   (balanced equal pages, as requested)"; fi
info "Pages : ${NPAGES}${pg_note}"
for (( i = 0; i < NPAGES; i++ )); do
	printf '  page %d: rows %6d .. %-6d  (height %5d px)%s\n' \
		"$(( i + 1 ))" "${PAGE_TOP[i]}" "${PAGE_BOT[i]}" \
		"$(( PAGE_BOT[i] - PAGE_TOP[i] ))" "${PAGE_SNAP[i]}"
done
info ""

# ------------------------------- render pages --------------------------------
make_page() {   # $1 top row, $2 bottom row (exclusive), $3 page index
	local top=$1 bot=$2 idx=$3 sh out
	sh=$(( bot - top ))
	(( sh > 0 && sh <= CH )) || die "internal error: bad slice height ${sh}"
	printf -v out '%s/pages/page-%03d.png' "$TMP" "$idx"
	"${MAGICK[@]}" -size "${PW}x${PH}" "xc:$BG" \
		\( "$BASE" -crop "${BW}x${sh}+0+${top}" +repage \
			-background "$BG" -gravity north -extent "${BW}x${CH}" \) \
		-gravity northwest -geometry "+${OFF}+${OFF}" -composite \
		"$out"
}

for (( i = 0; i < NPAGES; i++ )); do
	make_page "${PAGE_TOP[i]}" "${PAGE_BOT[i]}" "$i"
done

PAGES=("$TMP"/pages/page-*.png)
((${#PAGES[@]} == NPAGES)) || die "internal error: ${#PAGES[@]} page images for ${NPAGES} pages"

# ------------------------------- combine PDF ---------------------------------
"${MAGICK[@]}" "${PAGES[@]}" \
	-units PixelsPerInch -density "$DPI" -compress Zip \
	"$OUTPUT"

# Optional: make the media box exactly A4 (595.276 x 841.89 pt).
if command -v cpdf >/dev/null 2>&1; then
	if cpdf -mediabox "0 0 $A4_PT_W $A4_PT_H" "$OUTPUT" -o "$TMP/exact-a4.pdf" \
			2>"$TMP/cpdf.log"; then
		mv -f -- "$TMP/exact-a4.pdf" "$OUTPUT"
	else
		warn "cpdf media-box step failed; keeping the ImageMagick page size (A4 class)"
	fi
fi

# ------------------------------- summary -------------------------------------
info "Wrote : ${OUTPUT}  ($(du -h -- "$OUTPUT" 2>/dev/null | cut -f1))"
info "        ${NPAGES} page(s), A4 ${A4_WIDTH_MM} x ${A4_HEIGHT_MM} mm, ${MARGIN_MM} mm margins"
if command -v pdfinfo >/dev/null 2>&1; then
	ver=$(pdfinfo -- "$OUTPUT" 2>/dev/null || true)
	if [[ -n $ver ]]; then
		info "Verify: $(awk -F': *' '/^Pages:/{printf "pages=%s  ", $2}
		                             /^Page size:/{printf "page size=%s", $2}' <<<"$ver")"
	fi
fi
(( KEEP_TMP )) && info "Temp  : ${TMP} (kept, per-page PNGs in ${TMP}/pages)"
exit 0
