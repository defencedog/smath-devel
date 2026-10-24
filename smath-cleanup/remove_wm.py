#!/usr/bin/env python3
"""
remove_wm.py -- strip the SMath Studio free-version watermarks from a PDF.

SMath Studio's free export stamps two raster watermarks onto every page:

  TOP     "Created using a free version of SMath Studio"
  BOTTOM  "Not for commercial use"

They are image XObjects drawn in a tiny grey palette:

  #D3D3D3  light grey   (glyph fill)
  #2F4F4F  dark slate   (glyph outline)
  #696969  dim grey
  #A9A9A9  dark grey    (only used as antialias fringe)

Two things make this trickier than just deleting the draw operators:

1. The running header ("<date> - <file>.sm") is *also* a grey raster image, and
   it sits directly above the top watermark. It must survive. It is drawn in
   #A9A9A9 only, and the handful of dim-grey antialias pixels it does contain
   cover well under 0.1 % of its area -- far below the density of real
   watermark glyphs -- so palette plus density separate the two.

2. Where worksheet content overlaps the watermark band, SMath rasterises the
   overlap into a single bitmap that contains the watermark *and* real content
   (a formula box border, tops of glyphs, ...). Deleting those draws would erase
   content, so their watermark-coloured pixels are inpainted from the
   surrounding pixels instead; the content pixels are left untouched.

Pure watermark images are still removed by deleting their
`q <a> 0 0 <d> <e> <f> cm /Rxx Do Q` block, so no white boxes are painted over
the page. Unreferenced images are then dropped by the `garbage=3` save.

Usage:
    python3 remove_wm.py [input.pdf] [output.pdf]

Defaults:
    input  = sample.pdf
    output = <input>_clean.pdf

Requires PyMuPDF, NumPy and SciPy (`pip install pymupdf numpy scipy`).
"""

import argparse
import hashlib
import re
import subprocess
import sys

import numpy as np
import pymupdf
from scipy import ndimage

# Colours used by the watermark glyphs (RGB).
PALETTE = (
    (211, 211, 211),  # lightgrey  -- glyph fill
    (47, 79, 79),     # darkslategrey -- glyph outline
    (105, 105, 105),  # dimgrey
    (169, 169, 169),  # darkgrey -- antialias fringe only
)
# Colours that positively identify a watermark. The running header is drawn in
# darkgrey (#A9A9A9) alone, so it is deliberately excluded here.
SIGNATURE = ((211, 211, 211), (47, 79, 79), (105, 105, 105))


# SMath's rasteriser emits almost, but not exactly, those named colours: a
# colour-space round trip can shift them by a couple of levels (#D3D3D3 becomes
# #D5D3D2 in some exports). Match with a tolerance instead of exactly.
TOLERANCE = 6
PALETTE_ARRAY = np.array(PALETTE, dtype=np.int16)
SIGNATURE_ARRAY = np.array(SIGNATURE, dtype=np.int16)

# A `q <6 numbers> cm /Name Do Q` image-draw block.
DRAW_RE = re.compile(
    r"q\s+(?:[-+]?\d*\.?\d+\s+){6}cm\s*/([A-Za-z0-9#_.+-]+)\s+Do\s*Q"
)


def rgb_array(doc, xref):
    """Return (H x W x 3 uint8 array, pixmap) for image `xref`."""
    pix = pymupdf.Pixmap(doc, xref)
    if pix.alpha:
        pix = pymupdf.Pixmap(pix, 0)  # drop alpha channel
    if pix.colorspace is None or pix.n != 3:
        pix = pymupdf.Pixmap(pymupdf.csRGB, pix)
    arr = np.frombuffer(pix.samples, np.uint8).reshape(pix.height, pix.width, 3)
    return arr, pix


def match_colours(arr, colours):
    """Boolean mask of pixels within TOLERANCE of any of `colours` (RGB)."""
    diff = np.abs(arr.astype(np.int16)[:, :, None, :] - colours[None, None, :, :])
    return np.all(diff <= TOLERANCE, axis=3)


