"""Dev tool: the pictures for the Reddit and Discord posts (workshop/reddit, workshop/discord; texts in
workshop/promotion.md) from the full-screen 4K grabs the Workshop screenshots were made from.

    python tools/shot-director/make_post_pictures.py SHOTS GLOBES

SHOTS holds the 4K grabs named like workshop/screenshots (01-overview-standard.png ...), GLOBES the whole-globe grabs
of the looks (t1-standard.png ...: shot director `symbols off`, `center 58 -18 3.55`, one per look). The game's
FPS/CPU/GPU overlay is painted out of the title bar, and the aircraft and submarine shots are cut to the part around
the readout at full resolution, so the text can be read on a phone.
"""
import os
import shutil
import sys

from PIL import Image, ImageDraw, ImageFont

from make_preview import find_globe

HERE = os.path.dirname(os.path.abspath(__file__))
WORKSHOP = os.path.normpath(os.path.join(HERE, '..', '..', 'workshop'))
FONTS = os.path.join(os.environ.get('WINDIR', r'C:\Windows'), 'Fonts')
W, H = 1920, 1080
BAND = (14, 18, 24)            # the title band of the Workshop preview
ACCENT = (226, 172, 122)       # the game's lit button
LOOKS = [('t1-standard', 'Standard'), ('t7-atlas', 'Atlas'), ('t3-midnight', 'Midnight'),
         ('t2-combat', 'Combat display'), ('t6-phosphor', 'Phosphor green'), ('t5-thermal', 'Thermal imager'),
         ('t4-neon', 'Neon')]


def font(name, size):
    return ImageFont.truetype(os.path.join(FONTS, name), size)


def clean(im):
    """The FPS/CPU/GPU overlay at the top left out of the title bar (the window title under it goes too)."""
    im = im.copy()
    draw = ImageDraw.Draw(im)
    bar = im.getpixel((1000, 8))
    if bar == (30, 31, 34):                      # tactical map: a 40 px title bar
        draw.rectangle((0, 0, 885, 39), fill=bar)
    else:                                        # mission editor: the overlay over its panel header
        draw.rectangle((0, 2, 885, 70), fill=bar)
    return im


def full(im):
    return clean(im).resize((W, H), Image.LANCZOS)


def cut(im, x, y):
    """A 1920 x 1080 part of the 4K grab at full resolution."""
    return clean(im).crop((x, y, x + W, y + H))


def looks_grid(globes):
    """The looks on the whole globe, four by two; the last cell says how many there are."""
    out = Image.new('RGB', (W, H), (0, 0, 0))
    draw = ImageDraw.Draw(out)
    cw, ch, d = W // 4, H // 2, 440
    label = font('seguisb.ttf', 30)
    for i, (name, title) in enumerate(LOOKS):
        im = Image.open(os.path.join(globes, name + '.png')).convert('RGB')
        cx, cy, r = find_globe(im)
        side = int(round(2 * r))
        x0, y0 = int(round(cx - r)), int(round(cy - r))
        disc = im.crop((x0, y0, x0 + side, y0 + side)).resize((d, d), Image.LANCZOS)
        mask = Image.new('L', (d * 2, d * 2), 0)
        ImageDraw.Draw(mask).ellipse((1, 1, d * 2 - 2, d * 2 - 2), fill=255)
        mask = mask.resize((d, d), Image.LANCZOS)
        left, top = (i % 4) * cw, (i // 4) * ch
        out.paste(disc, (left + (cw - d) // 2, top + 30), mask)
        draw.text((left + cw // 2, top + 500), title, font=label, fill=(235, 235, 235), anchor='mm')
    left, top = 3 * cw, ch
    draw.text((left + cw // 2, top + 190), '18 looks', font=font('seguisb.ttf', 72), fill=(245, 245, 245), anchor='mm')
    for j, line in enumerate(('day and night,', 'plus your own', 'theme files')):
        draw.text((left + cw // 2, top + 270 + j * 46), line, font=font('segoeui.ttf', 36), fill=(205, 210, 216),
                  anchor='mm')
    return out


def title_bar(im):
    """The controls shot with the new title bar buttons numbered in a band above them."""
    band, x0 = 150, W                            # the top right quarter of the 4K grab at full resolution
    out = Image.new('RGB', (W, H), BAND)
    out.paste(clean(im).crop((x0, 0, x0 + W, H - band)), (0, band))
    draw = ImageDraw.Draw(out)
    buttons = [(3090, 3268), (3268, 3446), (3450, 3552), (3552, 3708)]   # 4K: Topography, look, Legend, Cursor height
    number = font('seguisb.ttf', 28)
    for i, (a, b) in enumerate(buttons):
        x = (a + b) / 2 - x0
        draw.line((x, 112, x, band), fill=ACCENT, width=3)
        draw.ellipse((x - 21, 70, x + 21, 112), fill=ACCENT)
        draw.text((x, 91), str(i + 1), font=number, fill=BAND, anchor='mm')
    draw.text((40, 22), 'In the map\u2019s title bar', font=font('seguisb.ttf', 44), fill=(245, 245, 245))
    keys = [('1', 'Topography on/off'), ('2', 'Look'), ('3', 'Legend'), ('4', 'Cursor height on/off')]
    x, small = 42, font('segoeui.ttf', 28)
    for n, text in keys:
        draw.ellipse((x, 92, x + 32, 124), fill=ACCENT)
        draw.text((x + 16, 108), n, font=font('seguisb.ttf', 22), fill=BAND, anchor='mm')
        draw.text((x + 42, 108), text, font=small, fill=(205, 210, 216), anchor='lm')
        x += 42 + draw.textlength(text, font=small) + 30
    return out


def main():
    shots, globes = sys.argv[1:3]
    shot = lambda name: Image.open(os.path.join(shots, name + '.png')).convert('RGB')
    reddit = {
        '1-globe-standard': full(shot('01-overview-standard')),
        '2-globe-combat-display': full(shot('02-overview-combat')),
        '3-looks': looks_grid(globes),
        '4-title-bar': title_bar(shot('07-controls')),
        '5-tromso': full(shot('03-closeup-standard')),
        '6-lofoten-legend': full(shot('04-closeup-combat')),
        '7-aircraft': cut(shot('05-aircraft'), 1120, 870),
        '8-submarine': cut(shot('06-submarine'), 1120, 500),
        '9-mission-editor': full(shot('08-editor')),
    }
    for folder in ('reddit', 'discord'):
        os.makedirs(os.path.join(WORKSHOP, folder), exist_ok=True)
    for name, im in reddit.items():
        path = os.path.join(WORKSHOP, 'reddit', name + '.jpg')
        im.save(path, 'JPEG', quality=92, optimize=True)
        print(path)
    # Discord shows four pictures in a grid under the message.
    for i, name in enumerate(('1-globe-standard', '4-title-bar', '9-mission-editor', '3-looks')):
        path = os.path.join(WORKSHOP, 'discord', f'{i + 1}-{name.split("-", 1)[1]}.jpg')
        shutil.copyfile(os.path.join(WORKSHOP, 'reddit', name + '.jpg'), path)
        print(path)


if __name__ == '__main__':
    main()
