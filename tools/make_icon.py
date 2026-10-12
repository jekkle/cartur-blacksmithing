"""Draws the 256x256 Thunderstore icon: a hammer over an anvil.

Thunderstore rejects anything that is not exactly 256x256 PNG, and there is no art asset to crop
from, so the icon is drawn - same approach and background as the other Cartur icons.

    python tools/make_icon.py
"""
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), "package", "icon.png")

SIZE = 256
SCALE = 4

BACKGROUND = (38, 34, 30, 255)
IRON = (120, 124, 130, 255)
IRON_EDGE = (58, 60, 66, 255)
IRON_LIGHT = (176, 182, 188, 255)
HANDLE = (138, 96, 58, 255)
HANDLE_EDGE = (74, 48, 26, 255)
SPARK = (246, 196, 60, 255)

s = SCALE
image = Image.new("RGBA", (SIZE * s, SIZE * s), BACKGROUND)
draw = ImageDraw.Draw(image)


def poly(points, fill, edge, width=5):
    draw.polygon([(x * s, y * s) for x, y in points], fill=fill, outline=edge, width=width * s)


# Anvil: horn to the left, face on top, waist, foot.
poly([(30, 150), (70, 140), (200, 140), (214, 150), (200, 168), (150, 172), (140, 196), (170, 214),
      (170, 228), (86, 228), (86, 214), (116, 196), (106, 172), (64, 168)], IRON, IRON_EDGE)
draw.line([(72 * s, 146 * s), (198 * s, 146 * s)], fill=IRON_LIGHT, width=4 * s)

# Hammer, raised, handle from lower right.
draw.line([(206 * s, 120 * s), (128 * s, 42 * s)], fill=HANDLE_EDGE, width=18 * s)
draw.line([(206 * s, 120 * s), (128 * s, 42 * s)], fill=HANDLE, width=10 * s)
poly([(96, 52), (130, 18), (160, 48), (126, 82)], IRON, IRON_EDGE)

# Sparks off the face.
for x, y in [(118, 122), (134, 112), (152, 120), (104, 112)]:
    draw.ellipse([(x - 3) * s, (y - 3) * s, (x + 3) * s, (y + 3) * s], fill=SPARK)

image = image.resize((SIZE, SIZE), Image.LANCZOS)
image.save(OUT)
print("wrote", OUT, image.size)
