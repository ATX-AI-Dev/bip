"""Rinel : video de presentation (60 s, 1920x1080, 30 i/s), images calculees avec Pillow.
Chaine complete (images, musique, ffmpeg) : docs/VIDEO-PRESENTATION.md.
Usage : python make_video.py frames [debut fin]   -> frames/00000.png ...
        python make_video.py audio                -> audio.wav (musique originale synthetisee)
"""
import math, os, sys, wave
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops

HERE = os.path.dirname(os.path.abspath(__file__))
SHOTS = os.path.normpath(os.path.join(HERE, '..', '..', 'screenshots'))
W, H, FPS, DUR = 1920, 1080, 30, 60.0

INK = (30, 34, 52)
ORANGE, GREEN, RED, GREY = (245, 165, 36), (63, 208, 127), (255, 93, 82), (142, 150, 163)

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
        f = {'b': 'segoeuib.ttf', 'r': 'segoeui.ttf', 'l': 'segoeuil.ttf', 'm': 'consola.ttf', 'mb': 'consolab.ttf'}[kind]
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

_bg = {}
def gradient(c1, c2):
    key = (c1, c2)
    if key not in _bg:
        x = np.linspace(0, 1, W)[None, :, None]
        y = np.linspace(0, 1, H)[:, None, None]
        t = np.clip(0.75 * x + 0.25 * y, 0, 1)
        arr = (np.array(c1)[None, None, :] * (1 - t) + np.array(c2)[None, None, :] * t).astype(np.uint8)
        _bg[key] = Image.fromarray(np.broadcast_to(arr, (H, W, 3)).copy(), 'RGB')
    return _bg[key].copy()

def blue_bg(): return gradient((138, 152, 186), (170, 182, 212))
def dark_bg(): return gradient((16, 18, 26), (34, 38, 52))

def overlay(img, layer, pos=(0, 0), alpha=1.0):
    if alpha < 1.0:
        a = layer.getchannel('A').point(lambda v: int(v * alpha))
        layer = layer.copy(); layer.putalpha(a)
    img.paste(layer, pos, layer)

def text(img, s, cx, cy, size, color, kind='b', alpha=1.0, anchor='mm', shadow=False):
    f = font(kind, size)
    bbox = ImageDraw.Draw(Image.new('L', (1, 1))).textbbox((0, 0), s, font=f, anchor=anchor)
    pad = 10
    w, h = bbox[2] - bbox[0] + 2 * pad, bbox[3] - bbox[1] + 2 * pad
    layer = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    ox, oy = -bbox[0] + pad, -bbox[1] + pad
    if shadow: d.text((ox + 2, oy + 3), s, font=f, fill=(0, 0, 0, 90), anchor=anchor)
    d.text((ox, oy), s, font=f, fill=tuple(color) + (255,), anchor=anchor)
    if anchor == 'mm': pos = (int(cx - w / 2), int(cy - h / 2))
    elif anchor == 'lm': pos = (int(cx - pad), int(cy - h / 2))
    else: pos = (int(cx - w + pad), int(cy - h / 2))
    overlay(img, layer, pos, alpha)

