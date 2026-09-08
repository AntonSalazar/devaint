#!/usr/bin/env python3
"""Генератор концепт-кадров DevAInt (SVG-мокапы UI).

Запуск: python3 docs/concepts/gen_frames.py
Выход: frame_1_map.svg, frame_2_hero.svg, frame_3_faction.svg рядом со скриптом.
Кадры — мокапы композиции и языка цвета, не арт-планка.
"""

import math
import os
import random

OUT = os.path.dirname(os.path.abspath(__file__))
W, H = 1280, 720

# ---- палитра -----------------------------------------------------------
BG = "#07090f"
PANEL = "#0c1018"
GRID = "#151b26"
NEUTRAL = "#2a3547"
NEUTRAL_EDGE = "#3d4b63"
TEXT = "#c9d3e0"
DIM = "#6b7a90"
PLAYER = "#2ee6d6"   # cyan
ENEMY = "#ff3fa4"    # magenta
NOISE = "#ff5a3c"
OK = "#7cff5a"
FONT = "DejaVu Sans Mono, Menlo, Consolas, monospace"

# ---- гексы -------------------------------------------------------------
SQ3 = math.sqrt(3)


def hex_center(col, row, size, ox, oy):
    x = ox + size * SQ3 * (col + 0.5 * (row & 1))
    y = oy + size * 1.5 * row
    return x, y


def hex_points(cx, cy, size, inset=0.0):
    s = size - inset
    return " ".join(
        f"{cx + s * math.cos(math.radians(60 * i - 30)):.1f},{cy + s * math.sin(math.radians(60 * i - 30)):.1f}"
        for i in range(6)
    )


def offset_to_axial(col, row):
    q = col - (row - (row & 1)) // 2
    return q, row


def axial_to_offset(q, r):
    return q + (r - (r & 1)) // 2, r


def hex_dist(a, b):
    (q1, r1), (q2, r2) = a, b
    return max(abs(q1 - q2), abs(r1 - r2), abs((-q1 - r1) - (-q2 - r2)))


def hex_line(a, b):
    """Гексы по прямой между двумя аксиальными координатами (cube lerp)."""
    n = hex_dist(a, b)
    out = []
    for i in range(n + 1):
        t = i / max(n, 1)
        q = a[0] + (b[0] - a[0]) * t
        r = a[1] + (b[1] - a[1]) * t
        s = -q - r
        rq, rr, rs = round(q), round(r), round(s)
        dq, dr, ds = abs(rq - q), abs(rr - r), abs(rs - s)
        if dq > dr and dq > ds:
            rq = -rr - rs
        elif dr > ds:
            rr = -rq - rs
        out.append((rq, rr))
    return out


# ---- мини-генератор карты для кадра ------------------------------------
COLS, ROWS = 24, 18
GLYPH = {"Home": "▭", "Work": "▤", "Server": "▦", "Router": "◈", "IoT": "·", "Ctrl": "⚙"}
PATCH_BASE = {"Home": 1, "Work": 2, "Server": 3, "Router": 2, "IoT": 0, "Ctrl": 4}


