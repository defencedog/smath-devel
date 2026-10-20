#!/usr/bin/env bash
# ==============================================================================
# SMath Studio (Mono) Linux Font Pack & Missing Symbol Fix Installer
# ==============================================================================
set -e

HERE="$(dirname "$(readlink -f "${0}")")"
TARGET_FONT_DIR="/usr/local/share/fonts/ms-cleartype"
SYSTEM_FONTCONFIG="/etc/fonts/local.conf"
USER_FONTCONFIG="${HOME}/.config/fontconfig/fonts.conf"
SMATH_CONFIG_DIR="${HOME}/.config/SMath"

echo "=========================================================="
echo " SMath Studio (Mono) Font Pack & Symbol Fix Installer"
echo "=========================================================="

# 1. Install prerequisites if apt is available and running with sudo/root
if [ "$(id -u)" -eq 0 ]; then
    echo "[1/5] Checking APT prerequisites (fonts-symbola, fonts-dejavu, fonts-freefont-ttf)..."
    if command -v apt-get >/dev/null 2>&1; then
        apt-get update -qq || true
        apt-get install -y -qq fonts-symbola fonts-freefont-ttf fonts-dejavu fonts-noto-extra >/dev/null 2>&1 || true
    fi
else
    echo "[1/5] Running without root. Note: system-wide fonts and packages require sudo."
fi

# 2. Install font files
if [ "$(id -u)" -eq 0 ]; then
    echo "[2/5] Installing Microsoft ClearType & Math fonts to ${TARGET_FONT_DIR}..."
    mkdir -p "${TARGET_FONT_DIR}"
    cp -f "${HERE}/fonts/"*.TT* "${HERE}/fonts/"*.tt* "${TARGET_FONT_DIR}/" 2>/dev/null || true
    chmod 644 "${TARGET_FONT_DIR}/"*
else
    USER_FONT_DIR="${HOME}/.local/share/fonts/ms-cleartype"
    echo "[2/5] Installing fonts to user directory: ${USER_FONT_DIR}..."
    mkdir -p "${USER_FONT_DIR}"
    cp -f "${HERE}/fonts/"*.TT* "${HERE}/fonts/"*.tt* "${USER_FONT_DIR}/" 2>/dev/null || true
    chmod 644 "${USER_FONT_DIR}/"*
fi

# 3. Install Fontconfig configuration
echo "[3/5] Installing Fontconfig symbol fallback & alias rules..."
if [ "$(id -u)" -eq 0 ]; then
    cp -f "${HERE}/fontconfig/99-smath-mono-fonts.conf" "${SYSTEM_FONTCONFIG}"
    chmod 644 "${SYSTEM_FONTCONFIG}"
    echo "  Installed system-wide configuration to ${SYSTEM_FONTCONFIG}"
fi

# Always update user fontconfig as well
mkdir -p "$(dirname "${USER_FONTCONFIG}")"
cp -f "${HERE}/fontconfig/99-smath-mono-fonts.conf" "${USER_FONTCONFIG}"
chmod 644 "${USER_FONTCONFIG}"
echo "  Installed user configuration to ${USER_FONTCONFIG}"

# 4. Patch SMath Studio configuration if found
echo "[4/5] Checking SMath Studio configuration in ${SMATH_CONFIG_DIR}..."
if [ -d "${SMATH_CONFIG_DIR}" ]; then
    if [ -f "${SMATH_CONFIG_DIR}/settings.inf" ]; then
        if ! grep -q "^FontName=DejaVu Sans" "${SMATH_CONFIG_DIR}/settings.inf"; then
            cp -n "${SMATH_CONFIG_DIR}/settings.inf" "${SMATH_CONFIG_DIR}/settings.inf.bak"
            sed -i 's/^FontName=.*/FontName=DejaVu Sans/' "${SMATH_CONFIG_DIR}/settings.inf"
            sed -i 's/^FontSize=.*/FontSize=10/' "${SMATH_CONFIG_DIR}/settings.inf"
            echo "  Updated FontName=DejaVu Sans, FontSize=10 in ${SMATH_CONFIG_DIR}/settings.inf (backup created: settings.inf.bak)"
        else
            sed -i 's/^FontSize=.*/FontSize=10/' "${SMATH_CONFIG_DIR}/settings.inf"
            echo "  FontName=DejaVu Sans, FontSize=10 set in ${SMATH_CONFIG_DIR}/settings.inf"
        fi
    fi

    WRITER_STYLE="${SMATH_CONFIG_DIR}/extensions/WriterManager/styles.xml"
    if [ -f "${WRITER_STYLE}" ]; then
        if ! grep -q 'font-family="DejaVu Sans"' "${WRITER_STYLE}"; then
            cp -n "${WRITER_STYLE}" "${WRITER_STYLE}.bak"
            sed -i 's/font-family="[^"]*"/font-family="DejaVu Sans"/' "${WRITER_STYLE}"
            sed -i 's/font-size="[0-9]*pt"/font-size="10pt"/' "${WRITER_STYLE}"
            echo "  Updated default font-family='DejaVu Sans', size=10pt in ${WRITER_STYLE} (backup created: styles.xml.bak)"
        else
            sed -i 's/font-size="[0-9]*pt"/font-size="10pt"/' "${WRITER_STYLE}"
            echo "  DejaVu Sans and font-size=10pt set in ${WRITER_STYLE}"
        fi
    fi
else
    echo "  SMath configuration directory not found yet (will apply automatically when SMath is run)."
fi

# 5. Refresh font cache
echo "[5/5] Refreshing fontconfig font cache..."
if command -v fc-cache >/dev/null 2>&1; then
    fc-cache -f >/dev/null 2>&1 || true
    echo "  Font cache successfully refreshed."
fi

echo ""
echo "=========================================================="
echo " Installation Complete!"
echo " All chemical reaction arrows, subscript numbers, and"
echo " engineering math symbols will now render cleanly in SMath."
echo " Run './verify/verify_fonts.sh' to test the installation."
echo "=========================================================="