def rrect_layer(w, h, r, fill, ss=3, top_flat=False):
    big = Image.new('RGBA', (w * ss, h * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    if top_flat:
        d.rounded_rectangle((0, -r * ss * 2, w * ss - 1, h * ss - 1), radius=r * ss, fill=fill)
    else:
        d.rounded_rectangle((0, 0, w * ss - 1, h * ss - 1), radius=r * ss, fill=fill)
    return big.resize((w, h), Image.LANCZOS)

def shadow(img, x, y, w, h, r=24, blur=28, dy=18, a=0.35):
    pad = blur * 3
    lay = Image.new('RGBA', (w + 2 * pad, h + 2 * pad), (0, 0, 0, 0))
    ImageDraw.Draw(lay).rounded_rectangle((pad, pad, pad + w, pad + h), radius=r, fill=(0, 0, 0, int(255 * a)))
    lay = lay.filter(ImageFilter.GaussianBlur(blur))
    img.paste(lay, (x - pad, y - pad + dy), lay)

def card(img, name, cx, top, scale, alpha=1.0, radius=18, with_shadow=True, crop=None, dx=0, dy=0):
    src = asset(name)
    if crop: src = src.crop(crop)
    w, h = int(src.width * scale), int(src.height * scale)
    pic = src.resize((w, h), Image.LANCZOS).convert('RGBA')
    mask = rrect_layer(w, h, radius, (255, 255, 255, 255)).getchannel('A')
    pic.putalpha(mask)
    x, y = int(cx - w / 2) + dx, int(top) + dy
    if with_shadow and alpha > 0.05:
        shadow(img, x, y, w, h, radius, a=0.30 * alpha)
    overlay(img, pic, (x, y), alpha)
    return x, y, w, h

def sprite(img, name, cx, cy, size, rot=0.0, alpha=1.0):
    if alpha <= 0.01 or size < 2: return
    s = asset('sprite:' + name).resize((int(size), int(size)), Image.LANCZOS)
    if rot: s = s.rotate(rot, resample=Image.BICUBIC, expand=True)
    overlay(img, s, (int(cx - s.width / 2), int(cy - s.height / 2)), alpha)

def cursor(img, x, y, scale=1.0, alpha=1.0):
    pts = [(0, 0), (0, 26), (6, 20), (11, 31), (16, 29), (11, 18), (19, 18)]
    pts = [(x + px * scale * 1.2, y + py * scale * 1.2) for px, py in pts]
    lay = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    d.polygon([(px + 2, py + 3) for px, py in pts], fill=(0, 0, 0, 70))
    d.polygon(pts, fill=(255, 255, 255, 255), outline=(20, 20, 20, 255))
    overlay(img, lay, (0, 0), alpha)

def ripple(img, x, y, p, color=(255, 255, 255)):
    if p <= 0 or p >= 1: return
    r = int(18 + 70 * ease_out(p)); a = int(200 * (1 - p))
    lay = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(lay).ellipse((x - r, y - r, x + r, y + r), outline=color + (a,), width=5)
    overlay(img, lay)

def caption(img, title, sub=None, y=140, color=INK, alpha=1.0, size=64):
    text(img, title, W / 2, y, size, color, 'b', alpha)
    if sub: text(img, sub, W / 2, y + int(size * 0.95), int(size * 0.55), color, 'r', alpha * 0.85)

# ---------------------------------------------------------------- scenes
def s1(t):  # 0-5 : le probleme
    img = dark_bg()
    appear = ease_out(seg(t, 0.0, 0.7))
    # terminal
    tx, ty, tw, th = 230, 150, 1040, 700
    shadow(img, tx, ty, tw, th, 22, a=0.6)
    term = rrect_layer(tw, th, 22, (24, 26, 34, 255))
    d = ImageDraw.Draw(term)
    d.rectangle((0, 0, tw, 56), fill=(38, 41, 54, 255))
    for i, c in enumerate([(255, 95, 86), (255, 189, 46), (39, 201, 63)]):
        d.ellipse((24 + i * 30, 20, 40 + i * 30, 36), fill=c + (255,))
    d.text((tw / 2, 28), 'claude  ·  data-pipeline', font=font('r', 22), fill=(170, 176, 196, 255), anchor='mm')
    lines = ['> Applique les migrations sur la base de staging', '', '  Je lis migrations/042_orders.sql …', '  Je prépare la commande :', '',
             '    npm run migrate -- --env=staging', '', '  Exécuter cette commande ?   [ Autoriser ]  [ Refuser ]']
    n = int(t * 7)
    y0 = 100
    for i, ln in enumerate(lines[:n]):
        col = (214, 220, 236, 255) if i != 7 else (255, 150, 140, 255)
        d.text((36, y0 + i * 46), ln, font=font('m', 28), fill=col)
    if n < len(lines) and int(t * 2) % 2 == 0:
        d.rectangle((36 + 6 * 17, y0 + n * 46 + 4, 36 + 6 * 17 + 14, y0 + n * 46 + 34), fill=(214, 220, 236, 255))
    overlay(img, term, (tx, ty), appear)
    # fenetre qui masque la question
    wx, wy, ww, wh = 640, 420, 1000, 560
    shadow(img, wx, wy, ww, wh, 22, a=0.55)
    win = rrect_layer(ww, wh, 22, (238, 240, 246, 255))
    d = ImageDraw.Draw(win)
    d.rectangle((0, 0, ww, 54), fill=(222, 225, 234, 255))
    d.text((ww / 2, 27), 'Navigateur  ·  Documentation', font=font('r', 22), fill=(90, 96, 116, 255), anchor='mm')
    for i in range(7):
        d.rounded_rectangle((40, 90 + i * 64, 40 + (780 if i % 3 else 620) , 90 + i * 64 + 26), radius=12, fill=(206, 210, 222, 255))
    overlay(img, win, (wx, wy), ease_out(seg(t, 0.3, 1.1)))
    # compteur d'attente, visible sous la fenetre
    if t > 1.6:
        minutes = int(lerp(1, 11, seg(t, 1.6, 4.6)))
        text(img, f'⏸ en attente d\'une réponse · {minutes} min', tx + 40, ty + th - 46, 28, (255, 120, 110), 'mb', alpha=seg(t, 1.6, 2.2), anchor='lm')
    if t > 2.4:
        text(img, 'Il attend. Personne ne le sait.', W / 2, 78, 74, (245, 246, 250), 'b', ease_out(seg(t, 2.4, 3.2)), shadow=True)
    return img

def notch_shape(img, w, h, y=0, alpha=1.0):
    pad = 40
    lay = Image.new('RGBA', (w + 2 * pad, h + 2 * pad), (0, 0, 0, 0))
    ImageDraw.Draw(lay).rounded_rectangle((pad, pad - 60, pad + w, pad + h), radius=int(min(h, 60) * 0.45), fill=(0, 0, 0, int(80 * alpha)))
    lay = lay.filter(ImageFilter.GaussianBlur(12))
    img.paste(lay, (int(W / 2 - w / 2 - pad), int(y - pad + 10)), lay)
    shp = rrect_layer(w, h + 60, int(min(h, 60) * 0.45), (0, 0, 0, 255))
    ImageDraw.Draw(shp)
    overlay(img, shp.crop((0, 60, w, h + 60)), (int(W / 2 - w / 2), int(y)), alpha) if False else None
    big = rrect_layer(w, h + 40, int(min(h, 60) * 0.45), (0, 0, 0, 255), top_flat=True)
    overlay(img, big, (int(W / 2 - w / 2), int(y - 40)), alpha)

def s2(t):  # 5-12 : arrivee de Rinel
    img = blue_bg()
    drop = ease_out(seg(t, 0.0, 0.9))
    row = seg(t, 4.6, 5.6)
    nw = int(lerp(240, 320, ease_in_out(row)))
    nh = int(lerp(64, 78, ease_in_out(row)))
    ny = lerp(-90, 0, drop)
    notch_shape(img, nw, nh, ny)
    # grand Rinel qui fait coucou
    pop = ease_out(seg(t, 0.9, 1.6))
    shrink = ease_in_out(seg(t, 4.0, 4.9))
    size = lerp(380 * pop, 46, shrink)
    cx, cy = lerp(W / 2, W / 2 - 3 * 28, shrink), lerp(470, 36, shrink)
    wave = 9 * math.sin((t - 1.6) * 9) * (1 - shrink) if 1.6 < t < 4.2 else 0
    float_ = 8 * math.sin(t * 3) * (1 - shrink)
    if shrink < 1: sprite(img, 'coucou', cx, cy + float_, size, wave)
    # quatre petits chevaliers dans l'encoche
    for i, (name, rot) in enumerate([('en-cours', 0), ('en-cours', 0), ('en-cours', 0), ('pret', 0)]):
        a = seg(t, 4.8 + i * 0.18, 5.3 + i * 0.18)
        x = W / 2 - 3 * 28 + i * 56
        if a > 0: sprite(img, name, x, 36 + 2 * math.sin(t * 4 + i), 46 * ease_out(a), alpha=a)
    fade = 1 - seg(t, 3.7, 4.3)
    text(img, 'Rinel', W / 2, 770, 130, INK, 'b', ease_out(seg(t, 1.7, 2.4)) * fade)
    text(img, "L'encoche Claude Code pour Windows", W / 2, 880, 46, INK, 'r', ease_out(seg(t, 2.1, 2.8)) * fade * 0.9)
    if t > 5.4:
        caption(img, 'Un petit chevalier par session', 'collé en haut de ton écran', y=300, alpha=ease_out(seg(t, 5.4, 6.2)))
    return img

def s3(t):  # 12-22 : les etats
    img = blue_bg()
    caption(img, 'Un coup d\'œil suffit', 'qui travaille, qui a fini, qui t\'attend', y=130, alpha=ease_out(seg(t, 0.0, 0.6)))
    items = [('en-cours', ORANGE, 'En cours', 'il travaille'), ('pret', GREEN, 'Prêt', 'il a fini'),
             ('attente', RED, "Il t'attend", 'une permission ou une question'), ('erreur', GREY, 'Erreur', "le tour s'est arrêté")]
    xs = [370, 760, 1150, 1540]
    for i, (name, col, label, sub) in enumerate(items):
        a = ease_out(seg(t, 0.8 + i * 1.5, 1.5 + i * 1.5))
        if a <= 0: continue
        tt = t - (0.8 + i * 1.5)
        size = 300 * (0.7 + 0.3 * a)
        y = 560
        rot = 0.0
        if name == 'en-cours': y += 14 * math.sin(tt * 3.2); rot = 3 * math.sin(tt * 1.6)
        elif name == 'pret':
            hop = abs(math.sin(tt * 2.6)); y -= 40 * hop * (1 if tt > 0.6 else 0); rot = 0
        elif name == 'attente':
            amp = 1 + 0.6 * math.sin(tt * 5); rot = 4 * math.sin(tt * 11) * (1 if int(tt * 1.3) % 3 == 2 else 0.35)
            size *= 1 + 0.025 * math.sin(tt * 6) * amp
        else: y += 8; rot = -4
        if a < 1: y += (1 - a) * 40
        sprite(img, name, xs[i], y, size, rot, a)
        pill = rrect_layer(250, 56, 28, col + (255,))
        overlay(img, pill, (xs[i] - 125, 730), a)
        text(img, label, xs[i], 758, 34, (255, 255, 255) if name != 'pret' else (14, 60, 36), 'b', a)
        text(img, sub, xs[i], 820, 28, INK, 'r', a * 0.9)
    text(img, 'Même sans les couleurs : plume, parchemin et pastille racontent l\'état', W / 2, 960, 34, INK, 'r', ease_out(seg(t, 7.2, 8.0)) * 0.9)
    return img

CROPS = {'repliee': ((330, 0, 470, 70), 3.2), 'entrouverte': ((220, 0, 580, 110), 2.6), 'permission': ((150, 0, 650, 402), 1.9)}
def s4(t):  # 22-34 : repondre dans l'encoche
    img = blue_bg()
    caption(img, "Réponds dans l'encoche", 'sans changer de fenêtre', y=100, alpha=ease_out(seg(t, 0.0, 0.7)))
    top = 240
    stages = [('repliee', 0.4, 2.4), ('entrouverte', 2.2, 4.4), ('permission', 4.2, 9.0), ('repliee', 8.9, 12.5)]
    for name, a0, a1 in stages:
        fin = seg(t, a0, a0 + 0.35)
        fout = 1 - seg(t, a1 - 0.35, a1) if a1 < 12.4 else 1.0
        al = fin * fout
        if al <= 0.01: continue
        crop, sc = CROPS[name]
        slide = (1 - ease_out(fin)) * -30
        card(img, name, W / 2, top + slide, sc, alpha=al, radius=16, crop=crop)
    if 2.2 < t < 4.4:
        text(img, 'Il entrouvre pour dire qui attend', W / 2, 640, 40, INK, 'r', seg(t, 2.6, 3.2) * (1 - seg(t, 4.0, 4.4)))
    crop, sc = CROPS['permission']
    tx, ty = W / 2 - 250 * sc + (246 - 150) * sc, top + 202 * sc
    if 6.0 < t < 9.2:
        cx = lerp(1350, tx, ease_in_out(seg(t, 6.0, 7.6)))
        cy = lerp(820, ty, ease_in_out(seg(t, 6.0, 7.6)))
        cursor(img, cx, cy, alpha=seg(t, 6.0, 6.4) * (1 - seg(t, 8.9, 9.2)))
        ripple(img, tx, ty, (t - 8.2) / 0.7)
    if 4.4 < t < 8.6:
        text(img, 'Autoriser · Refuser · Répondre dans Claude', W / 2, 1030, 38, INK, 'r', seg(t, 4.8, 5.4) * (1 - seg(t, 8.2, 8.7)))
    if t > 9.3:
        text(img, '✓ Autorisé : la session repart', W / 2, 640, 48, (20, 120, 70), 'b', ease_out(seg(t, 9.4, 10.0)))
    return img

def desktop_window(img, x, y, w, h, t_sel, sel_idx):
    """Fenetre generique « Claude Desktop » (dessin neutre, sans logo)."""
    shadow(img, x, y, w, h, 20, a=0.35)
    win = rrect_layer(w, h, 20, (27, 28, 34, 255))
    d = ImageDraw.Draw(win)
    d.rectangle((0, 0, w, 52), fill=(40, 42, 52, 255))
    for i, c in enumerate([(255, 95, 86), (255, 189, 46), (39, 201, 63)]):
        d.ellipse((22 + i * 28, 18, 36 + i * 28, 32), fill=c + (255,))
    d.text((w / 2, 26), 'Claude Desktop', font=font('r', 21), fill=(180, 186, 204, 255), anchor='mm')
    d.rectangle((0, 52, 230, h), fill=(33, 35, 43, 255))
    for i, nm in enumerate(['Migrations staging', 'API commandes', 'Refonte accueil']):
        d.rounded_rectangle((16, 76 + i * 56, 214, 120 + i * 56), radius=10, fill=(52, 55, 68, 255) if i == 0 else (33, 35, 43, 255))
        d.text((30, 98 + i * 56), nm, font=font('r', 19), fill=(210, 214, 228, 255), anchor='lm')
    d.text((262, 100), "Quelle base de données pour les tests d'intégration ?", font=font('b', 23), fill=(240, 242, 250, 255), anchor='lm')
    opts = ['PostgreSQL en conteneur', 'SQLite en mémoire', 'Base de staging partagée', 'Autre…']
    for i, o in enumerate(opts):
        yy = 150 + i * 84
        picked = sel_idx == i and t_sel > 0
        d.rounded_rectangle((262, yy, w - 28, yy + 68), radius=14, fill=(58, 92, 70, 255) if picked else (40, 42, 52, 255),
                            outline=(110, 220, 150, 255) if picked else (66, 70, 86, 255), width=2)
        d.text((286, yy + 34), o, font=font('r', 22), fill=(240, 242, 250, 255), anchor='lm')
    overlay(img, win, (x, y))

def s5(t):  # 34-44 : les deux interfaces
    img = blue_bg()
    caption(img, "Dans l'encoche et dans Claude Desktop", 'la première réponse gagne', y=100, alpha=ease_out(seg(t, 0.0, 0.7)), size=62)
    lx, ly, lw, lh = 90, 250, 900, 620
    rscale = 1.15
    QC = (150, 0, 650, 560)
    rw, rh = int(500 * rscale), int(560 * rscale)
    rx = 1470 - rw // 2
    beats = [(0.9, 4.6, 'left'), (5.2, 9.2, 'right')]
    appear = ease_out(seg(t, 0.4, 1.1))
    sel_left = None; sel_right = None; done = None
    if t < 5.0:
        cur = 'left'; t0 = t
    else:
        cur = 'right'; t0 = t - 5.0
    # etat gauche
    left_alpha = appear; right_alpha = appear
    if cur == 'left':
        sel = 0 if t0 > 3.0 else None
        if t0 > 3.3: right_alpha = appear * (1 - seg(t0, 3.3, 3.8))
        desktop_window(img, lx, ly + int((1 - appear) * 40), lw, lh, 1 if sel is not None else 0, sel)
        if right_alpha > 0.01:
            card(img, 'question', rx + rw / 2, ly + (lh - rh) / 2 + (1 - appear) * 40, rscale, alpha=right_alpha, radius=16, crop=QC)
        # curseur
        px, py = lx + 262 + 250, ly + 150 + 34
        if 1.2 < t0 < 3.6:
            cx = lerp(900, px, ease_in_out(seg(t0, 1.2, 2.8))); cy = lerp(900, py, ease_in_out(seg(t0, 1.2, 2.8)))
            cursor(img, cx, cy, alpha=seg(t0, 1.2, 1.6) * (1 - seg(t0, 3.4, 3.6)))
            ripple(img, px, py, (t0 - 3.0) / 0.6)
        if t0 > 3.8:
            text(img, "✓ Répondu dans Claude Desktop : la carte de l'encoche disparaît", W / 2, 960, 40, (20, 120, 70), 'b', ease_out(seg(t0, 3.9, 4.5)) * (1 - seg(t, 4.7, 5.0)))
    else:
        # deuxieme temps : on repond dans l'encoche
        sel = 1 if t0 > 3.0 else None
        fade_in = ease_out(seg(t0, 0.0, 0.6))
        left_alpha = fade_in
        if t0 > 3.3: left_alpha = fade_in * (1 - seg(t0, 3.3, 3.8))
        if left_alpha > 0.02:
            tmp = img.copy()
            desktop_window(tmp, lx, ly, lw, lh, 0, None)
            img = Image.blend(img, tmp, left_alpha)
        card(img, 'question', rx + rw / 2, ly + (lh - rh) / 2, rscale, alpha=fade_in, radius=16, crop=QC)
        px, py = rx + (400 - 150) * rscale, ly + (lh - rh) / 2 + 225 * rscale
        if 0.8 < t0 < 3.6:
            cx = lerp(1000, px, ease_in_out(seg(t0, 0.8, 2.4))); cy = lerp(900, py, ease_in_out(seg(t0, 0.8, 2.4)))
            cursor(img, cx, cy, alpha=seg(t0, 0.8, 1.2) * (1 - seg(t0, 3.4, 3.6)))
            ripple(img, px, py, (t0 - 2.5) / 0.6)
        if t0 > 3.8:
            text(img, "✓ Répondu dans l'encoche : Claude Desktop se met à jour", W / 2, 960, 40, (20, 120, 70), 'b', ease_out(seg(t0, 3.9, 4.5)))
    return img

def s6(t):  # 44-52 : confiance
    img = blue_bg()
    caption(img, "Tout ce que font les agents est journalisé", "chaîne d'empreintes · règles que tu crées · rien ne quitte ton poste", y=100, alpha=ease_out(seg(t, 0.0, 0.7)), size=58)
    src = asset('rapport-audit')
    pan = ease_in_out(seg(t, 0.6, 6.5))
    scale = 0.9
    vh = 640
    y0 = int(lerp(0, 260, pan))
    crop = (0, y0, 1280, y0 + int(vh / scale))
    shift = ease_in_out(seg(t, 4.2, 5.4))
    rcx = lerp(W / 2, 620, shift)
    dim = 1 - 0.0
    card(img, 'rapport-audit', rcx, 250, scale, alpha=ease_out(seg(t, 0.3, 1.0)), radius=16, crop=crop)
    if t > 4.0:
        p = ease_out(seg(t, 4.0, 5.0))
        card(img, 'regle', 1520 + (1 - p) * 500, 250, 1.0, alpha=p, radius=16, crop=(150, 0, 650, 509))
        text(img, 'Après 5 autorisations : « en faire une règle ? »', 1520, 830, 30, INK, 'b', ease_out(seg(t, 5.2, 5.9)))
    return img

def s7(t):  # 52-60 : final
    img = blue_bg()
    a = ease_out(seg(t, 0.0, 0.8))
    wave = 9 * math.sin(t * 8) if 0.8 < t < 3.6 else 0
    sprite(img, 'coucou', W / 2, 400 + 8 * math.sin(t * 3), 380 * (0.8 + 0.2 * a), wave, a)
    text(img, 'Rinel', W / 2, 700, 130, INK, 'b', ease_out(seg(t, 0.5, 1.3)))
    text(img, "L'encoche qui veille sur tes sessions", W / 2, 810, 46, INK, 'r', ease_out(seg(t, 0.9, 1.7)) * 0.9)
    text(img, 'github.com/ATX-AI-Dev/bip', W / 2, 920, 44, (20, 80, 160), 'b', ease_out(seg(t, 1.6, 2.4)))
    text(img, "Fork de CR0ZER/bip · licence MIT · Windows 10 et 11", W / 2, 990, 30, INK, 'r', ease_out(seg(t, 2.0, 2.8)) * 0.8)
    return img

SCENES = [(0, 5, s1), (5, 12, s2), (12, 22, s3), (22, 34, s4), (34, 44, s5), (44, 52, s6), (52, 60.001, s7)]

def render(t):
    for a, b, fn in SCENES:
        if a <= t < b:
            img = fn(t - a)
            # fondu enchaine avec la scene precedente (0.5 s)
            if t - a < 0.5 and a > 0:
                prev = [s for s in SCENES if s[1] == a][0]
                p = prev[2](a - 0.001 - prev[0])
                img = Image.blend(p, img, ease_in_out((t - a) / 0.5))
            break
    else:
        img = blue_bg()
    fi, fo = seg(t, 0, 0.4), 1 - seg(t, DUR - 0.6, DUR)
    k = min(fi, fo)
    if k < 1.0:
        img = Image.blend(Image.new('RGB', (W, H), (14, 16, 24)), img, k)
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
        n = int(0.14 * SR); tt = np.arange(n) / SR
        y = np.sin(2 * np.pi * (f0 + 300 * np.exp(-tt * 40)) * tt) * np.exp(-tt * 28)
        mix_in(buf, y, at, 0.35)
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