def analyse(arr):
    """Return (signature_pixels, signature_ratio, pure, palette_mask).

    `pure` means the bitmap holds nothing but watermark greys on white -- it can
    be dropped wholesale. Anything else needs its watermark pixels inpainted.
    """
    palette = match_colours(arr, PALETTE_ARRAY)
    is_palette = palette.any(axis=2)
    signature = int(match_colours(arr, SIGNATURE_ARRAY).any(axis=2).sum())
    white = np.all(arr >= 250, axis=2)
    non_white = ~white
    pure = bool(non_white.any()) and not np.any(non_white & ~is_palette)
    ratio = signature / float(arr.shape[0] * arr.shape[1])
    return signature, ratio, pure, is_palette


def _shift_slices(dy, dx, height, width):
    """Slices for dst[y, x] = src[y + dy, x + dx], clipped to the image."""
    if dy >= 0:
        dst_y, src_y = slice(0, height - dy), slice(dy, height)
    else:
        dst_y, src_y = slice(-dy, height), slice(0, height + dy)
    if dx >= 0:
        dst_x, src_x = slice(0, width - dx), slice(dx, width)
    else:
        dst_x, src_x = slice(-dx, width), slice(0, width + dx)
    return dst_y, dst_x, src_y, src_x


def inpaint(arr, mask, radius=10):
    """Fill masked pixels from their surroundings.

    For every masked pixel the nearest unmasked pixel is sampled along eight ray
    directions. If the horizontal pair agrees on a colour (or the vertical pair
    does), that colour wins: along a box border both sides are black, in open
    background both sides are white/yellow. This keeps rules continuous where a
    glyph crossed them instead of pulling border ink into the white beside it.
    Remaining pixels fall back to a distance-weighted vote over all eight rays.
    """
    height, width = mask.shape
    valid = ~mask
    directions = [(-1, 0), (1, 0), (0, -1), (0, 1),
                  (-1, -1), (-1, 1), (1, -1), (1, 1)]

    found = np.zeros((len(directions), height, width, 3), np.uint8)
    seen = np.zeros((len(directions), height, width), bool)
    weight = np.zeros((len(directions), height, width), np.float32)

    for k, (dy, dx) in enumerate(directions):
        for step in range(1, radius + 1):
            if abs(dy) * step >= height or abs(dx) * step >= width:
                break
            dst_y, dst_x, src_y, src_x = _shift_slices(
                dy * step, dx * step, height, width
            )
            take = mask[dst_y, dst_x] & ~seen[k][dst_y, dst_x] \
                & valid[src_y, src_x]
            if not take.any():
                continue
            found[k][dst_y, dst_x][take] = arr[src_y, src_x][take]
            seen[k][dst_y, dst_x][take] = True
            weight[k][dst_y, dst_x][take] = 1.0 / step

    # First choice: a colour agreed on by both sides of a cross-section. Along
    # a horizontal rule the left and right neighbours both see black; in open
    # background they both see white/yellow. This keeps rules continuous and
    # avoids pulling a rule's ink into the space beside it.
    left, right, up, down = 2, 3, 0, 1
    h_ok = seen[left] & seen[right] & np.all(found[left] == found[right], axis=2)
    v_ok = seen[up] & seen[down] & np.all(found[up] == found[down], axis=2)

    chosen = np.zeros((height, width, 3), np.uint8)
    chosen[h_ok] = found[left][h_ok]
    v_only = v_ok & ~h_ok
    chosen[v_only] = found[up][v_only]

    # Otherwise fall back to a distance-weighted vote over all eight rays.
    rest = mask & ~(h_ok | v_ok)
    if rest.any():
        score = np.zeros((len(directions), height, width), np.float32)
        for k in range(len(directions)):
            same = np.all(found == found[k], axis=3) & seen & seen[k]
            score[k] = (weight * same).sum(axis=0)
        best = np.argmax(score, axis=0)
        ys, xs = np.nonzero(rest)
        chosen[ys, xs] = found[best[ys, xs], ys, xs]

    out = arr.copy()
    ys, xs = np.nonzero(mask)
    out[ys, xs] = chosen[ys, xs]

    # Safety net: anything too far from valid pixels falls back to a plain
    # nearest-neighbour fill.
    orphan = mask & ~(h_ok | v_ok) & ~seen.any(axis=0)
    if orphan.any():
        idx = ndimage.distance_transform_edt(
            orphan, return_distances=False, return_indices=True
        )
        out[orphan] = out[idx[0][orphan], idx[1][orphan]]
    return out


