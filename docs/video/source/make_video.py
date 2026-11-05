"""Rinel : video de presentation (60 s, 1920x1080, 30 i/s), rendu « verre cristal » en 3D simulee.
Images calculees avec Pillow et NumPy : fond a bokeh, panneaux de verre depoli (flou du fond derriere, biseau
lumineux, reflets, balayage speculaire), perspective, orbes de cristal, parallaxe.
Chaine complete (images, musique, ffmpeg) : docs/VIDEO-PRESENTATION.md.
Usage : python make_video.py frames [debut fin]   -> frames/00000.png ...
        python make_video.py audio                -> audio.wav (musique originale synthetisee)
"""
import math, os, random, sys, wave
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops, ImageEnhance

HERE = os.path.dirname(os.path.abspath(__file__))
SHOTS = os.path.normpath(os.path.join(HERE, '..', '..', 'screenshots'))
W, H, FPS, DUR = 1920, 1080, 30, 60.0

LIGHT = (238, 244, 255)
SOFT = (204, 220, 246)
ORANGE, GREEN, RED, GREY = (245, 165, 36), (63, 208, 127), (255, 93, 82), (142, 150, 163)
CYAN, VIOLET = (90, 205, 255), (150, 120, 255)

# ---------------------------------------------------------------- outils
def clamp(x, a=0.0, b=1.0): return max(a, min(b, x))
def seg(t, a, b): return clamp((t - a) / (b - a)) if b > a else (1.0 if t >= a else 0.0)
def ease_out(x): return 1 - (1 - x) ** 3
def ease_in_out(x): return 3 * x * x - 2 * x ** 3
def lerp(a, b, x): return a + (b - a) * x

_fonts = {}
def font(kind, size):
    key = (kind, size)
    if key not in _fonts:
        f = {'b': 'segoeuib.ttf', 'r': 'segoeui.ttf', 'l': 'segoeuil.ttf', 'm': 'consola.ttf', 'mb': 'consolab.ttf', 's': 'seguisym.ttf'}[kind]
        _fonts[key] = ImageFont.truetype(os.path.join(r'C:\Windows\Fonts', f), size)
    return _fonts[key]

_assets = {}
def asset(name):
    if name not in _assets:
        if name.startswith('sprite:'):
            _assets[name] = Image.open(os.path.join(HERE, 'sprites', name[7:] + '.png')).convert('RGBA')
        else:
            _assets[name] = Image.open(os.path.join(SHOTS, name + '.png')).convert('RGB')
    return _assets[name]

def overlay(img, layer, pos=(0, 0), alpha=1.0):
    if alpha <= 0.003: return
    if alpha < 1.0:
        a = layer.getchannel('A').point(lambda v: int(v * alpha))
        layer = layer.copy(); layer.putalpha(a)
    img.paste(layer, pos, layer)

def rrect_mask(w, h, r, ss=3):
    big = Image.new('L', (w * ss, h * ss), 0)
    ImageDraw.Draw(big).rounded_rectangle((0, 0, w * ss - 1, h * ss - 1), radius=r * ss, fill=255)
    return big.resize((w, h), Image.LANCZOS)

def rrect_layer(w, h, r, fill, ss=3, top_flat=False):
    big = Image.new('RGBA', (w * ss, h * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    if top_flat: d.rounded_rectangle((0, -r * ss * 2, w * ss - 1, h * ss - 1), radius=r * ss, fill=fill)
    else: d.rounded_rectangle((0, 0, w * ss - 1, h * ss - 1), radius=r * ss, fill=fill)
    return big.resize((w, h), Image.LANCZOS)

def screen(a, b):  # fusion « ecran » : les lumieres s'ajoutent
    return ImageChops.screen(a, b)

# ---------------------------------------------------------------- fond : degrade profond + bokeh en parallaxe
_base = {}
def base_bg(mood):
    if mood not in _base:
        c1, c2 = {'blue': ((8, 12, 34), (24, 62, 120)), 'dark': ((6, 7, 14), (22, 26, 46)), 'violet': ((12, 10, 38), (46, 40, 112))}[mood]
        x = np.linspace(0, 1, W)[None, :, None]; y = np.linspace(0, 1, H)[:, None, None]
        t = np.clip(0.55 * x + 0.55 * (1 - y) * 0.6, 0, 1)
        arr = np.array(c1)[None, None, :] * (1 - t) + np.array(c2)[None, None, :] * t
        # vignette
        v = 1 - 0.55 * (((x - 0.5) * 1.6) ** 2 + ((y - 0.5) * 1.3) ** 2)
        arr = arr * np.clip(v, 0.35, 1)
        _base[mood] = np.broadcast_to(arr, (H, W, 3)).copy()
    return _base[mood]

_orbs = {}
def orbs(mood):
    if mood not in _orbs:
        rnd = random.Random({'blue': 7, 'dark': 3, 'violet': 11}[mood])
        pal = {'blue': [CYAN, VIOLET, (255, 170, 80), (80, 230, 170), (255, 110, 150)],
               'dark': [(255, 90, 80), (255, 150, 60), (90, 120, 255), (150, 110, 255)],
               'violet': [VIOLET, CYAN, (255, 120, 200), (110, 150, 255)]}[mood]
        _orbs[mood] = [dict(x=rnd.random(), y=rnd.random(), r=rnd.uniform(0.02, 0.095), c=rnd.choice(pal), a=rnd.uniform(0.35, 0.8),
                            z=rnd.uniform(0.25, 1.0), ph=rnd.uniform(0, 6.28), sp=rnd.uniform(0.15, 0.4)) for _ in range(22)]
    return _orbs[mood]

def bg_deep(t, mood='blue', accent=None):
    arr = base_bg(mood)
    lw, lh = W // 4, H // 4
    lay = Image.new('RGB', (lw, lh), (0, 0, 0))
    d = ImageDraw.Draw(lay)
    for o in orbs(mood):
        ox = (o['x'] + 0.05 * o['z'] * math.sin(t * o['sp'] + o['ph'])) * lw
        oy = (o['y'] + 0.04 * o['z'] * math.cos(t * o['sp'] * 0.8 + o['ph'])) * lh
        r = o['r'] * lw
        col = tuple(int(c * o['a'] * 0.36) for c in o['c'])
        d.ellipse((ox - r, oy - r, ox + r, oy + r), fill=col)
        d.ellipse((ox - r * 0.72, oy - r * 0.72, ox + r * 0.72, oy + r * 0.72), fill=tuple(min(255, int(c * 1.25)) for c in col))
    if accent:
        for k in range(3):
            ax = (0.25 + 0.25 * k + 0.04 * math.sin(t * 0.5 + k)) * lw; ay = (0.7 - 0.15 * k) * lh; r = lw * 0.11
            d.ellipse((ax - r, ay - r, ax + r, ay + r), fill=tuple(int(c * 0.55) for c in accent))
    lay = lay.filter(ImageFilter.GaussianBlur(7)).resize((W, H), Image.BICUBIC)
    base = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), 'RGB')
    img = screen(base, lay)
    # faisceau de lumiere en diagonale
    ray = Image.new('L', (W, H), 0)
    dr = ImageDraw.Draw(ray)
    sh = 0.06 * math.sin(t * 0.3)
    for k, (wid, al) in enumerate([(260, 16), (120, 22)]):
        cx = W * (0.62 + sh + 0.06 * k)
        dr.polygon([(cx - wid, 0), (cx + wid, 0), (cx - 520 + wid, H), (cx - 520 - wid, H)], fill=al)
    ray = ray.filter(ImageFilter.GaussianBlur(40))
    return screen(img, Image.merge('RGB', (ray, ray, ray)))

