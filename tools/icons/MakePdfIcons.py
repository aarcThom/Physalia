"""Draws the two Read PDF component icons in the plug-in's established style.

Style read off the shipped Resources/*.png rather than guessed at: 24x24, fully transparent
interiors, anti-aliased strokes, outline #201E63 with a #65CFDE cyan accent. Shipped icons fill
x in [1,23] and y in [1,23] almost edge to edge, so these do too. Drawn at 16x and downsampled,
which reproduces the same soft edge the hand-drawn sheet icons have.

The pair shares one base — a folded-corner document — and differs only in the cyan mark, the same
grammar the rest of the set uses: Add Image is a frame plus a cyan '+', the search tools are a
subject plus a cyan magnifier. So the human intake tool gets the '+' and the model's reader gets
the lens, and the two read as halves of one thing at a glance.
"""
from PIL import Image, ImageDraw

S = 16              # supersample factor
N = 24              # final icon size
D = N * S
INK = (0x20, 0x1E, 0x63, 255)
CYAN = (0x65, 0xCF, 0xDE, 255)
W = int(1.7 * S)    # main stroke
WT = int(1.35 * S)  # thin stroke for the interior text lines


def px(v):
    return int(round(v * S))


def line(d, a, b, colour=INK, width=W):
    d.line([px(a[0]), px(a[1]), px(b[0]), px(b[1])], fill=colour, width=width)
    # PIL butt-joins and leaves notches at corners; cap both ends.
    r = width / 2
    for p in (a, b):
        d.ellipse([px(p[0]) - r, px(p[1]) - r, px(p[0]) + r, px(p[1]) + r], fill=colour)


def poly(d, pts, colour=INK, width=W):
    for i in range(len(pts) - 1):
        line(d, pts[i], pts[i + 1], colour, width)


def circle(d, c, rad, colour=INK, width=W):
    d.ellipse(
        [px(c[0] - rad), px(c[1] - rad), px(c[0] + rad), px(c[1] + rad)],
        outline=colour, width=width)


def document(d):
    """A page with its top-right corner turned down — the motif Export Conversation, Geometry
    Report and Harness Notes already use for 'a document'."""
    x0, y0, x1, y1 = 1.9, 2.1, 14.2, 20.6
    fold = 4.3

    # Outline, with the corner cut and closed by the fold's diagonal.
    poly(d, [
        (x1 - fold, y0), (x0, y0), (x0, y1), (x1, y1), (x1, y0 + fold), (x1 - fold, y0),
    ])
    # The turned-down corner itself.
    poly(d, [(x1 - fold, y0), (x1 - fold, y0 + fold), (x1, y0 + fold)])

    # Text lines, so it reads as a document rather than an empty card. The third is short and sits
    # clear of the cyan mark, which overlaps the page's bottom-right.
    line(d, (4.4, 9.5), (12.1, 9.5), INK, WT)
    line(d, (4.4, 12.6), (12.1, 12.6), INK, WT)
    line(d, (4.4, 15.7), (8.6, 15.7), INK, WT)


def draw(name, extra):
    img = Image.new('RGBA', (D, D), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    document(d)
    extra(d)
    img.resize((N, N), Image.LANCZOS).save(name)
    bb = Image.open(name).getchannel('A').point(lambda a: 255 if a > 25 else 0).getbbox()
    print(f'wrote {name}  bbox={bb}')


def add_plus(d):
    """A cyan '+' at the bottom-right: Add Image's grammar for 'bring one of these in'."""
    cx, cy, arm = 17.5, 16.9, 4.1
    line(d, (cx - arm, cy), (cx + arm, cy), CYAN, W)
    line(d, (cx, cy - arm), (cx, cy + arm), CYAN, W)


def add_lens(d):
    """A cyan magnifier over the page — the search/look family, but over a document rather than a
    chip, so it does not read as Component Search."""
    circle(d, (16.3, 15.6), 4.7, CYAN, W)
    line(d, (19.7, 19.0), (21.9, 21.2), CYAN, W)


draw('AddPdf.png', add_plus)
draw('ReadPdf.png', add_lens)