# An image counts as watermark-bearing only if the watermark's own greys make up
# a real share of it. The running-header bitmaps contain a few antialiasing
# pixels in dim grey (< 0.1 % of their area) and must not be mistaken for one.
MIN_SIGNATURE_RATIO = 0.001


class PageImages:
    """Images drawn on a page, with their placement and pixel statistics."""

    def __init__(self, doc, page):
        self.items = []
        self.signature = {}
        self.signature_ratio = {}
        self.pure = {}
        self.arrays = {}

        seen = set()
        for info in page.get_image_info(xrefs=True):
            xref = info["xref"]
            # xref 0 is an inline image; it is never an SMath watermark.
            if not xref or xref in seen:
                continue
            seen.add(xref)
            self.items.append((xref, pymupdf.Rect(info["bbox"])))
            try:
                arr, _pix = rgb_array(doc, xref)
            except Exception:
                self.signature[xref] = 0
                self.signature_ratio[xref] = 0.0
                self.pure[xref] = False
                continue
            self.arrays[xref] = arr
            signature, ratio, pure, _mask = analyse(arr)
            self.signature[xref] = signature
            self.signature_ratio[xref] = ratio
            self.pure[xref] = pure

    def __iter__(self):
        return iter(self.items)


def find_bands(page, info):
    """Locate the top and bottom watermark bands on a page.

    Bands are seeded from images that are nothing but the grey watermark palette,
    then grown to cover adjacent bitmaps that SMath flattened the watermark into
    (those still carry watermark-coloured pixels, but also real content).
    """
    height = page.rect.height
    seeds = []
    for xref, rect in info:
        if (info.pure[xref] and info.signature[xref] >= 20
                and info.signature_ratio[xref] >= MIN_SIGNATURE_RATIO):
            centre = (rect.y0 + rect.y1) / 2
            if centre < height * 0.20 or centre > height * 0.80:
                seeds.append(rect)

    bands = []
    for seed in seeds:
        band = pymupdf.Rect(seed)
        grown = True
        while grown:
            grown = False
            for xref, rect in info:
                if rect in band:
                    continue
                if (info.signature[xref] < 20
                        or info.signature_ratio[xref] < MIN_SIGNATURE_RATIO):
                    continue
                if (rect + (-1.5, -1.5, 1.5, 1.5)).intersects(band):
                    band |= rect
                    grown = True
        if not any(abs(band.y0 - b.y0) < 2 and abs(band.y1 - b.y1) < 2
                   for b in bands):
            bands.append(band)
    return bands


def process_page(doc, page, xref_to_name, verbose):
    """Remove watermark draws and clean flattened bitmaps on one page."""
    info = PageImages(doc, page)
    bands = find_bands(page, info)

    delete_names = set()
    cleaned = 0
    for band in bands:
        for xref, rect in info:
            if (rect not in band or info.signature[xref] < 20
                    or info.signature_ratio[xref] < MIN_SIGNATURE_RATIO):
                continue
            if info.pure[xref]:
                name = xref_to_name.get(xref)
                if name:
                    delete_names.add(name)
                if verbose:
                    print(f"    xref {xref}: pure watermark -> removed")
            else:
                arr = info.arrays[xref]
                mask = match_colours(arr, PALETTE_ARRAY).any(axis=2)
                if not mask.any():
                    continue
                fixed = inpaint(arr, mask)
                new_pix = pymupdf.Pixmap(
                    pymupdf.csRGB, arr.shape[1], arr.shape[0], fixed.tobytes(), 0
                )
                page.replace_image(xref, pixmap=new_pix)
                cleaned += 1
                if verbose:
                    print(
                        f"    xref {xref}: {int(mask.sum())} watermark pixels "
                        f"inpainted (content kept)"
                    )

    removed = 0
    if delete_names:
        stream = page.read_contents().decode("latin-1")

        def drop(match):
            nonlocal removed
            if match.group(1) in delete_names:
                removed += 1
                return ""
            return match.group(0)

        new_stream = DRAW_RE.sub(drop, stream)
        cxrefs = page.get_contents()
        doc.update_stream(cxrefs[0], new_stream.encode("latin-1"), compress=True)
        # `replace_image()` appends a helper content stream; the whole edited
        # content now lives in the first stream, so blank any extras.
        for extra in cxrefs[1:]:
            doc.update_stream(extra, b" ", compress=True)
    return removed, cleaned


