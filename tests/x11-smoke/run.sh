# Usage (inside the container): bash /test/run.sh [none|openbox|xfwm4]
Xvfb :99 -screen 0 1920x1080x24 +extension RECORD +extension Composite >/dev/null 2>&1 &
export DISPLAY=:99
sleep 2
case "${1:-none}" in
  openbox) openbox >/dev/null 2>&1 & ;;
  xfwm4) dbus-launch xfwm4 --compositor=on >/dev/null 2>&1 & ;;
esac
sleep 3
echo "== window manager: ${1:-none}"
python3 /test/test.py
