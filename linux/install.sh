#!/bin/sh
# Per-user install; existing system packages are never modified.
set -eu
PULSE_AUTOSTART=false
case "${1:-}" in
    --autostart) PULSE_AUTOSTART=true ;;
    '') ;;
    *) printf 'Usage: %s [--autostart]\n' "$0" >&2; exit 2 ;;
esac
PULSE_SOURCE=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
PULSE_DATA=${XDG_DATA_HOME:-"$HOME/.local/share"}
PULSE_DEST="$PULSE_DATA/pulse-monitor"
/usr/bin/python3 -c 'import psutil, gi; gi.require_version("Gtk", "3.0")'
mkdir -p "$PULSE_DEST/typelib" "$PULSE_DATA/applications"
if ! /usr/bin/python3 -c 'import gi; gi.require_version("AyatanaAppIndicator3", "0.1")' 2>/dev/null; then
    /usr/bin/python3 -c 'import ctypes; ctypes.CDLL("libayatana-appindicator3.so.1")'
    PULSE_TMP=$(mktemp -d)
    trap 'rm -rf "$PULSE_TMP"' EXIT HUP INT TERM
    (cd "$PULSE_TMP" && apt-get download gir1.2-ayatanaappindicator3-0.1)
    for package in "$PULSE_TMP"/*.deb; do dpkg-deb -x "$package" "$PULSE_TMP/unpacked"; done
    find "$PULSE_TMP/unpacked" -name 'AyatanaAppIndicator3-0.1.typelib' -exec cp '{}' "$PULSE_DEST/typelib/" \;
fi
cp "$PULSE_SOURCE/pulse.py" "$PULSE_SOURCE/metrics.py" "$PULSE_SOURCE/launch.sh" "$PULSE_DEST/"
cp "$PULSE_SOURCE/../assets/PulseWidget.png" "$PULSE_DEST/"
cp "$PULSE_SOURCE/../LICENSE" "$PULSE_DEST/"
chmod 755 "$PULSE_DEST/launch.sh"
# Escape special characters for Desktop Entry quoted Exec arguments.
/usr/bin/python3 - "$PULSE_DEST" "$PULSE_DATA/applications/pulse-monitor.desktop" <<'PYTHON'
import pathlib, sys
root = pathlib.Path(sys.argv[1])
launcher = str(root/'launch.sh')
for char in ('\\', '"', '`', '$'):
    launcher = launcher.replace(char, '\\'+char)
launcher = launcher.replace('%', '%%')
pathlib.Path(sys.argv[2]).write_text('[Desktop Entry]\nType=Application\nName=Pulse Monitor\nComment=CPU, GPU and memory usage\nExec="'+launcher+'"\nIcon='+str(root/'PulseWidget.png')+'\nTerminal=false\nCategories=System;Monitor;\nStartupNotify=false\n')
PYTHON
printf 'Installed: %s\n' "$PULSE_DEST"
if [ "$PULSE_AUTOSTART" = true ]; then
    PULSE_CONFIG=${XDG_CONFIG_HOME:-"$HOME/.config"}
    mkdir -p "$PULSE_CONFIG/autostart"
    /usr/bin/python3 - "$PULSE_DATA/applications/pulse-monitor.desktop" "$PULSE_CONFIG/autostart/pulse-monitor.desktop" <<'PYTHON'
import pathlib, sys
text = pathlib.Path(sys.argv[1]).read_text()
text = '\n'.join(line+' --background' if line.startswith('Exec=') else line for line in text.splitlines())
pathlib.Path(sys.argv[2]).write_text(text+'\nX-GNOME-Autostart-enabled=true\n')
PYTHON
    printf 'Login autostart enabled.\n'
fi
