# Linux/X11 overlay smoke test, run in Docker against a linux-x64 publish:
#   dotnet publish src/Budditec.Overlay.Desktop -c Release -r linux-x64 --self-contained -o publish-linux
#   docker build -t budditec-x11-smoke tests/x11-smoke
#   docker run --rm -v "$PWD/publish-linux:/app:ro" -v "$PWD/tests/x11-smoke:/test:ro" budditec-x11-smoke bash /test/run.sh openbox
# Puts a window behind the widgets, then checks the widgets' input shape and whether real (XTEST) clicks
# reach that window: locked, in edit mode (toggled with the global hotkey), and locked again.
import ctypes, subprocess, time, os, re

x = ctypes.CDLL("libX11.so.6"); xe = ctypes.CDLL("libXext.so.6")
x.XOpenDisplay.restype = ctypes.c_void_p; x.XOpenDisplay.argtypes = [ctypes.c_char_p]
x.XDefaultRootWindow.restype = ctypes.c_ulong; x.XDefaultRootWindow.argtypes = [ctypes.c_void_p]
x.XCreateSimpleWindow.restype = ctypes.c_ulong
x.XCreateSimpleWindow.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int, ctypes.c_int, ctypes.c_uint, ctypes.c_uint, ctypes.c_uint, ctypes.c_ulong, ctypes.c_ulong]
x.XSelectInput.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_long]
x.XMapWindow.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
x.XFlush.argtypes = [ctypes.c_void_p]; x.XSync.argtypes = [ctypes.c_void_p, ctypes.c_int]
x.XCheckWindowEvent.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_long, ctypes.c_void_p]
xe.XShapeGetRectangles.restype = ctypes.c_void_p
xe.XShapeGetRectangles.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int, ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int)]
ShapeInput, ButtonPressMask = 2, 1 << 2

d = x.XOpenDisplay(None)
bg = x.XCreateSimpleWindow(d, x.XDefaultRootWindow(d), 0, 0, 1920, 1080, 0, 0, 0)
x.XSelectInput(d, bg, ButtonPressMask); x.XMapWindow(d, bg); x.XSync(d, 0)

env = dict(os.environ, BUDDITEC_OVERLAY_SETTINGS="/tmp/s/settings.json")
app = subprocess.Popen(["/app/Budditec.Overlay.Desktop"], env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
time.sleep(12)
print("app running:", app.poll() is None)

tree = subprocess.run(["xwininfo", "-root", "-tree"], capture_output=True, text=True).stdout
widgets = {}
for m in re.finditer(r'(0x[0-9a-f]+) "(Status|Chat)":.*?(\d+)x(\d+)\+(-?\d+)\+(-?\d+)\s+\+(-?\d+)\+(-?\d+)', tree):
    widgets[m.group(2)] = (int(m.group(1), 16), int(m.group(7)), int(m.group(8)), int(m.group(3)), int(m.group(4)))
print("widgets:", {k: v[1:] for k, v in widgets.items()})

def input_rects(w):
    n, order = ctypes.c_int(), ctypes.c_int()
    xe.XShapeGetRectangles(d, w, ShapeInput, ctypes.byref(n), ctypes.byref(order))
    return n.value

def click_reaches_background(px, py):
    ev = ctypes.create_string_buffer(192)
    while x.XCheckWindowEvent(d, bg, ButtonPressMask, ev): pass
    subprocess.run(["xdotool", "mousemove", str(px), str(py), "click", "1"]); time.sleep(0.7); x.XSync(d, 0)
    return bool(x.XCheckWindowEvent(d, bg, ButtonPressMask, ev))

def report(stage):
    for name, (w, wx, wy, ww, wh) in widgets.items():
        print(f"{stage:>18} {name:6} input rects={input_rects(w)} click reaches background={click_reaches_background(wx + ww // 2, wy + wh // 2)}")

print("warm-up click on bare background reaches it:", click_reaches_background(1500, 900))
report("locked")
subprocess.run(["xdotool", "key", "ctrl+alt+o"]); time.sleep(1.5)
report("edit mode")
subprocess.run(["xdotool", "key", "ctrl+alt+o"]); time.sleep(1.5)
report("locked again")
app.terminate()
try: out = app.communicate(timeout=5)[0]
except Exception: app.kill(); out = app.communicate()[0]
print("--- app output (last lines) ---"); print("\n".join(out.strip().splitlines()[-15:]))
