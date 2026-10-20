"""
Package an SMath Studio plugin folder into an installable .zip.

CRITICAL: SMath's plugin installer reads the zip with a streaming reader that
chokes on DEFLATE entries (error: "Stream length must be non-negative and less
than 2^31 - 1 - origin"). Every entry MUST be stored uncompressed
(ZIP_STORED / compress_type == 0). This script enforces that.

Usage:
    python _pack.py <source_folder> <output_zip>
"""

import os
import sys
import zipfile


def pack(src_dir, out_zip):
    if not os.path.isdir(src_dir):
        sys.exit("source folder not found: %s" % src_dir)

    # Collect files in a stable, deterministic order.
    entries = []
    for root, dirs, files in os.walk(src_dir):
        dirs.sort()
        for name in sorted(files):
            full = os.path.join(root, name)
            rel = os.path.relpath(full, src_dir).replace(os.sep, "/")
            entries.append((full, rel))
    entries.sort(key=lambda e: e[1])

    if not entries:
        sys.exit("source folder is empty: %s" % src_dir)

    with zipfile.ZipFile(out_zip, "w", zipfile.ZIP_STORED) as z:
        for full, rel in entries:
            z.write(full, rel)

    # Verify: every entry must be STORED, and the archive must be readable.
    with zipfile.ZipFile(out_zip) as z:
        bad = z.testzip()
        if bad is not None:
            sys.exit("corrupt entry: %s" % bad)
        for info in z.infolist():
            if info.compress_type != zipfile.ZIP_STORED:
                sys.exit("entry %s is not STORED" % info.filename)
        names = [i.filename for i in z.infolist()]

    print("wrote %s (%d bytes)" % (out_zip, os.path.getsize(out_zip)))
    for n in names:
        print("  STORED  %s" % n)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    pack(sys.argv[1], sys.argv[2])
