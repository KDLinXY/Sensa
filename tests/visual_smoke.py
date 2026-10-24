import ctypes
import os
import subprocess
import time
from PIL import ImageGrab

user32 = ctypes.windll.user32
speed = ctypes.c_int()
mouse = (ctypes.c_int * 3)()
user32.SystemParametersInfoW(0x70, 0, ctypes.byref(speed), 0)
user32.SystemParametersInfoW(0x03, 0, ctypes.byref(mouse), 0)
exe = os.path.abspath("dist/Sensa.exe")
process = subprocess.Popen([exe])
try:
    time.sleep(3)
    if process.poll() is not None:
        raise RuntimeError(f"Sensa exited early with code {process.returncode}")
    width, height = user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)
    app_width, app_height = 452, 730
    left = max(0, (width - app_width) // 2 - 12)
    top = max(0, (height - app_height) // 2 - 12)
    shot = ImageGrab.grab(bbox=(left, top, left + app_width + 24, top + app_height + 24), all_screens=True)
    shot.save("build/visual-smoke.png")
    print(f"UI smoke passed; screenshot {shot.size[0]}x{shot.size[1]}")
finally:
    process.terminate()
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        process.kill()
    user32.SystemParametersInfoW(0x71, 0, speed.value, 0)
    user32.SystemParametersInfoW(0x04, 0, ctypes.byref(mouse), 0)