def _image_fingerprint(doc, xref):
    arr, _pix = rgb_array(doc, xref)
    return hashlib.sha1(arr.tobytes()).hexdigest()


def _distinct_colours(arr):
    packed = (
        (arr[:, :, 0].astype(np.uint32) << 16)
        | (arr[:, :, 1].astype(np.uint32) << 8)
        | arr[:, :, 2]
    )
    return int(np.unique(packed).size)


def _bands(page):
    """Top and bottom 10 % of the page, where the watermarks live."""
    height = page.rect.height
    width = page.rect.width
    return (
        pymupdf.Rect(0, 0, width, height * 0.10),
        pymupdf.Rect(0, height * 0.90, width, height),
    )


def _ocr_band_text(page, rect, dpi=300):
    """OCR a band; returns None when tesseract is unavailable."""
    png = "/tmp/smath_cleanup_selftest.png"
    page.get_pixmap(dpi=dpi, clip=rect).save(png)
    try:
        out = subprocess.run(
            ["tesseract", png, "-", "--psm", "6"],
            capture_output=True, text=True, check=False,
        )
    except FileNotFoundError:
        return None
    return " ".join(out.stdout.split())


def verify(src, dst):
    """Self-test: confirm `dst` is a cleaned copy of `src`.

    Checks that watermarks are gone, that the running headers and page numbers
    survived, that the searchable text is unchanged and that every image outside
    the watermark bands is still present byte-for-byte. Returns True on success.
    """
    src_doc = pymupdf.open(src)
    dst_doc = pymupdf.open(dst)
    results = []

    def check(name, ok, detail=""):
        results.append((name, bool(ok), detail))

    check("page count", src_doc.page_count == dst_doc.page_count,
          f"{src_doc.page_count} -> {dst_doc.page_count}")

    same_text = all(
        " ".join(a.get_text().split()) == " ".join(b.get_text().split())
        for a, b in zip(src_doc, dst_doc)
    )
    check("searchable text unchanged", same_text)

    leftover = 0
    for page in dst_doc:
        info = PageImages(dst_doc, page)
        bands = _bands(page)
        for xref, rect in info:
            if not any(rect.intersects(band) for band in bands):
                continue
            # Charts reach into the margins and are full of grey gridlines, so
            # only near-flat bitmaps (the watermark is a handful of colours) are
            # judged on their palette.
            if _distinct_colours(info.arrays[xref]) > 8:
                continue
            if (info.signature[xref] >= 20
                    and info.signature_ratio[xref] >= MIN_SIGNATURE_RATIO):
                leftover += info.signature[xref]
    check("watermark pixels gone from top/bottom bands", leftover == 0,
          f"{leftover} signature pixel(s) left")

    ocr_checked = ocr_failed = 0
    for page in dst_doc:
        top = pymupdf.Rect(0, 0, page.rect.width, 60)
        bottom = pymupdf.Rect(0, page.rect.height - 50,
                              page.rect.width, page.rect.height)
        for rect in (top, bottom):
            text = _ocr_band_text(page, rect)
            if text is None:
                continue
            ocr_checked += 1
            low = text.lower()
            # Match the watermark wording, not the file name in the header
            # (a source called "smath-demo.sm" would otherwise false-positive).
            if ("commercial" in low or "free version" in low
                    or "created using" in low):
                ocr_failed += 1
    if ocr_checked:
        check("OCR of watermark bands", ocr_failed == 0,
              f"{ocr_checked} band(s) read")
    else:
        check("OCR of watermark bands", True, "tesseract not installed - skipped")

    missing_headers = []
    for page in dst_doc:
        info = PageImages(dst_doc, page)
        top_band = _bands(page)[0]
        has_header = any(
            rect.intersects(top_band)
            and info.signature_ratio[xref] < MIN_SIGNATURE_RATIO
            for xref, rect in info
        )
        if not has_header:
            missing_headers.append(page.number + 1)
    check("running header present on every page", not missing_headers,
          f"missing on page(s) {missing_headers}" if missing_headers else "")

    changed = []
    for page_a, page_b in zip(src_doc, dst_doc):
        bands = _bands(page_a)
        outside = lambda r: not any(r.intersects(b) for b in bands)
        before = {(round(r.x0), round(r.y0), round(r.x1), round(r.y1),
                   _image_fingerprint(src_doc, x))
                  for x, r in PageImages(src_doc, page_a) if outside(r)}
        after = {(round(r.x0), round(r.y0), round(r.x1), round(r.y1),
                  _image_fingerprint(dst_doc, x))
                 for x, r in PageImages(dst_doc, page_b) if outside(r)}
        if before != after:
            changed.append(page_a.number + 1)
    check("body images preserved", not changed,
          f"changed on page(s) {changed}" if changed else "")

    width = max(len(name) for name, _ok, _detail in results)
    failed = 0
    for name, ok, detail in results:
        if not ok:
            failed += 1
        suffix = f" ({detail})" if detail else ""
        print(f"  [{'PASS' if ok else 'FAIL'}] {name:<{width}}{suffix}")
    src_doc.close()
    dst_doc.close()
    return failed == 0


