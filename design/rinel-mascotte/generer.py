#!/usr/bin/env python3
"""Génère les 6 états de Rinel (SVG animés, grille 32x32) et la planche d'aperçu HTML."""
import os

OUT = os.path.dirname(os.path.abspath(__file__))
FACE = "#141414"
PARCH = "#F1E6C8"
PARCH_END = "#CDBB8A"
BADGE = "#FFC93D"

STATES = {
    "attente":  dict(c="#FF5D52", label="En attente"),
    "en-cours": dict(c="#F5A524", label="En cours"),
    "pret":     dict(c="#3FD07F", label="Prêt"),
    "erreur":   dict(c="#8E96A3", label="Erreur"),
    "inactif":  dict(c="#4A4A4E", label="Au repos"),
    "coucou":   dict(c="#DAD6CF", label="Neutre · coucou"),
}

CSS = {
"attente": """
 .corps{transform-origin:16px 28px;animation:resp 1.6s ease-in-out infinite}
 @keyframes resp{0%,100%{transform:scale(1,1)}50%{transform:scale(1.04,1.08)}}
 .bras{transform-origin:23.5px 20px;animation:bras 1.6s ease-in-out infinite}
 @keyframes bras{0%,100%{transform:rotate(-10deg)}50%{transform:rotate(12deg)}}
 .plume{transform-origin:16px 10px;animation:plume 1.6s ease-in-out infinite}
 @keyframes plume{0%,100%{transform:rotate(-8deg) scale(1.2)}50%{transform:rotate(10deg) scale(1.3)}}
 .trem{transform-origin:16px 28px;animation:trem 6s linear infinite}
 @keyframes trem{0%,90%,100%{transform:rotate(0)}92%{transform:rotate(-4deg)}95%{transform:rotate(4deg)}97%{transform:rotate(-3deg)}}
""",
"en-cours": """
 .corps{transform-origin:16px 28px;animation:flot 3.2s ease-in-out infinite}
 @keyframes flot{0%,100%{transform:translateY(0) rotate(-2deg)}50%{transform:translateY(-2px) rotate(2deg)}}
 .bras{transform-origin:23.5px 20px;animation:bras 1.6s ease-in-out infinite}
 @keyframes bras{0%,100%{transform:rotate(4deg)}50%{transform:rotate(-6deg)}}
 .plume{transform-origin:16px 10px;animation:plume 3.2s ease-in-out infinite}
 @keyframes plume{0%,100%{transform:rotate(-6deg)}50%{transform:rotate(8deg)}}
""",
"pret": """
 .corps{transform-origin:16px 28px;animation:saut 3.6s ease-in-out infinite}
 @keyframes saut{0%,70%,100%{transform:translateY(0) scale(1,1)}
   74%{transform:translateY(0) scale(1.06,.9)}82%{transform:translateY(-6px) scale(.96,1.05)}
   90%{transform:translateY(0) scale(1.08,.9)}95%{transform:translateY(0) scale(1,1)}}
 .bras{transform-origin:23.5px 20px;transform:rotate(-14deg)}
 .plume{transform-origin:16px 10px;animation:plume 3.6s ease-in-out infinite}
 @keyframes plume{0%,70%,100%{transform:rotate(0)}82%{transform:rotate(14deg)}90%{transform:rotate(-8deg)}}
""",
"erreur": """
 .corps{transform-origin:16px 28px;transform:scale(1.04,.94);animation:aff .25s ease-out 1}
 @keyframes aff{from{transform:scale(1,1)}to{transform:scale(1.04,.94)}}
 .badge{transform-origin:26px 7.5px;animation:pop .2s cubic-bezier(.34,1.56,.64,1) 1}
 @keyframes pop{from{transform:scale(0)}to{transform:scale(1)}}
""",
"inactif": "",
"coucou": """
 .corps{transform-origin:16px 28px;animation:flot 3.2s ease-in-out infinite}
 @keyframes flot{0%,100%{transform:translateY(0) rotate(-2deg)}50%{transform:translateY(-1.5px) rotate(2deg)}}
 .bras{transform-origin:23.5px 20px;animation:coucou 3.2s ease-in-out infinite}
 @keyframes coucou{0%{transform:rotate(0)}8%{transform:rotate(-16deg)}18%{transform:rotate(12deg)}28%{transform:rotate(-16deg)}38%{transform:rotate(12deg)}46%,100%{transform:rotate(0)}}
 .plume{transform-origin:16px 10px;animation:plume 3.2s ease-in-out infinite}
 @keyframes plume{0%,100%{transform:rotate(-5deg)}50%{transform:rotate(7deg)}}
 .yeux-o{animation:cl 3.2s steps(1) infinite}
 @keyframes cl{0%,74%{opacity:1}74.01%,80%{opacity:0}80.01%,100%{opacity:1}}
 .yeux-f{opacity:0;animation:cl2 3.2s steps(1) infinite}
 @keyframes cl2{0%,74%{opacity:0}74.01%,80%{opacity:1}80.01%,100%{opacity:0}}
""",
}

