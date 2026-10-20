#!/usr/bin/env bash
# ==============================================================================
# SMath Studio (Mono) Linux Font Pack Uninstaller
# ==============================================================================
set -e

TARGET_FONT_DIR="/usr/local/share/fonts/ms-cleartype"
SYSTEM_FONTCONFIG="/etc/fonts/local.conf"
USER_FONTCONFIG="${HOME}/.config/fontconfig/fonts.conf"
SMATH_CONFIG_DIR="${HOME}/.config/SMath"

echo "Reverting SMath Studio Linux Font Pack changes..."

if [ "$(id -u)" -eq 0 ]; then
    rm -rf "${TARGET_FONT_DIR}"
    rm -f "${SYSTEM_FONTCONFIG}"
    echo "Removed system fonts and /etc/fonts/local.conf"
fi

rm -rf "${HOME}/.local/share/fonts/ms-cleartype"
rm -f "${USER_FONTCONFIG}"
echo "Removed user fonts and ~/.config/fontconfig/fonts.conf"

if [ -f "${SMATH_CONFIG_DIR}/settings.inf.bak" ]; then
    mv -f "${SMATH_CONFIG_DIR}/settings.inf.bak" "${SMATH_CONFIG_DIR}/settings.inf"
    echo "Restored settings.inf from backup."
fi

if [ -f "${SMATH_CONFIG_DIR}/extensions/WriterManager/styles.xml.bak" ]; then
    mv -f "${SMATH_CONFIG_DIR}/extensions/WriterManager/styles.xml.bak" "${SMATH_CONFIG_DIR}/extensions/WriterManager/styles.xml"
    echo "Restored styles.xml from backup."
fi

if command -v fc-cache >/dev/null 2>&1; then
    fc-cache -f >/dev/null 2>&1 || true
    echo "Refreshed font cache."
fi

echo "Uninstallation complete."