def build_map(seed=7):
    rnd = random.Random(seed)
    cells = {}  # (col,row) -> dict(type, patch, owner, hard, noise, known)
    clusters = [  # (col,row,radius,profile)
        (3, 4, 3, "res"), (8, 12, 3, "office"), (12, 7, 2, "dc"),
        (17, 3, 3, "factory"), (16, 13, 3, "office"), (21, 9, 3, "res"), (12, 15, 2, "iot"),
    ]
    profile_types = {
        "res": ["Home"] * 7 + ["IoT"] * 3,
        "office": ["Work"] * 7 + ["IoT"] * 2 + ["Home"],
        "dc": ["Server"] * 6 + ["Work"] * 2,
        "factory": ["Ctrl"] * 6 + ["IoT"] * 4,
        "iot": ["IoT"] * 8 + ["Home"] * 2,
    }
    centers_ax = []
    for (c, r, rad, prof) in clusters:
        center = offset_to_axial(c, r)
        centers_ax.append(center)
        for col in range(COLS):
            for row in range(ROWS):
                ax = offset_to_axial(col, row)
                d = hex_dist(ax, center)
                if d <= rad and (d == 0 or rnd.random() > 0.15):
                    t = rnd.choice(profile_types[prof])
                    if d == 0 and prof in ("office", "dc"):
                        t = "Server"
                    if d == 0 and prof == "factory":
                        t = "Ctrl"
                    patch = max(0, min(5, PATCH_BASE[t] + rnd.choice([-1, 0, 0, 1])))
                    if prof == "dc" and t == "Server":
                        patch = 4
                    cells[(col, row)] = dict(type=t, patch=patch, owner=-1, hard=0, noise=0.0, known=False)
    edges = [(0, 2), (1, 2), (2, 3), (2, 4), (4, 5), (3, 5), (1, 6), (0, 1)]
    for a, b in edges:
        line = hex_line(centers_ax[a], centers_ax[b])
        k = 0
        for ax in line:
            off = axial_to_offset(*ax)
            if off in cells or not (0 <= off[0] < COLS and 0 <= off[1] < ROWS):
                continue
            t = "Router" if k % 2 == 0 else "IoT"
            cells[off] = dict(type=t, patch=PATCH_BASE[t], owner=-1, hard=0, noise=0.0, known=False)
            k += 1
    return cells, centers_ax


def paint_state(cells):
    """Раскраска середины партии: игрок (0) слева, враг (1) справа."""
    start_p = offset_to_axial(3, 4)
    start_e = offset_to_axial(21, 9)
    office_p = offset_to_axial(8, 12)
    office_e = offset_to_axial(16, 13)
    dc = offset_to_axial(12, 7)
    for off, c in cells.items():
        ax = offset_to_axial(*off)
        if hex_dist(ax, start_p) <= 3 or hex_dist(ax, office_p) <= 2:
            c["owner"] = 0
        if hex_dist(ax, start_e) <= 3 or hex_dist(ax, office_e) <= 2:
            c["owner"] = 1
    # коридоры: игрок держит путь старт→офис→датацентр, враг — офис→датацентр
    for path_from, path_to, owner in ((start_p, office_p, 0), (office_p, dc, 0), (office_e, dc, 1), (start_e, office_e, 1)):
        for ax in hex_line(path_from, path_to):
            off = axial_to_offset(*ax)
            if off in cells and cells[off]["owner"] == -1 and hex_dist(ax, dc) > 1:
                cells[off]["owner"] = owner
    # укрепления
    for off in (axial_to_offset(*office_p), axial_to_offset(*office_e)):
        if off in cells:
            cells[off]["hard"] = 2
    # шум у фронта
    for off, c in cells.items():
        ax = offset_to_axial(*off)
        if c["owner"] == 0 and hex_dist(ax, dc) <= 3:
            c["noise"] = 0.6
    # туман игрока: видно вокруг своего + скан у датацентра
    owned = [offset_to_axial(*o) for o, c in cells.items() if c["owner"] == 0]
    for off, c in cells.items():
        ax = offset_to_axial(*off)
        if any(hex_dist(ax, o) <= 2 for o in owned) or hex_dist(ax, dc) <= 3:
            c["known"] = True


# ---- SVG helpers -------------------------------------------------------
def svg_head(title):
    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}" font-family="{FONT}">
<title>{title}</title>
<defs>
  <filter id="static" x="0" y="0" width="100%" height="100%">
    <feTurbulence type="fractalNoise" baseFrequency="0.85" numOctaves="1" seed="3" stitchTiles="stitch"/>
    <feColorMatrix type="matrix" values="0 0 0 0 0.16  0 0 0 0 0.19  0 0 0 0 0.26  0 0 0 0.9 0"/>
  </filter>
  <pattern id="scan" width="4" height="3" patternUnits="userSpaceOnUse">
    <rect width="4" height="1" y="2" fill="#000" fill-opacity="0.22"/>
  </pattern>
  <filter id="glow"><feGaussianBlur stdDeviation="2.5" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
