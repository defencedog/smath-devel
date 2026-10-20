#!/usr/bin/env python3
"""Re-apply the SMath Studio (Mono) blank-print / Print-Preview fix in place.

Change: SMath.Viewer.Printing::InitPrinting in SMath.Document.dll, guard
    `PaperId > -1`  ->  `PaperId > 0`
IL: ldc.i4.m1 (0x15) -> ldc.i4.0 (0x16) at file offset 0x22BA of v1.75.9678.

Effect: paper format "Custom" (PaperId = 0) skips the printer-paper scan and uses
the document's own page size as a custom PaperSize, instead of adopting Mono's
bogus zero-sized 'Custom' paper entry (which produced 0x0 PostScript pages =
blank output and GDI+ InvalidParameter in Print Preview).

Usage:  python3 apply-patch.py [path/to/SMath.Document.dll]
Default: ~/Binaries/SMathStudioDesktop.1_5_0_9678.Mono/SMath.Document.dll
A .bak-YYYYMMDD-blankprint backup is created next to the DLL (first run only).
"""
import hashlib
import os
import shutil
import sys
import time

DEFAULT = os.path.expanduser(
    "~/Binaries/SMathStudioDesktop.1_5_0_9678.Mono/SMath.Document.dll")


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT
    if not os.path.exists(path):
        print("not found:", path)
        return 1

    data = bytearray(open(path, "rb").read())
    # Locate:  ... 6F <tok4>  15 31 6C  02 7B ...
    #          callvirt get_PaperId; ldc.i4.m1; ble.s; ldarg.0; ldfld
    cands = [
        i for i in range(5, len(data) - 4)
        if data[i] == 0x15 and data[i + 1] == 0x31 and data[i + 2] == 0x6C
        and data[i - 5] == 0x6F and data[i + 3] == 0x02 and data[i + 4] == 0x7B
    ]
    if len(cands) == 0:
        print("pattern not found - already patched, or a different SMath version.")
        return 2
    if len(cands) > 1:
        print("pattern not unique, aborting. candidates:", [hex(c) for c in cands])
        return 3

    off = cands[0]
    bak = path + ".bak-" + time.strftime("%Y%m%d") + "-blankprint"
    if os.path.exists(bak):
        print("backup already exists:", bak)
    else:
        shutil.copy2(path, bak)
        print("backup created:", bak)

    data[off] = 0x16  # ldc.i4.m1 -> ldc.i4.0
    with open(path, "wb") as f:
        f.write(bytes(data))
    print("patched at file offset:", hex(off))
    print("sha256:", hashlib.sha256(open(path, "rb").read()).hexdigest())
    return 0


if __name__ == "__main__":
    sys.exit(main())