def eyes(state):
    s = "stroke-linecap:round;fill:none;stroke:%s" % FACE
    if state == "attente":
        return (f'<circle cx="12.4" cy="20.4" r="1.9" fill="{FACE}"/><circle cx="19.6" cy="20.4" r="1.9" fill="{FACE}"/>'
                f'<ellipse cx="16" cy="25" rx="1.7" ry="2" fill="{FACE}"/>')
    if state == "en-cours":
        return (f'<path d="M10.6 20.6 H14.2" style="{s};stroke-width:2"/><path d="M17.8 20.6 H21.4" style="{s};stroke-width:2"/>'
                f'<path d="M14.6 24.6 H17.4" style="{s};stroke-width:1.6"/>')
    if state in ("pret",):
        return (f'<path d="M10.6 21 Q12.4 18.4 14.2 21" style="{s};stroke-width:2.1"/><path d="M17.8 21 Q19.6 18.4 21.4 21" style="{s};stroke-width:2.1"/>'
                f'<path d="M13.6 23.6 Q16 26.4 18.4 23.6" style="{s};stroke-width:1.7"/>')
    if state == "erreur":
        return (f'<circle cx="12.6" cy="21.2" r="1.1" fill="{FACE}"/><circle cx="19.4" cy="21.2" r="1.1" fill="{FACE}"/>'
                f'<path d="M14.4 25.2 Q16 24 17.6 25.2" style="{s};stroke-width:1.5"/>')
    if state == "inactif":
        return (f'<path d="M10.6 20.6 Q12.4 22.2 14.2 20.6" style="{s};stroke-width:1.8"/><path d="M17.8 20.6 Q19.6 22.2 21.4 20.6" style="{s};stroke-width:1.8"/>')
    # coucou : yeux rieurs, clignement
    return (f'<g class="yeux-o"><path d="M10.6 21 Q12.4 18.4 14.2 21" style="{s};stroke-width:2.1"/><path d="M17.8 21 Q19.6 18.4 21.4 21" style="{s};stroke-width:2.1"/></g>'
            f'<g class="yeux-f"><path d="M10.6 20.2 Q12.4 22 14.2 20.2" style="{s};stroke-width:1.8"/><path d="M17.8 20.2 Q19.6 22 21.4 20.2" style="{s};stroke-width:1.8"/></g>'
            f'<path d="M14 23.8 Q16 25.8 18 23.8" style="{s};stroke-width:1.6"/>')

def arm(state, c):
    # bras + parchemin (le cartographe). Angle de repos selon l'état.
    rest = {"attente": -28, "en-cours": 10, "pret": 0, "erreur": 60, "inactif": 55, "coucou": 0}[state]
    return (f'<g class="bras" transform="rotate({rest} 23.5 20)">'
            f'<path d="M23.5 20 L27.6 15.6" fill="none" stroke="{c}" stroke-width="2.8" stroke-linecap="round"/>'
            f'<g transform="rotate(-35 28.6 14.4)">'
            f'<rect x="25.4" y="12.9" width="6.4" height="3" rx="1.5" fill="{PARCH}"/>'
            f'<circle cx="25.9" cy="14.4" r="1.5" fill="{PARCH_END}"/><circle cx="31.3" cy="14.4" r="1.5" fill="{PARCH_END}"/>'
            f'</g></g>')

