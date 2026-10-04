"""Generates Flight's logo from one set of shapes.

Concept: "your voice takes flight" -- a paper plane leaving a trail of voice
waveform bars, on an indigo-to-sky tile. Small sizes (<= 32 px) use a
simplified mark (bigger plane, no bars) so the tray icon stays legible.

Outputs (committed, regenerate with `python assets/brand/make_logo.py`):
  flight-logo.svg            master artwork
  png/flight-<size>.png      full mark, 16..1024
  flight.ico                 app/exe/window icon (small variant at <= 32 px)
  flight-listening.ico       tray: recording
  flight-paused.ico          tray: paused (desaturated, dimmed)
  icon-manifest.json         which variant each .ico frame uses (read by tests)
"""
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance

OUT = Path(__file__).resolve().parent
CANVAS = 1024
SS = 4  # supersampling factor for the raster renders

GRADIENT = ("#3730A3", "#38BDF8")  # indigo -> sky, top-left to bottom-right
TILE_MARGIN, TILE_RADIUS = 24, 228
PLANE_TOP = "#FFFFFF"
PLANE_FOLD = "#C7D7FE"
BAR_COLOR = (255, 255, 255, 220)
REC_RED, REC_RING = "#EF4444", "#FFFFFF"

# Paper plane, nose up-right. Two facets meeting at the crease.
NOSE, WING, CREASE, TAIL = (818, 206), (250, 470), (490, 552), (584, 782)
# Voice bars trailing at lower left: (centre x, height), shared width/centre y.
BARS, BAR_W, BAR_CY = [(246, 124), (330, 224), (414, 148)], 56, 774

ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
SMALL_MAX = 32


def _scale_about_centre(pt, k):
    c = CANVAS / 2
    return (c + (pt[0] - c) * k, c + (pt[1] - c) * k)


def plane(small):
    # The small mark drops the bars, so the plane grows and recentres.
    pts = [NOSE, WING, CREASE, TAIL]
    if small:
        pts = [_scale_about_centre((x - 40, y + 30), 1.22) for x, y in pts]
    return pts


def _hex(c):
    c = c.lstrip("#")
    return tuple(int(c[i:i + 2], 16) for i in (0, 2, 4))


def render(size, small, state="normal"):
    s = CANVAS * SS
    k = SS

    # Diagonal gradient tile, masked to a rounded square.
    a, b = _hex(GRADIENT[0]), _hex(GRADIENT[1])
    grad = Image.new("RGB", (256, 256))
    gp = grad.load()
    for y in range(256):
        for x in range(256):
            t = (x + y) / 510
            gp[x, y] = tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))
    grad = grad.resize((s, s), Image.BILINEAR)
    mask = Image.new("L", (s, s), 0)
    m = TILE_MARGIN * k
    ImageDraw.Draw(mask).rounded_rectangle((m, m, s - m, s - m), TILE_RADIUS * k, fill=255)
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img, "RGBA")
    if not small:
        for cx, h in BARS:
            d.rounded_rectangle(
                ((cx - BAR_W / 2) * k, (BAR_CY - h / 2) * k, (cx + BAR_W / 2) * k, (BAR_CY + h / 2) * k),
                BAR_W / 2 * k, fill=BAR_COLOR)

    n, w, c, t = [(x * k, y * k) for x, y in plane(small)]
    d.polygon([n, w, c], fill=PLANE_TOP)
    d.polygon([n, c, t], fill=PLANE_FOLD)

    if state == "listening":
        cx, cy, r, ring = (790, 790, 170, 44) if small else (826, 826, 112, 30)
        d.ellipse(((cx - r - ring) * k, (cy - r - ring) * k, (cx + r + ring) * k, (cy + r + ring) * k), fill=REC_RING)
        d.ellipse(((cx - r) * k, (cy - r) * k, (cx + r) * k, (cy + r) * k), fill=REC_RED)

    out = img.resize((size, size), Image.LANCZOS)
    if state == "paused":
        alpha = out.getchannel("A").point(lambda v: v * 55 // 100)
        out = ImageEnhance.Color(out).enhance(0.0)
        out.putalpha(alpha)
    return out


def write_svg():
    pts = lambda ps: " ".join(f"{x:g},{y:g}" for x, y in ps)
    bars = "\n".join(
        f'    <rect x="{cx - BAR_W / 2:g}" y="{BAR_CY - h / 2:g}" width="{BAR_W}" height="{h}" '
        f'rx="{BAR_W / 2:g}" fill="#FFFFFF" fill-opacity="{BAR_COLOR[3] / 255:.2f}"/>'
        for cx, h in BARS)
    n, w, c, t = NOSE, WING, CREASE, TAIL
    size = CANVAS - 2 * TILE_MARGIN
    svg = f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {CANVAS} {CANVAS}" role="img" aria-label="Flight">
  <defs>
    <linearGradient id="tile" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="{GRADIENT[0]}"/>
      <stop offset="1" stop-color="{GRADIENT[1]}"/>
    </linearGradient>
  </defs>
  <rect x="{TILE_MARGIN}" y="{TILE_MARGIN}" width="{size}" height="{size}" rx="{TILE_RADIUS}" fill="url(#tile)"/>
  <g>
{bars}
  </g>
  <polygon points="{pts([n, w, c])}" fill="{PLANE_TOP}"/>
  <polygon points="{pts([n, c, t])}" fill="{PLANE_FOLD}"/>
</svg>
"""
    (OUT / "flight-logo.svg").write_text(svg, encoding="utf-8")


def write_ico(path, state):
    frames = [render(sz, small=sz <= SMALL_MAX, state=state) for sz in ICO_SIZES]
    frames[-1].save(path, format="ICO", sizes=[(sz, sz) for sz in ICO_SIZES], append_images=frames[:-1])


def main():
    write_svg()
    (OUT / "png").mkdir(exist_ok=True)
    for sz in [16, 32, 48, 64, 128, 256, 512, 1024]:
        render(sz, small=sz <= SMALL_MAX).save(OUT / "png" / f"flight-{sz}.png")
    write_ico(OUT / "flight.ico", "normal")
    write_ico(OUT / "flight-listening.ico", "listening")
    write_ico(OUT / "flight-paused.ico", "paused")
    manifest = {
        "icons": ["flight.ico", "flight-listening.ico", "flight-paused.ico"],
        "frames": {str(sz): "small" if sz <= SMALL_MAX else "full" for sz in ICO_SIZES},
    }
    (OUT / "icon-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