def remove_watermarks(src, dst, verbose=False):
    doc = pymupdf.open(src)
    total_draws = 0
    total_bitmaps = 0

    for page in doc:
        xref_to_name = {}
        for image in page.get_images(full=True):
            xref, name = image[0], image[7]
            if name:
                xref_to_name.setdefault(xref, name)
        if verbose:
            print(f"  page {page.number + 1}")
        removed, cleaned = process_page(doc, page, xref_to_name, verbose)
        total_draws += removed
        total_bitmaps += cleaned

    if not total_draws and not total_bitmaps:
        print("WARNING: no watermarks detected; output is unchanged.", file=sys.stderr)

    doc.save(dst, garbage=3, deflate=True)
    doc.close()
    return total_draws, total_bitmaps


def main():
    parser = argparse.ArgumentParser(
        description="Remove the SMath Studio watermarks from a PDF."
    )
    parser.add_argument("input", nargs="?", default="sample.pdf",
                        help="source PDF (default: sample.pdf)")
    parser.add_argument("output", nargs="?",
                        help="output PDF (default: <input>_clean.pdf)")
    parser.add_argument("-v", "--verbose", action="store_true",
                        help="report every image touched")
    parser.add_argument("--verify", action="store_true",
                        help="check an already-cleaned OUTPUT instead of cleaning")
    args = parser.parse_args()

    dst = args.output or re.sub(r"\.pdf$", "", args.input, flags=re.I) + "_clean.pdf"

    if args.verify:
        print(f"self-test: {args.input} -> {dst}")
        if not verify(args.input, dst):
            print("FAIL: output is not clean", file=sys.stderr)
            sys.exit(1)
        print("PASS: output looks clean")
        return

    try:
        draws, bitmaps = remove_watermarks(args.input, dst, args.verbose)
    except FileNotFoundError:
        parser.error(f"input not found: {args.input}")
    print(
        f"{args.input} -> {dst}: removed {draws} watermark image(s), "
        f"cleaned {bitmaps} flattened bitmap(s)"
    )


if __name__ == "__main__":
    main()