def plume(state, c):
    if state == "inactif":  # plume couchée
        return f'<path d="M11.6 10.8 C13 8 18 7.4 22.4 9.8 C19 9.6 16 10 13.6 11.2 Z" fill="{c}"/>'
    if state == "erreur":  # plume tombante
        return f'<path d="M12.6 10.8 C10 9 7.4 9.4 5.2 12.6 C8.4 11.8 10.6 12.2 13.8 11.4 Z" fill="{c}"/>'
    return (f'<g class="plume"><path d="M12.2 10.6 C11 5.4 14.6 1.4 21.2 1.6 C19 3.6 18.4 6.6 19.8 10.6 Z" fill="{c}"/>'
            f'<path d="M14 9.6 C14.2 6.6 16 4.4 18.6 3.2" fill="none" stroke="#000" stroke-opacity=".25" stroke-width=".8" stroke-linecap="round"/></g>')

def svg(state):
    c = STATES[state]["c"]
    css = CSS[state]
    badge = (f'<g class="badge"><circle cx="26" cy="7.5" r="4.2" fill="{BADGE}"/>'
             f'<path d="M26 5.2 V8.1" stroke="{FACE}" stroke-width="1.6" stroke-linecap="round"/><circle cx="26" cy="9.9" r=".9" fill="{FACE}"/></g>') if state == "erreur" else ""
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32" width="256" height="256" role="img" aria-label="Rinel, {STATES[state]["label"]}">
  <style>{css}</style>
  <g class="trem"><g class="corps">
    {plume(state, c)}
    {arm(state, c)}
    <rect x="10" y="26.4" width="4.2" height="3.6" rx="1.3" fill="{c}"/>
    <rect x="17.8" y="26.4" width="4.2" height="3.6" rx="1.3" fill="{c}"/>
    <circle cx="16" cy="19" r="9" fill="{c}"/>
    <path d="M7.2 17 A9 9 0 0 1 24.8 17 Z" fill="#fff" fill-opacity=".30"/>
    <rect x="6.6" y="16.2" width="18.8" height="1.9" rx=".95" fill="#000" fill-opacity=".42"/>
    {eyes(state)}
  </g></g>
  {badge}
</svg>
'''

def main():
    os.makedirs(os.path.join(OUT, "svg"), exist_ok=True)
    cards = ""
    for s, d in STATES.items():
        code = svg(s)
        open(os.path.join(OUT, "svg", f"rinel-{s}.svg"), "w", encoding="utf-8").write(code)
        cards += (f'<div class="card"><div class="big">{code}</div>'
                  f'<div class="row"><span class="i40">{code}</span><span class="i20">{code}</span><span class="i16">{code}</span></div>'
                  f'<b>{d["label"]}</b><code>{d["c"]}</code></div>')
    html = f'''<!doctype html><meta charset="utf-8"><title>Rinel · proposition</title>
<style>body{{background:#0b0b0c;color:#ddd;font:14px "Segoe UI",sans-serif;margin:32px}}
h1{{font-weight:600}}p{{color:#9a9a9a;max-width:70ch}}
.g{{display:flex;flex-wrap:wrap;gap:16px}}.card{{background:#161616;border-radius:14px;padding:16px;width:190px;text-align:center}}
.big svg{{width:140px;height:140px}}.row{{display:flex;gap:14px;justify-content:center;align-items:center;margin:8px 0;background:#000;padding:8px;border-radius:10px}}
code{{display:block;color:#888;margin-top:2px}}
.i40 svg{{width:40px;height:40px}}.i20 svg{{width:20px;height:20px}}.i16 svg{{width:16px;height:16px}}.i16,.i20,.i40{{display:inline-flex}}.notch{{margin-top:28px;background:#000;border-radius:0 0 11px 11px;height:22px;width:220px;display:flex;gap:6px;align-items:center;padding:0 10px}}</style>
<h1>Rinel — hérault cartographe du bord de l'encoche</h1>
<p>Hervé de Rinel, chevalier de la Table Ronde, cartographe qui conclut que la Bretagne est ronde. Un petit chevalier tout rond, casque à plume, parchemin sous le bras.</p>
<div class="g">{cards}</div>
<p>Encoche repliée (taille réelle 16 px) :</p>
<div class="notch">{"".join('<span class="i16">'+svg(s)+'</span>' for s in ["attente","en-cours","pret","erreur","inactif"])}</div>'''
    open(os.path.join(OUT, "apercu.html"), "w", encoding="utf-8").write(html)

main()