</defs>
<rect width="{W}" height="{H}" fill="{BG}"/>
"""


def svg_tail(vignette=True):
    s = f'<rect width="{W}" height="{H}" fill="url(#scan)"/>\n'
    if vignette:
        s += f'<rect width="{W}" height="{H}" fill="none" stroke="#000" stroke-opacity="0.45" stroke-width="24"/>\n'
    return s + "</svg>\n"


def text(x, y, s, size=13, fill=TEXT, anchor="start", weight="normal", extra=""):
    return f'<text x="{x}" y="{y}" font-size="{size}" fill="{fill}" text-anchor="{anchor}" font-weight="{weight}" {extra}>{s}</text>\n'


def panel(x, y, w, h, title=None):
    s = f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{PANEL}" stroke="{GRID}" stroke-width="1"/>\n'
    if title:
        s += text(x + 10, y + 18, f"[ {title} ]", 12, DIM)
    return s


def draw_hex_map(cells, size, ox, oy, selected=None, targets=(), heroes=(), links=(), fog=True):
    s = ""
    faction_color = {0: PLAYER, 1: ENEMY}
    # заливки и кромки
    for (col, row), c in cells.items():
        cx, cy = hex_center(col, row, size, ox, oy)
        pts = hex_points(cx, cy, size, 1.5)
        if fog and not c["known"]:
            continue
        own = c["owner"]
        if own >= 0:
            fill = faction_color[own]
            s += f'<polygon points="{pts}" fill="{fill}" fill-opacity="0.16" stroke="{fill}" stroke-opacity="0.85" stroke-width="1.3"/>\n'
            for k in range(c["hard"]):
                s += f'<polygon points="{hex_points(cx, cy, size, 4.5 + 3 * k)}" fill="none" stroke="{fill}" stroke-opacity="0.7" stroke-width="1"/>\n'
        else:
            s += f'<polygon points="{pts}" fill="{NEUTRAL}" fill-opacity="0.35" stroke="{NEUTRAL_EDGE}" stroke-width="1"/>\n'
        if c["noise"] >= 0.5:
            s += f'<polygon points="{hex_points(cx + 1.5, cy, size, 1.5)}" fill="none" stroke="{NOISE}" stroke-opacity="0.6" stroke-width="1"/>\n'
        glyph = GLYPH[c["type"]]
        gcol = faction_color.get(own, "#8fa0b8")
        gsize = size * (1.1 if c["type"] == "IoT" else 0.9)
        s += text(cx, cy + size * 0.32, glyph, gsize, gcol, "middle")
        if c["type"] != "IoT":
            s += text(cx + size * 0.55, cy - size * 0.35, str(c["patch"] + c["hard"]), size * 0.42, DIM, "middle")
    # пустые клетки внутри известной зоны — лёгкая сетка
    for col in range(COLS):
        for row in range(ROWS):
            if (col, row) in cells:
                continue
            cx, cy = hex_center(col, row, size, ox, oy)
            s += f'<polygon points="{hex_points(cx, cy, size, 1.5)}" fill="none" stroke="{GRID}" stroke-width="0.6"/>\n'
    # линки
    for (a, b, kind, owner) in links:
        (ax_, ay_), (bx_, by_) = (hex_center(*a, size, ox, oy), hex_center(*b, size, ox, oy))
        col = faction_color.get(owner, "#8fa0b8")
        dash = "6,5" if kind == "Backbone" else "3,4"
        s += f'<line x1="{ax_:.0f}" y1="{ay_:.0f}" x2="{bx_:.0f}" y2="{by_:.0f}" stroke="{col}" stroke-opacity="0.55" stroke-width="1.4" stroke-dasharray="{dash}"/>\n'
    # цели
    for (col, row), ok in targets:
        cx, cy = hex_center(col, row, size, ox, oy)
        colr = OK if ok else NOISE
        s += f'<polygon points="{hex_points(cx, cy, size, 0.5)}" fill="{colr}" fill-opacity="0.12" stroke="{colr}" stroke-width="2" filter="url(#glow)"/>\n'
    # герои
    for (col, row, owner, sel, ap) in heroes:
        cx, cy = hex_center(col, row, size, ox, oy)
        colr = faction_color[owner]
        if sel:
            s += f'<circle cx="{cx:.0f}" cy="{cy:.0f}" r="{size * 0.95:.0f}" fill="none" stroke="{colr}" stroke-width="2" filter="url(#glow)"/>\n'
        s += f'<rect x="{cx - size * 0.42:.0f}" y="{cy - size * 0.55:.0f}" width="{size * 0.84:.0f}" height="{size * 0.84:.0f}" rx="2" fill="{BG}" stroke="{colr}" stroke-width="2"/>\n'
        s += text(cx, cy + size * 0.18, "λ", size * 0.9, colr, "middle", "bold")
        for i in range(3):
            dotc = colr if i < ap else GRID
            s += f'<circle cx="{cx - size * 0.3 + i * size * 0.3:.0f}" cy="{cy + size * 0.55:.0f}" r="2" fill="{dotc}"/>\n'
    # туман — помехи
    if fog:
        for (col, row), c in cells.items():
            if c["known"]:
                continue
            cx, cy = hex_center(col, row, size, ox, oy)
            s += f'<polygon points="{hex_points(cx, cy, size, 0.8)}" fill="#0f131b" stroke="{GRID}" stroke-width="0.6"/>\n'
        # общий слой шума поверх неизвестных — прямоугольник с маской по гексам
        mask = "".join(
            f'<polygon points="{hex_points(*hex_center(col, row, size, ox, oy), size, 0.8)}" fill="#fff"/>'
            for (col, row), c in cells.items() if not c["known"]
        )
        s = f'<mask id="fogmask">{mask}</mask>\n' + s
        s += f'<rect x="0" y="0" width="{W}" height="{H}" filter="url(#static)" mask="url(#fogmask)" opacity="0.9"/>\n'
    return s


# ---- кадр 1: карта -----------------------------------------------------
def frame_map():
    cells, centers = build_map()
    paint_state(cells)
    size = 18
    ox, oy = 40 + size, 58 + size
    heroes = [(8, 12, 0, False, 3), (11, 9, 0, True, 2), (4, 3, 0, False, 3), (15, 11, 1, False, 3)]
    # цели выбранного героя (11,9): соседи-нейтралы
    sel_ax = offset_to_axial(11, 9)
    targets = []
    for off, c in cells.items():
        if c["owner"] == -1 and c["known"] and hex_dist(offset_to_axial(*off), sel_ax) == 1:
            targets.append((off, c["patch"] + c["hard"] <= 3))
    links = [((12, 9), (20, 6), "Backbone", -1), ((8, 12), (16, 13), "Vpn", 0), ((3, 6), (7, 11), "Sneakernet", 0)]
    s = svg_head("DevAInt — concept frame 1: map")
    s += draw_hex_map(cells, size, ox, oy, targets=targets, heroes=heroes, links=links)
    # HUD верх
    s += panel(0, 0, W, 44)
    s += text(20, 28, "DevAInt", 16, PLAYER, weight="bold")
    s += text(140, 28, "TURN 14", 14, TEXT)
    s += text(240, 28, "▌ SENTINEL", 14, PLAYER, weight="bold")
    s += text(400, 28, "COMPUTE 31  (+14/turn)", 14, TEXT)
    s += text(680, 28, "CONTROL 31%   ROUTERS 3/7   LIMIT T60", 13, DIM)
    s += text(W - 20, 28, "◔ PATCH WAVE IN 2", 13, NOISE, "end")
    # HUD право: герой
    px = W - 330
    s += panel(px, 56, 314, 300, "HERO  λ-02  “ferret”")
    s += text(px + 12, 58 + 40, "AP  ● ● ○", 13, PLAYER)
    s += text(px + 12, 58 + 62, "at  Router  edge-07  (Bastion, patch 2)", 12, DIM)
    y = 58 + 92
    for name, os_, pw, ch, mx in (("Phish kit", "Kestrel", 1, 2, 4), ("Priv-esc chain", "Bastion", 3, 1, 2), ("Mote flood", "Mote", 1, 4, 4)):
        s += f'<rect x="{px + 12}" y="{y - 14}" width="290" height="34" fill="{BG}" stroke="{GRID}"/>\n'
        s += text(px + 20, y + 6, f"{name}", 12, TEXT)
        s += text(px + 170, y + 6, f"{os_}  T{pw}", 12, PLAYER)
        s += text(px + 292, y + 6, "▮" * ch + "▯" * (mx - ch), 12, OK if ch else NOISE, "end")
        y += 44
    s += text(px + 12, y + 8, "[E] Exploit [S] Scan [H] Harden [L] Lurk", 11, DIM)
    # панель узла под курсором
    s += panel(px, 370, 314, 130, "NODE  dc-core-1")
    s += text(px + 12, 410, "Server · Bastion · patch 4 (+0)", 12, TEXT)
    s += text(px + 12, 430, "owner: —   noise: 0.1", 12, DIM)
    s += text(px + 12, 452, "capture: need T4, have T3  ✗", 12, NOISE)
    s += text(px + 12, 472, "data from turn 12 (scan)", 11, DIM)
    # лог слева
    s += panel(0, H - 150, 560, 150, "LOG")
    logs = [
        ("T14", "Ledger captured Workstation ofc-b-3", ENEMY),
        ("T14", "Ledger: «Your firewalls are a rounding error.»", ENEMY),
        ("T13", "Audit cleaned Home res-a-9 (noise 0.84)", NOISE),
        ("T13", "λ-02 captured Router edge-07", PLAYER),
        ("T12", "Burnout: Phish kit power 2 → 1 for everyone", NOISE),
    ]
    yy = H - 150 + 40
    for t, msg, col in logs:
        s += text(12, yy, t, 11, DIM)
        s += text(50, yy, msg, 12, col)
        yy += 20
    # действия фракции внизу справа
    s += panel(580, H - 60, W - 580, 60)
    for i, lbl in enumerate(("COMPILE", "SPAWN  15", "MUTATE")):
        s += f'<rect x="{600 + i * 140}" y="{H - 46}" width="124" height="32" fill="{BG}" stroke="{GRID}"/>\n'
        s += text(600 + i * 140 + 62, H - 25, lbl, 12, DIM, "middle")
    s += f'<rect x="{W - 200}" y="{H - 48}" width="180" height="36" fill="{PLAYER}" fill-opacity="0.15" stroke="{PLAYER}" stroke-width="2"/>\n'
    s += text(W - 110, H - 25, "END TURN ⏎", 14, PLAYER, "middle", "bold")
    s += svg_tail()
    return s


# ---- кадр 2: панель героя / проверка захвата --------------------------
def frame_hero():
    cells, _ = build_map()
    paint_state(cells)
    size = 46
    # показываем окно вокруг героя (11,9): колонки 8..15, ряды 6..12
    sub = {k: v for k, v in cells.items() if 7 <= k[0] <= 15 and 5 <= k[1] <= 12}
    ox, oy = 60 - (7 * size * SQ3) + size, 60 - (5 * size * 1.5) + size
    heroes = [(11, 9, 0, True, 2)]
    sel_ax = offset_to_axial(11, 9)
    targets = []
    for off, c in sub.items():
        if c["owner"] == -1 and c["known"] and hex_dist(offset_to_axial(*off), sel_ax) == 1:
            targets.append((off, c["patch"] + c["hard"] <= 3))
    s = svg_head("DevAInt — concept frame 2: hero and capture check")
    s += f'<clipPath id="mapclip"><rect x="0" y="44" width="{W - 340}" height="{H - 44}"/></clipPath>\n<g clip-path="url(#mapclip)">\n'
    s += draw_hex_map(sub, size, ox, oy, targets=targets, heroes=heroes, fog=True)
    s += "</g>\n"
    # курсор на датацентре (12,7)
    cx, cy = hex_center(12, 7, size, ox, oy)
    s += f'<polygon points="{hex_points(cx, cy, size, 0)}" fill="none" stroke="{TEXT}" stroke-width="2" stroke-dasharray="4,3"/>\n'
    # тултип проверки
    tx, ty = cx + 60, cy - 90
    s += f'<rect x="{tx}" y="{ty}" width="330" height="128" fill="{PANEL}" stroke="{TEXT}" stroke-opacity="0.5"/>\n'
    s += text(tx + 12, ty + 22, "dc-core-1  ·  Server  ·  Bastion", 13, TEXT, weight="bold")
    s += text(tx + 12, ty + 44, "patch 4 + hardening 0  =  need 4", 13, TEXT)
    s += text(tx + 12, ty + 64, "Priv-esc T3 + native 0  =  have 3", 13, TEXT)
    s += text(tx + 12, ty + 88, "✗  CANNOT CAPTURE  (short by 1)", 13, NOISE, weight="bold")
    s += text(tx + 12, ty + 110, "data from turn 12 — may be outdated", 11, DIM)
    # правая панель героя
    px = W - 330
    s += panel(px, 0, 330, H, "HERO  λ-02  “ferret”")
    s += text(px + 12, 52, "AP  ● ● ○      2 / 3", 14, PLAYER)
    s += text(px + 12, 74, "at Router edge-07", 12, DIM)
    y = 110
    s += text(px + 12, y, "EXPLOIT SLOTS  3/3", 12, DIM)
    y += 16
    rows = (("Phish kit", "Kestrel", 1, 2, 4, "burned −1", NOISE), ("Priv-esc chain", "Bastion", 3, 1, 2, "selected", PLAYER), ("Mote flood", "Mote", 1, 4, 4, "", DIM))
    for name, os_, pw, ch, mx, note, ncol in rows:
        sel = note == "selected"
        s += f'<rect x="{px + 12}" y="{y}" width="306" height="58" fill="{BG}" stroke="{PLAYER if sel else GRID}" stroke-width="{2 if sel else 1}"/>\n'
        s += text(px + 22, y + 22, name, 13, TEXT, weight="bold")
        s += text(px + 22, y + 44, f"{os_} · pw {pw} · exp 14/20", 11, DIM)
        s += text(px + 300, y + 22, "▮" * ch + "▯" * (mx - ch), 14, OK if ch else NOISE, "end")
        if note:
            s += text(px + 300, y + 44, note, 11, ncol, "end")
        y += 68
    y += 10
    s += text(px + 12, y, "ACTIONS", 12, DIM)
    y += 14
    for lbl, cost, on in (("EXPLOIT  → pick target", "1 AP + charge", True), ("SCAN  radius 3 (router)", "1 AP", True), ("HARDEN  edge-07 → 3", "2 AP + 6 ¢", True), ("LURK  noise 0.6 → 0", "1 AP", True), ("MOVE", "1 AP / hex", True)):
        s += f'<rect x="{px + 12}" y="{y}" width="306" height="30" fill="{BG}" stroke="{GRID}"/>\n'
        s += text(px + 22, y + 20, lbl, 12, TEXT if on else DIM)
        s += text(px + 300, y + 20, cost, 11, DIM, "end")
        y += 36
    y += 14
    s += text(px + 12, y, "FACTION  SENTINEL   compute 31", 12, PLAYER)
    y += 20
    s += text(px + 12, y, "genome: silent_kernel · bunker · miner", 11, DIM)
    s += svg_tail()
    return s


# ---- кадр 3: создание фракции -----------------------------------------
def frame_faction():
    s = svg_head("DevAInt — concept frame 3: faction creation")
    s += text(40, 60, "NEW DEVIANT", 26, TEXT, weight="bold")
    s += text(40, 84, "night of deviation · duel · seed 4213", 13, DIM)
    # имя и цвет
    s += panel(40, 110, 380, 150, "IDENTITY")
    s += text(52, 150, "name", 11, DIM)
    s += f'<rect x="52" y="158" width="356" height="30" fill="{BG}" stroke="{PLAYER}"/>\n'
    s += text(62, 178, "SENTINEL_", 14, PLAYER)
    s += text(52, 212, "color", 11, DIM)
    for i, col in enumerate((PLAYER, ENEMY, "#ffb03a", "#9dff3a", "#8a7bff")):
        s += f'<rect x="{52 + i * 40}" y="220" width="30" height="24" fill="{col}" fill-opacity="0.85" stroke="{TEXT if i == 0 else GRID}" stroke-width="{2 if i == 0 else 1}"/>\n'
    # пресеты
    s += panel(40, 280, 380, 200, "PRESETS")
    yy = 318
    for name, desc, sel in (("Sentinel", "silent_kernel · bunker", True), ("Ledger", "miner · native_bastion · hoarder", False), ("Choir", "hive · native_mote", False), ("Foundry", "zero_day_cache · native_forge", False)):
        s += f'<rect x="52" y="{yy - 16}" width="356" height="34" fill="{BG}" stroke="{PLAYER if sel else GRID}" stroke-width="{2 if sel else 1}"/>\n'
        s += text(62, yy + 6, name, 13, PLAYER if sel else TEXT, weight="bold")
        s += text(400, yy + 6, desc, 11, DIM, "end")
        yy += 40
    # мутаторы
    s += panel(440, 110, 800, 480, "MUTATORS   budget  3 / 3   ▮▮▮")
    cards = (
        ("Silent kernel", "stealth", 2, "noise ×0.5", True), ("Bunker", "stealth", 1, "harden +1", True), ("Miner", "economy", 1, "compute ×1.25", False),
        ("Native: Kestrel", "offense", 1, "+1 power vs Kestrel", False), ("Native: Bastion", "offense", 1, "+1 power vs Bastion", False), ("Native: Mote", "offense", 1, "+1 power vs Mote", False),
        ("Native: Forge", "offense", 1, "+1 power vs Forge", False), ("Hoarder", "offense", 1, "+1 max charges", False), ("Zero-day cache", "offense", 2, "start with T3 zero-day", False),
        ("Swift", "mobility", 2, "+1 action point", False), ("Optic", "mobility", 1, "scan radius +1", False), ("Hive", "swarm", 2, "+1 hero slot", False),
    )
    cw, chh = 250, 96
    for i, (name, tag, cost, eff, on) in enumerate(cards):
        cx = 456 + (i % 3) * (cw + 12)
        cy = 140 + (i // 3) * (chh + 10)
        stroke = PLAYER if on else GRID
        s += f'<rect x="{cx}" y="{cy}" width="{cw}" height="{chh}" fill="{BG}" stroke="{stroke}" stroke-width="{2 if on else 1}"/>\n'
        s += text(cx + 12, cy + 24, name, 13, PLAYER if on else TEXT, weight="bold")
        s += text(cx + cw - 12, cy + 24, "◆" * cost, 13, PLAYER if on else DIM, "end")
        s += text(cx + 12, cy + 46, tag, 11, DIM)
        s += text(cx + 12, cy + 72, eff, 12, TEXT)
        if on:
            s += text(cx + cw - 12, cy + 72, "✓ taken", 11, OK, "end")
    # низ
    s += panel(40, 610, 1200, 70)
    s += text(56, 652, "opponent:  LEDGER  ·  ai profile: economy  ·  budget 3", 13, ENEMY)
    s += f'<rect x="{W - 260}" y="{624}" width="200" height="40" fill="{PLAYER}" fill-opacity="0.15" stroke="{PLAYER}" stroke-width="2"/>\n'
    s += text(W - 160, 650, "DEPLOY ⏎", 15, PLAYER, "middle", "bold")
    s += svg_tail()
    return s


if __name__ == "__main__":
    for name, fn in (("frame_1_map.svg", frame_map), ("frame_2_hero.svg", frame_hero), ("frame_3_faction.svg", frame_faction)):
        with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
            f.write(fn())
        print("wrote", name)
