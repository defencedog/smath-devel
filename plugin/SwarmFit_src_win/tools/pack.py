#!/usr/bin/env python3
"""
Pack an SMath Studio plugin into an installable .zip.

SMath's package reader derives each entry's stream length from the local header
without honouring the compression method, so a DEFLATE entry produces a negative
length and installation fails with:

    Stream length must be non-negative and less than 2^31 - 1 - origin

Every entry is therefore written with ZIP_STORED, and the archive is re-opened
afterwards to assert compress_type == 0 on all of them.

Usage:
    python pack.py <staging_folder> <output.zip>
"""

import os
import sys
import zipfile


def pack(stage_dir, out_zip):
    stage_dir = os.path.abspath(stage_dir)
    out_zip = os.path.abspath(out_zip)

    if not os.path.isdir(stage_dir):
        raise SystemExit("staging folder not found: " + stage_dir)

    entries = []
    for root, dirs, files in os.walk(stage_dir):
        dirs.sort()
        for name in sorted(files):
            full = os.path.join(root, name)
            arc = os.path.relpath(full, stage_dir).replace(os.sep, "/")
            entries.append((full, arc))

    if not entries:
        raise SystemExit("nothing to pack in " + stage_dir)

    os.makedirs(os.path.dirname(out_zip) or ".", exist_ok=True)
    if os.path.exists(out_zip):
        os.remove(out_zip)

    with zipfile.ZipFile(out_zip, "w", compression=zipfile.ZIP_STORED) as z:
        for full, arc in entries:
            with open(full, "rb") as f:
                z.writestr(arc, f.read())

    # Verify: STORED only, and the archive reads back cleanly.
    with zipfile.ZipFile(out_zip) as z:
        bad = [i.filename for i in z.infolist() if i.compress_type != zipfile.ZIP_STORED]
        if bad:
            raise SystemExit("refusing to ship: deflated entries present: " + ", ".join(bad))
        broken = z.testzip()
        if broken:
            raise SystemExit("corrupt entry: " + broken)

        print("wrote %s (%d bytes)" % (out_zip, os.path.getsize(out_zip)))
        for i in z.infolist():
            print("  STORED  %-40s %9d" % (i.filename, i.file_size))


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("usage: pack.py <staging_folder> <output.zip>")
    pack(sys.argv[1], sys.argv[2])
