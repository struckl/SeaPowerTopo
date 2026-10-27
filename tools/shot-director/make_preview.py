"""Dev tool: the Workshop preview (workshop/preview.jpg) from a full-screen 4K grab of the map at minimum zoom (the whole
globe, unit symbols off: shot director `symbols off`, `center 58 -18 3.55`).

The globe is found in the grab and cut out onto black (the compass and the title bar over its top rim go with it), then
the title band of the other Sea Power mods (Pop-Out Windows, Radio) goes over the bottom: 1000 x 1000, Segoe UI.

    python tools/shot-director/make_preview.py GRAB.png
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', '..', 'workshop', 'preview.jpg')
FONTS = os.path.join(os.environ.get('WINDIR', r'C:\Windows'), 'Fonts')
SIZE = 1000
GLOBE = 880            # diameter in the preview
GLOBE_TOP = 18         # the band covers the globe's lower rim
TITLE = 'Topographic Map'
SUBTITLE = 'Land heights and sea depths on the map'


def find_globe(im):
    """Centre and radius of the globe in the grab: its edges along the centre row and column (the background is
    black with faint stars); the rim under the title bar and the compass is left out."""
    gray = im.convert('L')
    w = im.width
    cx, cy = w // 2, 1068

    def bright(x, y):
        return gray.getpixel((x, y)) > 40

    left = next(x for x in range(0, cx) if all(bright(x + d, cy) for d in range(4)))
    right = next(x for x in range(w - 1, cx, -1) if all(bright(x - d, cy) for d in range(4)))
    bottom = next(y for y in range(2090, cy, -1) if all(bright(cx, y - d) for d in range(4)))
    r = (right - left) / 2
    cx, cy = (left + right) / 2, bottom - r
    return cx, cy, r - max(0.0, 70 - (cy - r))


def globe_on_black(im):
    cx, cy, r = find_globe(im)
    side = int(round(2 * r))
    disc = im.crop((int(round(cx - r)), int(round(cy - r)), int(round(cx - r)) + side, int(round(cy - r)) + side))
    disc = disc.resize((GLOBE, GLOBE), Image.LANCZOS)
    # A soft-edged circle, drawn at twice the size and scaled down.
    mask = Image.new('L', (GLOBE * 2, GLOBE * 2), 0)
    ImageDraw.Draw(mask).ellipse((1, 1, GLOBE * 2 - 2, GLOBE * 2 - 2), fill=255)
    mask = mask.resize((GLOBE, GLOBE), Image.LANCZOS)
    out = Image.new('RGB', (SIZE, SIZE), (0, 0, 0))
    out.paste(disc, ((SIZE - GLOBE) // 2, GLOBE_TOP), mask)
    return out


def title_band(out):
    band = Image.new('RGBA', (SIZE, 210), (14, 18, 24, 205))
    out.paste(band, (0, SIZE - 210), band)
    draw = ImageDraw.Draw(out)
    draw.text((52, SIZE - 196), TITLE, font=ImageFont.truetype(os.path.join(FONTS, 'seguisb.ttf'), 92),
              fill=(245, 245, 245))
    draw.text((56, SIZE - 76), SUBTITLE, font=ImageFont.truetype(os.path.join(FONTS, 'segoeui.ttf'), 40),
              fill=(205, 210, 216))
    return out


def main():
    grab = Image.open(sys.argv[1]).convert('RGB')
    out = title_band(globe_on_black(grab))
    out.save(os.path.normpath(OUT), 'JPEG', quality=93, optimize=True)
    print(os.path.normpath(OUT))


if __name__ == '__main__':
    main()
