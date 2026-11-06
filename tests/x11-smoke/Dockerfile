# Headless X11 environment for tests/x11-smoke/test.py: Xvfb plus two reparenting window managers.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
RUN apt-get update && apt-get install -y --no-install-recommends xvfb x11-utils xdotool python3 \
    libx11-6 libxext6 libxtst6 libxi6 libxrandr2 libxcursor1 libxinerama1 libxkbcommon0 libxkbcommon-x11-0 \
    libice6 libsm6 libfontconfig1 fonts-dejavu-core libx11-xcb1 openbox xfwm4 dbus-x11 \
    && rm -rf /var/lib/apt/lists/*
