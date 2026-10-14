#!/bin/sh
set -eu
PULSE_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
export GI_TYPELIB_PATH="$PULSE_DIR/typelib${GI_TYPELIB_PATH:+:$GI_TYPELIB_PATH}"
exec /usr/bin/python3 "$PULSE_DIR/pulse.py" "$@"
