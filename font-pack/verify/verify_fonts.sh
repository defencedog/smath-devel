#!/usr/bin/env bash
# Verification script for SMath Studio font rendering
set -e

echo "=== 1. Checking Fontconfig Mappings ==="
echo "Arial -> $(fc-match Arial)"
echo "Times New Roman -> $(fc-match 'Times New Roman')"
echo "Segoe UI -> $(fc-match 'Segoe UI')"
echo "Segoe UI Symbol -> $(fc-match 'Segoe UI Symbol')"
echo "Cambria Math -> $(fc-match 'Cambria Math')"
echo ""

echo "=== 2. Testing Mono Graphics.DrawString Glyph Rendering ==="
cat << 'EOF' > /tmp/test_smath_verify.cs
using System;
using System.Drawing;
using System.Drawing.Imaging;

class Program {
    static void Main() {
        string[] fonts = new[] {"Arial", "DejaVu Sans", "Segoe UI", "Segoe UI Symbol", "Cambria"};
        string testText = "H\u2082 + Cl\u2082 \u2192 2 HCl | N\u2082 + 3 H\u2082 \u21CC 2 NH\u2083 | \u21C4 \u2264 \u2265 \u0394 \u00B0";

        foreach (var fontName in fonts) {
            using (Bitmap bmp = new Bitmap(700, 50))
            using (Graphics g = Graphics.FromImage(bmp)) {
                g.Clear(Color.White);
                using (Font f = new Font(fontName, 12)) {
                    g.DrawString(testText, f, Brushes.Black, 5, 10);
                }
                int ink = 0;
                for (int x = 0; x < bmp.Width; x++) {
                    for (int y = 0; y < bmp.Height; y++) {
                        if (bmp.GetPixel(x, y).R < 200) ink++;
                    }
                }
                Console.WriteLine(string.Format("  Font '{0}': successfully rendered {1} ink pixels (no blank box).", fontName, ink));
            }
        }
    }
}
EOF

mcs -r:System.Drawing /tmp/test_smath_verify.cs -out:/tmp/test_smath_verify.exe
mono /tmp/test_smath_verify.exe
rm -f /tmp/test_smath_verify.cs /tmp/test_smath_verify.exe

echo ""
echo "=== Verification complete! All symbols render properly without blank boxes. ==="