# ---------------------------------------------------------------- texte
def text(img, s, cx, cy, size, color, kind='b', alpha=1.0, anchor='mm', glow=None):
    f = font(kind, size)
    bbox = ImageDraw.Draw(Image.new('L', (1, 1))).textbbox((0, 0), s, font=f, anchor=anchor)
    pad = 36
    w, h = bbox[2] - bbox[0] + 2 * pad, bbox[3] - bbox[1] + 2 * pad
    ox, oy = -bbox[0] + pad, -bbox[1] + pad
    layer = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    if glow is not None:
        g = Image.new('RGBA', (w, h), (0, 0, 0, 0))
        ImageDraw.Draw(g).text((ox, oy), s, font=f, fill=tuple(glow) + (255,), anchor=anchor)
        g = g.filter(ImageFilter.GaussianBlur(max(6, size // 7)))
        layer.alpha_composite(g)
        g2 = g.filter(ImageFilter.GaussianBlur(2)); layer.alpha_composite(g2)
    sh = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(sh).text((ox + 2, oy + 4), s, font=f, fill=(0, 0, 0, 110), anchor=anchor)
    layer.alpha_composite(sh.filter(ImageFilter.GaussianBlur(3)))
    ImageDraw.Draw(layer).text((ox, oy), s, font=f, fill=tuple(color) + (255,), anchor=anchor)
    if anchor == 'mm': pos = (int(cx - w / 2), int(cy - h / 2))
    elif anchor == 'lm': pos = (int(cx - pad), int(cy - h / 2))
    else: pos = (int(cx - w + pad), int(cy - h / 2))
    overlay(img, layer, pos, alpha)

def caption(img, title, sub=None, y=120, alpha=1.0, size=64, glow=CYAN):
    text(img, title, W / 2, y, size, LIGHT, 'b', alpha, glow=glow)
    if sub: text(img, sub, W / 2, y + int(size * 0.98), int(size * 0.53), SOFT, 'r', alpha * 0.95)

# ---------------------------------------------------------------- 3D : perspective d'un panneau
D_CAM = 2300.0
def proj(px, py, cx, cy, ax, ay):
    x, y = px - cx, py - cy
    ra, rb = math.radians(ay), math.radians(ax)
    x1, z1 = x * math.cos(ra), -x * math.sin(ra)
    y2, z2 = y * math.cos(rb) - z1 * math.sin(rb), y * math.sin(rb) + z1 * math.cos(rb)
    s = D_CAM / (D_CAM - z2)
    return cx + x1 * s, cy + y2 * s

def persp_coeffs(dst, src):
    A, B = [], []
    for (X, Y), (x, y) in zip(dst, src):
        A.append([X, Y, 1, 0, 0, 0, -x * X, -x * Y]); B.append(x)
        A.append([0, 0, 0, X, Y, 1, -y * X, -y * Y]); B.append(y)
    return np.linalg.solve(np.array(A, float), np.array(B, float)).tolist()

def warp(layer, pad, w, h, ax, ay):
    if abs(ax) < 0.01 and abs(ay) < 0.01: return layer
    cx, cy = pad + w / 2, pad + h / 2
    src = [(pad, pad), (pad + w, pad), (pad + w, pad + h), (pad, pad + h)]
    dst = [proj(x, y, cx, cy, ax, ay) for x, y in src]
    # on etend aux bords du calque (marges) par la meme transformation
    full_src = [(0, 0), (layer.width, 0), (layer.width, layer.height), (0, layer.height)]
    full_dst = [proj(x, y, cx, cy, ax, ay) for x, y in full_src]
    return layer.transform(layer.size, Image.PERSPECTIVE, persp_coeffs(full_dst, full_src), Image.BICUBIC)

def frost_patch(img, x, y, w, h, blur=26, lift=1.18, sat=1.35):
    patch = img.crop((int(x), int(y), int(x + w), int(y + h)))
    k = 6
    small = patch.resize((max(2, w // k), max(2, h // k)), Image.BILINEAR).filter(ImageFilter.GaussianBlur(blur / k))
    out = small.resize((w, h), Image.BICUBIC)
    out = ImageEnhance.Brightness(out).enhance(lift)
    return ImageEnhance.Color(out).enhance(sat)

def glass_panel(img, x, y, w, h, r=40, ax=0.0, ay=0.0, content=None, cpos=(0, 0), sweep=0.5, tint=(255, 255, 255),
                depth=1.0, alpha=1.0, border=3, tint_a=34):
    """Panneau de verre cristal pose sur img : retourne rien, dessine directement."""
    x, y, w, h = int(x), int(y), int(w), int(h)
    pad = 70
    L = Image.new('RGBA', (w + 2 * pad, h + 2 * pad), (0, 0, 0, 0))
    mask = rrect_mask(w, h, r)
    # 1. verre depoli : le fond derriere, flou et eclairci
    fr = frost_patch(img, x, y, w, h).convert('RGBA')
    fr.putalpha(mask)
    L.paste(fr, (pad, pad), fr)
    # 2. teinte, plus lumineuse en haut
    yy = np.linspace(0, 1, h)[:, None]
    ta = (tint_a * (1.15 - 0.85 * yy)).astype(np.uint8)
    tl = Image.new('RGBA', (w, h), tuple(tint) + (0,))
    tl.putalpha(Image.fromarray(np.broadcast_to(ta, (h, w)).copy(), 'L'))
    tl.putalpha(ImageChops.multiply(tl.getchannel('A'), mask))
    L.alpha_composite(tl, (pad, pad))
    # 3. contenu pose sous le verre
    if content is not None:
        L.alpha_composite(content, (pad + cpos[0], pad + cpos[1]))
    # 4. eclat interieur le long du bord
    inner = rrect_mask(w - 2 * border * 3, h - 2 * border * 3, max(2, r - border * 3))
    innerfull = Image.new('L', (w, h), 0); innerfull.paste(inner, (border * 3, border * 3))
    edge = ImageChops.subtract(mask, innerfull).filter(ImageFilter.GaussianBlur(7))
    glow = Image.new('RGBA', (w, h), (190, 220, 255, 0)); glow.putalpha(edge.point(lambda v: int(v * 0.55)))
    L.alpha_composite(glow, (pad, pad))
    # 5. biseau : anneau lumineux, clair en haut a gauche, bleute en bas a droite
    ring_in = rrect_mask(w - 2 * border, h - 2 * border, max(2, r - border))
    rin = Image.new('L', (w, h), 0); rin.paste(ring_in, (border, border))
    ring = ImageChops.subtract(mask, rin)
    gx = np.linspace(0, 1, w)[None, :]; gy = np.linspace(0, 1, h)[:, None]
    gt = np.clip(0.5 * gx + 0.5 * gy, 0, 1)
    ga = (235 * (1 - gt) + 55 * gt)
    rgb = np.stack([np.broadcast_to(255 * (1 - gt) + 120 * gt, (h, w)), np.broadcast_to(255 * (1 - gt) + 175 * gt, (h, w)),
                    np.broadcast_to(255 * np.ones_like(gt), (h, w))], axis=-1).astype(np.uint8)
    bev = Image.fromarray(rgb, 'RGB').convert('RGBA')
    bev.putalpha(Image.fromarray(np.broadcast_to(ga, (h, w)).astype(np.uint8), 'L'))
    bev.putalpha(ImageChops.multiply(bev.getchannel('A'), ring))
    L.alpha_composite(bev, (pad, pad))
    # 6. reflets : bande speculaire qui balaie, brillance courbe en haut
    uu = (np.linspace(0, 1, w)[None, :] * 0.8 + np.linspace(0, 1, h)[:, None] * 0.55) / 1.35
    band = np.exp(-((uu - (sweep * 1.6 - 0.3)) / 0.055) ** 2) * 70 + np.exp(-((uu - (sweep * 1.6 - 0.4)) / 0.02) ** 2) * 50
    sp = Image.fromarray(np.clip(band, 0, 255).astype(np.uint8), 'L')
    sp = ImageChops.multiply(sp, mask)
    spl = Image.new('RGBA', (w, h), (255, 255, 255, 0)); spl.putalpha(sp)
    L.alpha_composite(spl, (pad, pad))
    gl = Image.new('L', (w, h), 0)
    ImageDraw.Draw(gl).ellipse((-w * 0.25, -h * 1.05, w * 1.25, h * 0.32), fill=255)
    gl = gl.filter(ImageFilter.GaussianBlur(10))
    gv = np.linspace(1, 0, h)[:, None]
    gloss = ImageChops.multiply(ImageChops.multiply(gl, mask), Image.fromarray(np.broadcast_to((gv * 60).astype(np.uint8), (h, w)).copy(), 'L'))
    gll = Image.new('RGBA', (w, h), (255, 255, 255, 0)); gll.putalpha(gloss)
    L.alpha_composite(gll, (pad, pad))
    # 7. perspective, ombre portee, composition
    Lw = warp(L, pad, w, h, ax, ay)
    sh = Lw.getchannel('A').filter(ImageFilter.GaussianBlur(26 * depth))
    shl = Image.new('RGBA', Lw.size, (2, 6, 24, 0)); shl.putalpha(sh.point(lambda v: int(v * 0.55 * alpha)))
    img.paste(shl, (x - pad, y - pad + int(30 * depth)), shl)
    overlay(img, Lw, (x - pad, y - pad), alpha)

# ---------------------------------------------------------------- captures detourees (l'encoche seule, sans son fond)
_cuts = {}
def cutout(name, crop, scale):
    key = (name, crop, scale)
    if key in _cuts: return _cuts[key]
    src = asset(name).crop(crop)
    a = np.asarray(src).astype(float)
    h, w, _ = a.shape
    left, right = a[:, :3, :].mean(axis=1), a[:, -3:, :].mean(axis=1)
    fx = np.linspace(0, 1, w)[None, :, None]
    bgc = left[:, None, :] * (1 - fx) + right[:, None, :] * fx
    dist = np.abs(a - bgc).max(axis=2)
    near = (dist < 22).astype(np.uint8) * 255
    m = Image.fromarray(near, 'L')
    # ce qui touche le bord et ressemble au fond est exterieur ; le reste (encoche, ombre) est conserve
    flood = m.copy()
    for pt in [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1), (w // 2, h - 1), (0, h // 2), (w - 1, h // 2)]:
        if flood.getpixel(pt) == 255: ImageDraw.floodfill(flood, pt, 128)
    outside = np.asarray(flood) == 128
    alpha = np.where(outside, np.clip((dist - 6) / 55.0, 0, 1) * 255, 255).astype(np.uint8)
    rgb = np.where(outside[:, :, None], 0, a).astype(np.uint8)
    out = Image.fromarray(np.dstack([rgb, alpha]), 'RGBA')
    out = out.resize((int(w * scale), int(h * scale)), Image.LANCZOS)
    _cuts[key] = out
    return out

def sprite(img, name, cx, cy, size, rot=0.0, alpha=1.0):
    if alpha <= 0.01 or size < 2: return
    s = asset('sprite:' + name).resize((int(size), int(size)), Image.LANCZOS)
    if rot: s = s.rotate(rot, resample=Image.BICUBIC, expand=True)
    overlay(img, s, (int(cx - s.width / 2), int(cy - s.height / 2)), alpha)

def cursor(img, x, y, alpha=1.0):
    pts = [(0, 0), (0, 26), (6, 20), (11, 31), (16, 29), (11, 18), (19, 18)]
    pts = [(x + px * 1.3, y + py * 1.3) for px, py in pts]
    lay = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    d.polygon([(px + 2, py + 4) for px, py in pts], fill=(0, 0, 0, 90))
    d.polygon(pts, fill=(255, 255, 255, 255), outline=(20, 24, 40, 255))
    overlay(img, lay, (0, 0), alpha)

def ripple(img, x, y, p):
    if p <= 0 or p >= 1: return
    r = int(16 + 80 * ease_out(p)); a = int(220 * (1 - p))
    lay = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(lay).ellipse((x - r, y - r, x + r, y + r), outline=(180, 235, 255, a), width=5)
    overlay(img, lay)

# ---------------------------------------------------------------- orbe de cristal
def crystal_orb(img, cx, cy, r, color, t, sprite_name=None, sprite_size=None, rot=0.0, dy=0.0, alpha=1.0, floor=True):
    if r < 6 or alpha <= 0.01: return
    r = int(r)
    # lueur coloree et reflet sur le sol
    if floor:
        fl = Image.new('RGBA', (int(r * 4), int(r * 1.2)), (0, 0, 0, 0))
        ImageDraw.Draw(fl).ellipse((r * 0.6, r * 0.45, r * 3.4, r * 0.95), fill=tuple(color) + (110,))
        fl = fl.filter(ImageFilter.GaussianBlur(r * 0.16))
        overlay(img, fl, (int(cx - r * 2), int(cy + r * 1.02)), alpha)
    glw = Image.new('RGBA', (r * 4, r * 4), (0, 0, 0, 0))
    ImageDraw.Draw(glw).ellipse((r * 0.9, r * 0.9, r * 3.1, r * 3.1), fill=tuple(color) + (90,))
    glw = glw.filter(ImageFilter.GaussianBlur(r * 0.35))
    overlay(img, glw, (int(cx - r * 2), int(cy - r * 2)), alpha)
    # verre depoli dans le disque
    size = 2 * r
    cm = Image.new('L', (size, size), 0)
    ImageDraw.Draw(cm).ellipse((0, 0, size - 1, size - 1), fill=255)
    cm = cm.resize((size, size), Image.LANCZOS)
    fr = frost_patch(img, cx - r, cy - r, size, size, blur=18, lift=1.25, sat=1.4).convert('RGBA')
    fr.putalpha(cm.point(lambda v: int(v * 0.95 * alpha)))
    img.paste(fr, (int(cx - r), int(cy - r)), fr)
    # couleur du cristal : plus sombre au centre, vive vers le bord (Fresnel)
    yy, xx = np.mgrid[0:size, 0:size]
    d = np.sqrt((xx - r) ** 2 + (yy - r) ** 2) / r
    fres = np.clip(d, 0, 1) ** 3.2
    ca = (16 + 135 * fres) * (d <= 1)
    tl = np.zeros((size, size, 4), np.uint8)
    tl[..., 0], tl[..., 1], tl[..., 2] = color
    tl[..., 3] = np.clip(ca * 0.75, 0, 255).astype(np.uint8)
    tl = Image.fromarray(tl, 'RGBA'); tl.putalpha(ImageChops.multiply(tl.getchannel('A'), cm.point(lambda v: int(v * alpha))))
    img.paste(tl, (int(cx - r), int(cy - r)), tl)
    # la mascotte dans le cristal
    if sprite_name:
        sprite(img, sprite_name, cx, cy + dy, sprite_size or size * 0.78, rot, alpha)
    # reflets au-dessus : lumiere haut-gauche, contre-jour bas-droite, anneau
    hl = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    hd = ImageDraw.Draw(hl)
    hd.ellipse((r * 0.34, r * 0.14, r * 1.02, r * 0.56), fill=(255, 255, 255, 125))
    hd.ellipse((r * 0.40, r * 0.20, r * 0.98, r * 0.52), fill=(255, 255, 255, 70))
    hl = hl.filter(ImageFilter.GaussianBlur(r * 0.05))
    # croissant lumineux le long du bord haut-gauche
    cres = Image.new('L', (size, size), 0)
    cd_ = ImageDraw.Draw(cres)
    cd_.ellipse((r * 0.06, r * 0.06, size - r * 0.06, size - r * 0.06), fill=255)
    cd_.ellipse((r * 0.16, r * 0.20, size - r * 0.02, size - r * 0.02), fill=0)
    cres = cres.filter(ImageFilter.GaussianBlur(r * 0.025)).point(lambda v: int(v * 0.55))
    cl = Image.new('RGBA', (size, size), (255, 255, 255, 0)); cl.putalpha(cres)
    hl.alpha_composite(cl)
    sw = int((math.sin(t * 0.9) * 0.5 + 0.5) * 30)
    hd2 = ImageDraw.Draw(hl)
    hd2.ellipse((r * 1.1, r * 1.3, r * 1.7, r * 1.75), fill=tuple(min(255, c + 60) for c in color) + (40 + sw // 2,))
    hl2 = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    ImageDraw.Draw(hl2).ellipse((1, 1, size - 2, size - 2), outline=(235, 245, 255, 210), width=max(2, r // 55))
    hl2 = hl2.filter(ImageFilter.GaussianBlur(0.8))
    hl.alpha_composite(hl2)
    hl.putalpha(ImageChops.multiply(hl.getchannel('A'), cm))
    overlay(img, hl, (int(cx - r), int(cy - r)), alpha)

# ---------------------------------------------------------------- scenes
def desktop_layer(w, h, sel_idx):
    L = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(L)
    d.rectangle((0, 0, w, 56), fill=(30, 34, 52, 170))
    for i, c in enumerate([(255, 95, 86), (255, 189, 46), (39, 201, 63)]):
        d.ellipse((26 + i * 30, 20, 42 + i * 30, 36), fill=c + (255,))
    d.text((w / 2, 28), 'Claude Desktop', font=font('r', 22), fill=(200, 214, 240, 255), anchor='mm')
    for i, nm in enumerate(['Migrations staging', 'API commandes', 'Refonte accueil']):
        d.rounded_rectangle((18, 84 + i * 58, 236, 130 + i * 58), radius=12, fill=(255, 255, 255, 46 if i == 0 else 14))
        d.text((34, 107 + i * 58), nm, font=font('r', 20), fill=(232, 238, 252, 255), anchor='lm')
    d.text((280, 112), "Quelle base de données pour les tests d'intégration ?", font=font('b', 25), fill=(244, 248, 255, 255), anchor='lm')
    for i, o in enumerate(['PostgreSQL en conteneur', 'SQLite en mémoire', 'Base de staging partagée', 'Autre…']):
        yy = 164 + i * 86
        picked = sel_idx == i
        d.rounded_rectangle((280, yy, w - 32, yy + 70), radius=16, fill=(70, 200, 130, 120) if picked else (255, 255, 255, 30),
                            outline=(150, 255, 200, 230) if picked else (255, 255, 255, 90), width=2)
        d.text((306, yy + 35), o, font=font('r', 23), fill=(244, 248, 255, 255), anchor='lm')
    return L

def s1(t):  # 0-5 : le probleme
    img = bg_deep(t, 'dark', accent=(255, 80, 70))
    appear = ease_out(seg(t, 0.0, 0.7))
    tx, ty, tw, th = 220, 170, 1060, 700
    shadow = Image.new('RGBA', (tw + 160, th + 160), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle((80, 80, 80 + tw, 80 + th), radius=24, fill=(0, 0, 0, 170))
    shadow = shadow.filter(ImageFilter.GaussianBlur(30))
    overlay(img, shadow, (tx - 80, ty - 50), appear)
    term = rrect_layer(tw, th, 24, (18, 21, 30, 255))
    d = ImageDraw.Draw(term)
    d.rectangle((0, 0, tw, 56), fill=(32, 36, 50, 255))
    for i, c in enumerate([(255, 95, 86), (255, 189, 46), (39, 201, 63)]):
        d.ellipse((26 + i * 30, 20, 42 + i * 30, 36), fill=c + (255,))
    d.text((tw / 2, 28), 'claude  ·  data-pipeline', font=font('r', 22), fill=(170, 182, 210, 255), anchor='mm')
    lines = ['> Applique les migrations sur la base de staging', '', '  Je lis migrations/042_orders.sql …', '  Je prépare la commande :', '',
             '    npm run migrate -- --env=staging', '', '  Exécuter cette commande ?   [ Autoriser ]  [ Refuser ]']
    n = int(t * 7)
    for i, ln in enumerate(lines[:n]):
        col = (214, 222, 240, 255) if i != 7 else (255, 150, 140, 255)
        d.text((38, 104 + i * 46), ln, font=font('m', 28), fill=col)
    if n < len(lines) and int(t * 2) % 2 == 0:
        d.rectangle((38 + 6 * 17, 104 + n * 46 + 4, 38 + 6 * 17 + 14, 104 + n * 46 + 34), fill=(214, 222, 240, 255))
    if t > 1.6:
        minutes = int(lerp(1, 11, seg(t, 1.6, 4.6)))
        d.text((38, th - 46), f"⏸ en attente d'une réponse · {minutes} min", font=font('mb', 28), fill=(255, 120, 110, 255), anchor='lm')
    overlay(img, term, (tx, ty), appear)
    # fenetre de verre givre posee par-dessus : on devine le terminal derriere, flou
    gx, gy, gw, gh = 700, 470, 1000, 540
    a = ease_out(seg(t, 0.3, 1.1))
    cont = Image.new('RGBA', (gw, gh), (0, 0, 0, 0))
    cd = ImageDraw.Draw(cont)
    cd.rectangle((0, 0, gw, 54), fill=(255, 255, 255, 40))
    cd.text((gw / 2, 27), 'Navigateur  ·  Documentation', font=font('r', 22), fill=(230, 238, 255, 255), anchor='mm')
    for i in range(6):
        cd.rounded_rectangle((44, 92 + i * 70, 44 + (790 if i % 3 else 640), 92 + i * 70 + 28), radius=14, fill=(255, 255, 255, 60))
    glass_panel(img, gx, gy + int((1 - a) * 40), gw, gh, 34, ax=2, ay=-5, content=cont, sweep=0.25 + 0.15 * t / 5, alpha=a, tint_a=40)
    if t > 2.4:
        text(img, 'Il attend. Personne ne le sait.', W / 2, 92, 76, LIGHT, 'b', ease_out(seg(t, 2.4, 3.2)), glow=(255, 90, 80))
    return img

def s2(t):  # 5-12 : arrivee de Rinel
    img = bg_deep(t + 5, 'blue')
    drop = ease_out(seg(t, 0.0, 0.9))
    row = ease_in_out(seg(t, 4.6, 5.6))
    nw, nh = int(lerp(240, 330, row)), int(lerp(64, 80, row))
    ny = lerp(-90, 0, drop)
    # encoche : noir brillant, reflet en bordure
    sh = Image.new('RGBA', (nw + 120, nh + 120), (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle((60, 20, 60 + nw, 60 + nh), radius=30, fill=(0, 0, 0, 150))
    sh = sh.filter(ImageFilter.GaussianBlur(16))
    overlay(img, sh, (int(W / 2 - nw / 2 - 60), int(ny) - 10))
    big = rrect_layer(nw, nh + 40, 30, (4, 5, 9, 255), top_flat=True)
    ImageDraw.Draw(big).rounded_rectangle((1, -60, nw - 2, nh + 38), radius=30, outline=(150, 190, 255, 120), width=2)
    overlay(img, big, (int(W / 2 - nw / 2), int(ny - 40)))
    # orbe de cristal + Rinel
    pop = ease_out(seg(t, 0.9, 1.6))
    shrink = ease_in_out(seg(t, 4.0, 4.9))
    size = lerp(420 * pop, 46, shrink)
    cx, cy = lerp(W / 2, W / 2 - 3 * 28, shrink), lerp(450, 36, shrink)
    wave = 9 * math.sin((t - 1.6) * 9) * (1 - shrink) if 1.6 < t < 4.2 else 0
    float_ = 10 * math.sin(t * 3) * (1 - shrink)
    if shrink < 1:
        crystal_orb(img, cx, cy + float_, lerp(250 * pop, 0, shrink), (110, 170, 255), t, 'coucou', size * 0.95, wave, alpha=1 - shrink * 0.6)
    fade = 1 - seg(t, 3.7, 4.3)
    text(img, 'Rinel', W / 2, 800, 140, LIGHT, 'b', ease_out(seg(t, 1.7, 2.4)) * fade, glow=CYAN)
    text(img, "L'encoche Claude Code pour Windows", W / 2, 910, 46, SOFT, 'r', ease_out(seg(t, 2.1, 2.8)) * fade)
    for i, name in enumerate(['en-cours', 'en-cours', 'en-cours', 'pret']):
        a = seg(t, 4.8 + i * 0.18, 5.3 + i * 0.18)
        if a > 0: sprite(img, name, W / 2 - 3 * 28 + i * 56 + (0 if i < 4 else 0), 36 + 2 * math.sin(t * 4 + i), 46 * ease_out(a), alpha=a)
    if t > 5.4:
        caption(img, 'Un petit chevalier par session', 'collé en haut de ton écran', y=340, alpha=ease_out(seg(t, 5.4, 6.2)))
    return img

def s3(t):  # 12-22 : les etats
    img = bg_deep(t + 12, 'blue')
    caption(img, "Un coup d'œil suffit", "qui travaille, qui a fini, qui t'attend", y=120, alpha=ease_out(seg(t, 0.0, 0.6)))
    items = [('en-cours', ORANGE, 'En cours', 'il travaille'), ('pret', GREEN, 'Prêt', 'il a fini'),
             ('attente', RED, "Il t'attend", 'une permission ou une question'), ('erreur', GREY, 'Erreur', "le tour s'est arrêté")]
    xs = [370, 760, 1150, 1540]
    for i, (name, col, label, sub) in enumerate(items):
        a = ease_out(seg(t, 0.8 + i * 1.5, 1.5 + i * 1.5))
        if a <= 0: continue
        tt = t - (0.8 + i * 1.5)
        rot = 0.0; dy = 0.0; sc = 1.0
        if name == 'en-cours': dy = 12 * math.sin(tt * 3.2); rot = 3 * math.sin(tt * 1.6)
        elif name == 'pret': dy = -34 * abs(math.sin(tt * 2.6)) * (1 if tt > 0.6 else 0)
        elif name == 'attente':
            rot = 4 * math.sin(tt * 11) * (1 if int(tt * 1.3) % 3 == 2 else 0.35); sc = 1 + 0.03 * math.sin(tt * 6)
        else: dy = 8; rot = -4
        r = 150 * (0.75 + 0.25 * a)
        crystal_orb(img, xs[i], 540 + (1 - a) * 50, r, col, t + i, name, 2 * r * 0.82 * sc, rot, dy=dy, alpha=a)
        pill = rrect_layer(250, 58, 29, tuple(col) + (235,))
        overlay(img, pill, (xs[i] - 125, 770), a)
        text(img, label, xs[i], 799, 34, (255, 255, 255) if name != 'pret' else (10, 60, 34), 'b', a)
        text(img, sub, xs[i], 862, 28, SOFT, 'r', a * 0.95)
    text(img, "Même sans les couleurs : plume, parchemin et pastille racontent l'état", W / 2, 985, 34, SOFT, 'r', ease_out(seg(t, 7.2, 8.0)))
    return img

CROPS = {'repliee': ((330, 0, 470, 70), 3.2), 'entrouverte': ((220, 0, 580, 110), 2.6), 'permission': ((150, 0, 650, 402), 1.7)}
def s4(t):  # 22-34 : repondre dans l'encoche
    img = bg_deep(t + 22, 'blue')
    caption(img, "Réponds dans l'encoche", 'sans changer de fenêtre', y=100, alpha=ease_out(seg(t, 0.0, 0.7)))
    hs = [(0.0, 330), (2.2, 330), (2.8, 420), (4.2, 420), (4.9, 760), (8.9, 760), (9.5, 330)]
    h = hs[0][1]
    for (a0, v0), (a1, v1) in zip(hs, hs[1:]):
        if a0 <= t <= a1: h = lerp(v0, v1, ease_in_out(seg(t, a0, a1)) if v0 != v1 else 0)
        elif t > a1: h = v1
    pw = 980
    px, py = int(W / 2 - pw / 2), 190
    ay = 4 * math.sin(t * 0.55); ax = 1.5 * math.sin(t * 0.4 + 1)
    stages = [('repliee', 0.4, 2.4), ('entrouverte', 2.2, 4.4), ('permission', 4.2, 9.0), ('repliee', 8.9, 12.5)]
    cont = Image.new('RGBA', (pw, int(h)), (0, 0, 0, 0))
    for name, a0, a1 in stages:
        fin = seg(t, a0, a0 + 0.35); fout = 1 - seg(t, a1 - 0.35, a1) if a1 < 12.4 else 1.0
        al = fin * fout
        if al <= 0.01: continue
        crop, sc = CROPS[name]
        cut = cutout(name, crop, sc)
        lay = Image.new('RGBA', cont.size, (0, 0, 0, 0)); lay.alpha_composite(cut, (int((pw - cut.width) / 2), 56))
        lay.putalpha(lay.getchannel('A').point(lambda v, al=al: int(v * al)))
        cont.alpha_composite(lay)
    glass_panel(img, px, py, pw, h, 44, ax=ax, ay=ay, content=cont, sweep=(t * 0.12) % 1.0, alpha=ease_out(seg(t, 0.2, 0.9)))
    if 2.2 < t < 4.4:
        text(img, 'Il entrouvre pour dire qui attend', W / 2, 640, 40, SOFT, 'r', seg(t, 2.6, 3.2) * (1 - seg(t, 4.0, 4.4)))
    crop, sc = CROPS['permission']
    cutw = (crop[2] - crop[0]) * sc
    ox, oy = px + (pw - cutw) / 2 + (246 - crop[0]) * sc, py + 56 + 202 * sc
    tx, ty = proj(ox, oy, px + pw / 2, py + h / 2, ax, ay)
    if 6.0 < t < 9.2:
        cx = lerp(1350, tx, ease_in_out(seg(t, 6.0, 7.6))); cy = lerp(840, ty, ease_in_out(seg(t, 6.0, 7.6)))
        cursor(img, cx, cy, alpha=seg(t, 6.0, 6.4) * (1 - seg(t, 8.9, 9.2)))
        ripple(img, tx, ty, (t - 8.2) / 0.7)
    if 4.4 < t < 8.6:
        text(img, 'Autoriser · Refuser · Répondre dans Claude', W / 2, 1035, 36, SOFT, 'r', seg(t, 4.8, 5.4) * (1 - seg(t, 8.2, 8.7)))
    if t > 9.3:
        text(img, '✓ Autorisé : la session repart', W / 2, 560, 52, (150, 255, 200), 's', ease_out(seg(t, 9.4, 10.0)), glow=(40, 200, 120))
    return img

def s5(t):  # 34-44 : les deux interfaces
    img = bg_deep(t + 34, 'violet')
    caption(img, "Dans l'encoche et dans Claude Desktop", 'la première réponse gagne', y=100, alpha=ease_out(seg(t, 0.0, 0.7)), size=62, glow=VIOLET)
    appear = ease_out(seg(t, 0.4, 1.1))
    cur, t0 = ('left', t) if t < 5.0 else ('right', t - 5.0)
    lw, lh = 940, 640; lx, ly = 70, 250
    rw, rh = 640, 700; rx, ry = 1210, 220
    ayL = 5 + 2 * math.sin(t * 0.5); ayR = -6 + 2 * math.sin(t * 0.6 + 1)
    if cur == 'left':
        sel = 0 if t0 > 3.0 else None
        ralpha = appear * (1 - seg(t0, 3.3, 3.8)); lalpha = appear
    else:
        sel = None; lalpha = ease_out(seg(t0, 0.0, 0.6)) * (1 - seg(t0, 3.3, 3.8)); ralpha = ease_out(seg(t0, 0.0, 0.6))
    # fenetre Claude Desktop (a gauche)
    if lalpha > 0.02:
        glass_panel(img, lx, ly + int((1 - appear) * 40), lw, lh, 36, ax=1.5, ay=ayL, content=desktop_layer(lw, lh, sel), cpos=(0, 0),
                    sweep=(t * 0.1) % 1, alpha=lalpha, tint_a=30)
    # carte de l'encoche (a droite)
    if ralpha > 0.02:
        cut = cutout('question', (150, 0, 650, 560), 1.12)
        cont = Image.new('RGBA', (rw, rh), (0, 0, 0, 0)); cont.alpha_composite(cut, ((rw - cut.width) // 2, 50))
        glass_panel(img, rx, ry + int((1 - appear) * 40), rw, rh, 40, ax=-1.5, ay=ayR, content=cont, sweep=(t * 0.1 + 0.5) % 1, alpha=ralpha)
    if cur == 'left':
        px_, py_ = proj(lx + 280 + 270, ly + 164 + 35, lx + lw / 2, ly + lh / 2, 1.5, ayL)
        if 1.2 < t0 < 3.6:
            cx = lerp(900, px_, ease_in_out(seg(t0, 1.2, 2.8))); cy = lerp(930, py_, ease_in_out(seg(t0, 1.2, 2.8)))
            cursor(img, cx, cy, alpha=seg(t0, 1.2, 1.6) * (1 - seg(t0, 3.4, 3.6)))
            ripple(img, px_, py_, (t0 - 3.0) / 0.6)
        if t0 > 3.8:
            text(img, "✓ Répondu dans Claude Desktop : la carte de l'encoche disparaît", W / 2, 990, 40, (150, 255, 200), 's',
                 ease_out(seg(t0, 3.9, 4.5)) * (1 - seg(t, 4.7, 5.0)), glow=(40, 200, 120))
    else:
        cut = cutout('question', (150, 0, 650, 560), 1.12)
        ox = rx + (rw - cut.width) // 2 + (400 - 150) * 1.12; oy = ry + 50 + 225 * 1.12
        px_, py_ = proj(ox, oy, rx + rw / 2, ry + rh / 2, -1.5, ayR)
        if 0.8 < t0 < 3.6:
            cx = lerp(1000, px_, ease_in_out(seg(t0, 0.8, 2.4))); cy = lerp(930, py_, ease_in_out(seg(t0, 0.8, 2.4)))
            cursor(img, cx, cy, alpha=seg(t0, 0.8, 1.2) * (1 - seg(t0, 3.4, 3.6)))
            ripple(img, px_, py_, (t0 - 2.5) / 0.6)
        if t0 > 3.8:
            text(img, "✓ Répondu dans l'encoche : Claude Desktop se met à jour", W / 2, 990, 40, (150, 255, 200), 's', ease_out(seg(t0, 3.9, 4.5)), glow=(40, 200, 120))
    return img

def report_layer(w, h, y0):
    src = asset('rapport-audit')
    sc = w / 1280.0
    crop = src.crop((0, y0, 1280, y0 + int(h / sc)))
    return crop.resize((w, h), Image.LANCZOS).convert('RGBA')

def s6(t):  # 44-52 : confiance
    img = bg_deep(t + 44, 'blue')
    caption(img, 'Tout ce que font les agents est journalisé', "chaîne d'empreintes · règles que tu crées · rien ne quitte ton poste", y=100,
            alpha=ease_out(seg(t, 0.0, 0.7)), size=58)
    pan = ease_in_out(seg(t, 0.6, 6.5))
    y0 = int(lerp(0, 260, pan))
    shift = ease_in_out(seg(t, 4.2, 5.4))
    rw_, rh_ = 1100, 640
    pw, ph = rw_ + 40, rh_ + 40
    cx_ = lerp(W / 2, 640, shift)
    a = ease_out(seg(t, 0.3, 1.0))
    ay = lerp(-3, 4, shift) + 2 * math.sin(t * 0.5)
    rep = rrect_mask(rw_, rh_, 18)
    layer = report_layer(rw_, rh_, y0); layer.putalpha(rep)
    glass_panel(img, cx_ - pw / 2, 230, pw, ph, 40, ax=1.0, ay=ay, content=layer, cpos=(20, 20), sweep=(t * 0.1) % 1, alpha=a, tint_a=22)
    if t > 4.0:
        p = ease_out(seg(t, 4.0, 5.0))
        cut = cutout('regle', (150, 0, 650, 509), 1.05)
        cont = Image.new('RGBA', (cut.width + 70, cut.height + 90), (0, 0, 0, 0)); cont.alpha_composite(cut, (35, 36))
        glass_panel(img, 1500 - cont.width / 2 + (1 - p) * 500, 215, cont.width, cont.height, 40, ax=-1.0, ay=-8 + 2 * math.sin(t * 0.6), content=cont,
                    sweep=(t * 0.15 + 0.3) % 1, alpha=p, depth=1.6)
        text(img, 'Après 5 autorisations : « en faire une règle ? »', 1500, 840, 30, LIGHT, 'b', ease_out(seg(t, 5.2, 5.9)), glow=CYAN)
    return img

def s7(t):  # 52-60 : final
    img = bg_deep(t + 52, 'violet')
    a = ease_out(seg(t, 0.0, 0.8))
    wave = 9 * math.sin(t * 8) if 0.8 < t < 3.6 else 0
    crystal_orb(img, W / 2, 400 + 8 * math.sin(t * 3), 250 * (0.8 + 0.2 * a), (120, 150, 255), t, 'coucou', 410 * (0.8 + 0.2 * a), wave, alpha=a)
    text(img, 'Rinel', W / 2, 735, 140, LIGHT, 'b', ease_out(seg(t, 0.5, 1.3)), glow=VIOLET)
    text(img, "L'encoche qui veille sur tes sessions", W / 2, 840, 46, SOFT, 'r', ease_out(seg(t, 0.9, 1.7)))
    text(img, 'github.com/ATX-AI-Dev/bip', W / 2, 935, 46, (150, 215, 255), 'b', ease_out(seg(t, 1.6, 2.4)), glow=CYAN)
    text(img, "Fork de CR0ZER/bip · licence MIT · Windows 10 et 11", W / 2, 1005, 30, SOFT, 'r', ease_out(seg(t, 2.0, 2.8)) * 0.9)
    return img

SCENES = [(0, 5, s1), (5, 12, s2), (12, 22, s3), (22, 34, s4), (34, 44, s5), (44, 52, s6), (52, 60.001, s7)]

def render(t):
    for a, b, fn in SCENES:
        if a <= t < b:
            img = fn(t - a)
            if t - a < 0.5 and a > 0:
                prev = [s for s in SCENES if s[1] == a][0]
                p = prev[2](a - 0.001 - prev[0])
                img = Image.blend(p, img, ease_in_out((t - a) / 0.5))
            break
    else:
        img = bg_deep(t)
    fi, fo = seg(t, 0, 0.4), 1 - seg(t, DUR - 0.6, DUR)
    k = min(fi, fo)
    if k < 1.0:
        img = Image.blend(Image.new('RGB', (W, H), (6, 8, 16)), img, k)
    return img.convert('RGB')

def work(i):
    out = os.path.join(HERE, 'frames', '%05d.png' % i)
    render(i / FPS).save(out, compress_level=1)
    return i

# ---------------------------------------------------------------- audio
SR = 44100
def pluck(freq, dur=1.6, g=0.9965, bright=0.5):
    L = int(SR / freq)
    n = int(SR * dur)
    rng = np.random.default_rng(int(freq * 10))
    blk = rng.uniform(-1, 1, L)
    blk = np.convolve(blk, [0.5, 0.5], mode='same') if bright < 1 else blk
    out = [blk]
    prev2 = np.zeros(L)
    for k in range(1, n // L + 1):
        prev = out[-1]
        shifted = np.concatenate([[out[-2][-1] if k > 1 else 0.0], prev[:-1]])
        out.append(g * 0.5 * (prev + shifted))
    y = np.concatenate(out)[:n]
    y *= np.exp(-np.linspace(0, 3.0, n)) ** 0.5
    return y

def note(name):
    names = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}
    base = names[name[0]]; acc = 0
    rest = name[1:]
    if rest.startswith('b'): acc = -1; rest = rest[1:]
    return 440.0 * 2 ** ((base + acc + (int(rest) - 4) * 12 - 9) / 12)

def mix_in(buf, y, at, vol):
    i = int(at * SR)
    if i >= len(buf): return
    n = min(len(y), len(buf) - i)
    buf[i:i + n] += y[:n] * vol

def audio():
    total = int(DUR * SR)
    buf = np.zeros(total)
    bpm = 84; beat = 60 / bpm; eighth = beat / 2
    chords = [('D', ['D3', 'F3', 'A3', 'D4', 'F4', 'A4']), ('C', ['C3', 'E3', 'G3', 'C4', 'E4', 'G4']),
              ('Bb', ['Bb2', 'D3', 'F3', 'Bb3', 'D4', 'F4']), ('C', ['C3', 'E3', 'G3', 'C4', 'E4', 'G4'])]
    pat = [0, 2, 4, 2, 3, 5, 4, 2]
    bar = beat * 4
    # nappe douce sur toute la duree
    t = np.arange(total) / SR
    for bi in range(int(DUR / bar) + 1):
        root, tones = chords[bi % 4]
        t0 = bi * bar
        env = np.clip((t - t0) / 0.6, 0, 1) * np.clip((t0 + bar + 0.5 - t) / 1.2, 0, 1)
        for nm in tones[:3]:
            f = note(nm)
            buf += 0.018 * env * (np.sin(2 * np.pi * f * t) + 0.3 * np.sin(2 * np.pi * 2 * f * t))
    # intro tendue 0-5 s : pouls grave
    for k in range(int(5 / beat)):
        mix_in(buf, pluck(note('D2'), 1.0, 0.99), k * beat, 0.35 if k % 2 == 0 else 0.2)
    # luth des 5 s
    start = 5.0
    steps = int((DUR - start - 1.0) / eighth)
    for s in range(steps):
        at = start + s * eighth
        bi = int((at - start) / bar)
        root, tones = chords[bi % 4]
        idx = pat[s % 8]
        vel = 0.30 if s % 8 == 0 else 0.22
        mix_in(buf, pluck(note(tones[idx]), 1.4), at, vel)
        if s % 8 == 0: mix_in(buf, pluck(note(tones[0]) / 2, 1.8, 0.998), at, 0.30)
    # petite melodie : toutes les 2 mesures, a partir de 12 s
    mel = [('A4', 0), ('G4', 1), ('F4', 2), ('E4', 3), ('F4', 4), ('A4', 6), ('D5', 8)]
    for rep in range(int((DUR - 14) / (bar * 4))):
        base = 14 + rep * bar * 4 + (0 if rep % 2 == 0 else 0)
        for nm, e in mel:
            mix_in(buf, pluck(note(nm), 1.6, 0.997), base + e * eighth, 0.33)
    # bruitages
    def pop(at, f0=720):
        n = int(1.1 * SR); tt = np.arange(n) / SR
        y = np.zeros(n)
        for k, (m, d_, g_) in enumerate([(1.0, 5.0, 1.0), (2.76, 7.0, 0.5), (5.4, 11.0, 0.25), (8.93, 16.0, 0.12)]):
            y += g_ * np.sin(2 * np.pi * f0 * 1.5 * m * tt) * np.exp(-tt * d_)
        y *= np.clip(tt / 0.004, 0, 1)
        mix_in(buf, y, at, 0.16)
    def click(at):
        n = int(0.09 * SR); tt = np.arange(n) / SR
        y = (np.sin(2 * np.pi * 1500 * tt) * np.exp(-tt * 70) + 0.4 * np.random.default_rng(3).uniform(-1, 1, n) * np.exp(-tt * 200))
        mix_in(buf, y, at, 0.35)
    for a in (5.9, 13.9, 15.4, 16.9, 18.4, 25.0, 28.6, 31.1, 36.0, 39.4, 40.0, 44.6, 52.4):
        pop(a, 640 + (hash(a) % 5) * 60)
    for a in (22 + 8.2 + 0.0, 34 + 3.0, 34 + 8.5):
        click(a)
    # fondus + normalisation
    buf *= np.clip(t / 1.0, 0, 1) * np.clip((DUR - t) / 2.0, 0, 1)
    buf /= max(1e-6, np.max(np.abs(buf))) / 0.85
    pcm = (buf * 32767).astype('<i2')
    with wave.open(os.path.join(HERE, 'audio.wav'), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
    print('audio ok')

if __name__ == '__main__':
    import multiprocessing as mp
    mode = sys.argv[1]
    if mode == 'audio':
        audio()
    elif mode == 'frames':
        a = int(sys.argv[2]) if len(sys.argv) > 2 else 0
        b = int(sys.argv[3]) if len(sys.argv) > 3 else int(DUR * FPS)
        os.makedirs(os.path.join(HERE, 'frames'), exist_ok=True)
        with mp.Pool(10) as p:
            for k, _ in enumerate(p.imap_unordered(work, range(a, b), chunksize=4)):
                if k % 150 == 0: print(k, flush=True)
        print('frames ok')
