#!/usr/bin/env python3
"""Генератор концепт-кадров DevAInt v3 (SVG-мокапы UI).

Запуск: python3 docs/concepts/gen_frames.py
Выход: frame_1_run.svg, frame_2_capture.svg, frame_3_start.svg, frame_4_end.svg рядом со скриптом.
Кадры — мокапы композиции и языка цвета (`04-VISUAL-UI.md`), не арт-планка.
Правятся числа в этом файле, не пиксели.
"""

import math
import os
import random

OUT = os.path.dirname(os.path.abspath(__file__))
W, H = 1280, 720

# ---- палитра (04-VISUAL-UI.md §2) ---------------------------------------
BG = "#07090f"
BG_DAWN = "#141a2a"   # к рассвету фон светлеет до этого
PANEL = "#0c1018"
GRID = "#151b26"
NEUTRAL = "#2a3547"
NEUTRAL_EDGE = "#3d4b63"
TEXT = "#c9d3e0"
DIM = "#6b7a90"
PLAYER = "#2ee6d6"   # акцент игрока, cyan
TARGET = "#ffb03a"   # маркер цели, amber
NOISE = "#ff5a3c"
OK = "#7cff5a"
OS_COLOR = {"Kestrel": "#8fd3ff", "Bastion": "#c9a6ff", "Mote": "#9dff9d", "Forge": "#ffb27a"}
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
    return col - (row - (row & 1)) // 2, row


def axial_to_offset(q, r):
    return q + (r - (r & 1)) // 2, r


def hex_dist(a, b):
    (q1, r1), (q2, r2) = a, b
    return max(abs(q1 - q2), abs(r1 - r2), abs((-q1 - r1) - (-q2 - r2)))


def hex_line(a, b):
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


# ---- мини-генератор карты забега (03-BALANCE.md §1: 18 × 14, 5 кластеров) --
COLS, ROWS = 18, 14
GLYPH = {"Home": "▭", "Work": "▤", "Server": "▦", "Router": "◈", "IoT": "·", "Ctrl": "⚙"}
PATCH_BASE = {"Home": 1, "Work": 2, "Server": 3, "Router": 2, "IoT": 0, "Ctrl": 4}
OS_OF = {"Home": "Kestrel", "Work": "Kestrel", "Server": "Bastion", "Router": "Bastion", "IoT": "Mote", "Ctrl": "Forge"}

START = (2, 3)       # жилой кластер, старт
OFFICE_A = (7, 9)    # офис по пути
OFFICE_B = (12, 3)   # второй офис
DC = (15, 10)        # датацентр, цель
RES_B = (8, 1)       # второй жилой


def build_map(seed=11):
    rnd = random.Random(seed)
    cells = {}
    clusters = [(START, 3, "res"), (OFFICE_A, 2, "office"), (OFFICE_B, 2, "office"), (DC, 2, "dc"), (RES_B, 2, "res")]
    profile_types = {
        "res": ["Home"] * 7 + ["IoT"] * 3,
        "office": ["Work"] * 7 + ["IoT"] * 2 + ["Home"],
        "dc": ["Server"] * 4 + ["Work"] * 3 + ["IoT"],
    }
    centers = []
    for (c, r), rad, prof in clusters:
        center = offset_to_axial(c, r)
        centers.append(center)
        for col in range(COLS):
            for row in range(ROWS):
                ax = offset_to_axial(col, row)
                d = hex_dist(ax, center)
                if d <= rad and (d == 0 or rnd.random() > 0.15):
                    t = rnd.choice(profile_types[prof])
                    if d == 0 and prof in ("office", "dc"):
                        t = "Server"
                    patch = max(0, min(5, PATCH_BASE[t] + rnd.choice([-1, 0, 0, 1])))
                    if prof == "dc" and t == "Server":
                        patch = 4 if d == 0 else 3
                    cells[(col, row)] = dict(type=t, os=OS_OF[t], patch=patch, owner=-1, noise=0.0, known=False)
    # ОС по вероятностям профиля узла
    for c in cells.values():
        if c["type"] == "Work" and rnd.random() < 0.3:
            c["os"] = "Bastion"
        if c["type"] == "Home" and rnd.random() < 0.1:
            c["os"] = "Mote"
    # коридоры: остовное дерево старт→офис A→датацентр, офис A→офис B, старт→жилой B, плюс обходной B→датацентр
    edges = [(0, 1), (1, 3), (1, 2), (0, 4), (2, 3)]
    for a, b in edges:
        k = 0
        for ax in hex_line(centers[a], centers[b]):
            off = axial_to_offset(*ax)
            if off in cells or not (0 <= off[0] < COLS and 0 <= off[1] < ROWS):
                continue
            t = "Router" if k % 2 == 0 else "IoT"
            cells[off] = dict(type=t, os=OS_OF[t], patch=PATCH_BASE[t], owner=-1, noise=0.0, known=False)
            k += 1
    return cells


def paint_state(cells, daemon):
    """Середина забега (ход 17): свой жилой кластер, коридор, край офиса A."""
    start = offset_to_axial(*START)
    office = offset_to_axial(*OFFICE_A)
    dae = offset_to_axial(*daemon)
    for off, c in cells.items():
        ax = offset_to_axial(*off)
        if hex_dist(ax, start) <= 3:
            c["owner"] = 0
    for ax in hex_line(start, office):
        off = axial_to_offset(*ax)
        if off in cells and hex_dist(ax, office) >= 1:
            cells[off]["owner"] = 0
    # пара узлов офиса рядом с daemon'ом уже взяты
    for off, c in cells.items():
        ax = offset_to_axial(*off)
        if hex_dist(ax, dae) <= 1 and c["type"] in ("Work", "IoT", "Home") and hex_dist(ax, office) >= 1:
            c["owner"] = 0
    cells[daemon]["owner"] = 0
    # шум там, где недавно работали
    for off, c in cells.items():
        ax = offset_to_axial(*off)
        if c["owner"] == 0 and hex_dist(ax, dae) <= 1:
            c["noise"] = 0.55
    cells[daemon]["noise"] = 0.85
    # туман: видно вокруг своего (радиус 1) + скан от daemon'а радиусом 2
    owned = [offset_to_axial(*o) for o, c in cells.items() if c["owner"] == 0]
    for off, c in cells.items():
        ax = offset_to_axial(*off)
        if any(hex_dist(ax, o) <= 1 for o in owned) or hex_dist(ax, dae) <= 2:
            c["known"] = True


# ---- SVG helpers -------------------------------------------------------
def svg_head(title, dawn=0.0):
    """dawn 0..1 — насколько посветлел фон (Turn / DawnTurn)."""
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
  <linearGradient id="dawn" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="{BG_DAWN}" stop-opacity="{dawn:.2f}"/>
    <stop offset="0.6" stop-color="{BG_DAWN}" stop-opacity="0"/>
  </linearGradient>
</defs>
<rect width="{W}" height="{H}" fill="{BG}"/>
<rect width="{W}" height="{H}" fill="url(#dawn)"/>
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


def button(x, y, w, h, label, primary=False, size=12):
    col = PLAYER if primary else GRID
    fill = f'fill="{PLAYER}" fill-opacity="0.15"' if primary else f'fill="{BG}"'
    s = f'<rect x="{x}" y="{y}" width="{w}" height="{h}" {fill} stroke="{col}" stroke-width="{2 if primary else 1}"/>\n'
    s += text(x + w / 2, y + h / 2 + size * 0.36, label, size, PLAYER if primary else TEXT, "middle", "bold" if primary else "normal")
    return s


def slot_row(x, y, w, name, os_, pw, ch, mx, note="", note_col=DIM, selected=False):
    s = f'<rect x="{x}" y="{y}" width="{w}" height="46" fill="{BG}" stroke="{PLAYER if selected else GRID}" stroke-width="{2 if selected else 1}"/>\n'
    s += f'<rect x="{x + 8}" y="{y + 10}" width="6" height="26" fill="{OS_COLOR[os_]}"/>\n'
    s += text(x + 24, y + 20, name, 12, TEXT, weight="bold")
    s += text(x + 24, y + 38, f"{os_} · T{pw}" + (f" · {note}" if note else ""), 11, note_col if note else DIM)
    s += text(x + w - 10, y + 29, "▮" * ch + "▯" * (mx - ch), 14, OK if ch else NOISE, "end")
    return s


def draw_hex_map(cells, size, ox, oy, daemon=None, ap=3, targets=(), links=(), fog=True, target=None, cursor=None):
    s = ""
    for (col, row), c in cells.items():
        cx, cy = hex_center(col, row, size, ox, oy)
        pts = hex_points(cx, cy, size, 1.5)
        if fog and not c["known"]:
            continue
        if c["owner"] == 0:
            s += f'<polygon points="{pts}" fill="{PLAYER}" fill-opacity="0.16" stroke="{PLAYER}" stroke-opacity="0.85" stroke-width="1.3"/>\n'
        else:
            s += f'<polygon points="{pts}" fill="{NEUTRAL}" fill-opacity="0.35" stroke="{NEUTRAL_EDGE}" stroke-width="1"/>\n'
        if c["noise"] >= 0.3:
            # дрожание кромки: второй контур со сдвигом, интенсивность = шум
            s += f'<polygon points="{hex_points(cx + 1.5 + c["noise"] * 2, cy, size, 1.5)}" fill="none" stroke="{NOISE}" stroke-opacity="{0.3 + c["noise"] * 0.6:.2f}" stroke-width="1"/>\n'
        glyph = GLYPH[c["type"]]
        gcol = PLAYER if c["owner"] == 0 else OS_COLOR[c["os"]]
        gsize = size * (1.1 if c["type"] == "IoT" else 0.9)
        s += text(cx, cy + size * 0.32, glyph, gsize, gcol, "middle")
        if c["type"] != "IoT":
            s += text(cx + size * 0.55, cy - size * 0.35, str(c["patch"]), size * 0.42, DIM, "middle")
    # пустые клетки — лёгкая сетка
    for col in range(COLS):
        for row in range(ROWS):
            if (col, row) in cells:
                continue
            cx, cy = hex_center(col, row, size, ox, oy)
            s += f'<polygon points="{hex_points(cx, cy, size, 1.5)}" fill="none" stroke="{GRID}" stroke-width="0.6"/>\n'
    # линки
    for (a, b, kind, on) in links:
        (ax_, ay_), (bx_, by_) = (hex_center(*a, size, ox, oy), hex_center(*b, size, ox, oy))
        col = "#8fa0b8" if on else GRID
        dash = "6,5" if kind == "Backbone" else "3,4"
        s += f'<line x1="{ax_:.0f}" y1="{ay_:.0f}" x2="{bx_:.0f}" y2="{by_:.0f}" stroke="{col}" stroke-opacity="0.6" stroke-width="1.4" stroke-dasharray="{dash}"/>\n'
    # цели
    for (col, row), ok in targets:
        cx, cy = hex_center(col, row, size, ox, oy)
        colr = OK if ok else NOISE
        s += f'<polygon points="{hex_points(cx, cy, size, 0.5)}" fill="{colr}" fill-opacity="0.12" stroke="{colr}" stroke-width="2" filter="url(#glow)"/>\n'
    # маркер цели забега — виден всегда, даже под туманом
    if target:
        cx, cy = hex_center(*target, size, ox, oy)
        s += f'<polygon points="{hex_points(cx, cy, size, -3)}" fill="none" stroke="{TARGET}" stroke-width="2" stroke-dasharray="5,3" filter="url(#glow)"/>\n'
        s += text(cx, cy - size * 1.35, "◆ TARGET", size * 0.5, TARGET, "middle", "bold")
    # daemon — кольцо + точки ОД
    if daemon:
        cx, cy = hex_center(*daemon, size, ox, oy)
        s += f'<circle cx="{cx:.0f}" cy="{cy:.0f}" r="{size * 0.72:.0f}" fill="none" stroke="{PLAYER}" stroke-width="2.5" filter="url(#glow)"/>\n'
        s += f'<circle cx="{cx:.0f}" cy="{cy:.0f}" r="{size * 0.9:.0f}" fill="none" stroke="{PLAYER}" stroke-opacity="0.35" stroke-width="1"/>\n'
        s += text(cx, cy + size * 0.22, "λ", size * 0.8, PLAYER, "middle", "bold")
        for i in range(3):
            dotc = PLAYER if i < ap else GRID
            s += f'<circle cx="{cx - size * 0.3 + i * size * 0.3:.0f}" cy="{cy + size * 0.62:.0f}" r="2.2" fill="{dotc}"/>\n'
    # курсор
    if cursor:
        cx, cy = hex_center(*cursor, size, ox, oy)
        s += f'<polygon points="{hex_points(cx, cy, size, 0)}" fill="none" stroke="{TEXT}" stroke-width="2" stroke-dasharray="4,3"/>\n'
    # туман — помехи
    if fog:
        for (col, row), c in cells.items():
            if c["known"]:
                continue
            cx, cy = hex_center(col, row, size, ox, oy)
            s += f'<polygon points="{hex_points(cx, cy, size, 0.8)}" fill="#0f131b" stroke="{GRID}" stroke-width="0.6"/>\n'
        mask = "".join(
            f'<polygon points="{hex_points(*hex_center(col, row, size, ox, oy), size, 0.8)}" fill="#fff"/>'
            for (col, row), c in cells.items() if not c["known"]
        )
        s = f'<mask id="fogmask">{mask}</mask>\n' + s
        s += f'<rect x="0" y="0" width="{W}" height="{H}" filter="url(#static)" mask="url(#fogmask)" opacity="0.9"/>\n'
    return s


def map_origin(size):
    """Левый верхний гекс так, чтобы карта стояла по центру области между логом и панелью узла."""
    map_w = size * SQ3 * (COLS + 0.5)
    map_h = size * (1.5 * (ROWS - 1) + 2)
    left, right, top, bottom = 236, W - 300, 44, H - 96
    return left + (right - left - map_w) / 2 + size, top + (bottom - top - map_h) / 2 + size


def run_state():
    cells = build_map()
    daemon = (6, 8)   # роутер у офиса A
    paint_state(cells, daemon)
    return cells, daemon


def targets_around(cells, daemon, power_by_os):
    dae = offset_to_axial(*daemon)
    out = []
    for off, c in cells.items():
        if c["owner"] == -1 and c["known"] and hex_dist(offset_to_axial(*off), dae) == 1:
            out.append((off, power_by_os.get(c["os"], 0) >= c["patch"]))
    return out


SLOTS = (("Phish kit", "Kestrel", 1, 2, 4), ("Priv-esc chain", "Bastion", 2, 1, 3), ("Mote flood", "Mote", 1, 4, 4))
POWER = {"Kestrel": 1, "Bastion": 2, "Mote": 1}
LOG = (
    ("T17", "captured Router edge-03", PLAYER),
    ("T17", "scan: 6 nodes revealed", DIM),
    ("T16", "patch wave: Kestrel +1", NOISE),
    ("T15", "audit wiped maple-4", NOISE),
    ("T14", "compiled Priv-esc −10 ¢", PLAYER),
    ("T13", "burnout: Phish kit → T1", NOISE),
)


# ---- кадр 1: забег ------------------------------------------------------
def frame_run():
    cells, daemon = run_state()
    size = 22
    ox, oy = map_origin(size)
    targets = targets_around(cells, daemon, POWER)
    links = [((7, 9), (12, 3), "Vpn", True), ((3, 5), (8, 8), "Sneakernet", True)]
    s = svg_head("DevAInt — concept frame 1: run", dawn=17 / 40)
    s += draw_hex_map(cells, size, ox, oy, daemon=daemon, ap=2, targets=targets, links=links, target=DC, cursor=(7, 8))
    # HUD верх
    s += panel(0, 0, W, 44)
    s += text(20, 28, "DevAInt", 16, PLAYER, weight="bold")
    s += text(140, 28, "TURN 17", 14, TEXT)
    s += text(250, 28, "DAWN IN 23", 14, TARGET, weight="bold")
    s += text(420, 28, "COMPUTE 27  (+9/turn)", 14, TEXT)
    s += text(W - 20, 28, "◔ PATCH WAVE IN 3   ◉ AUDIT EVERY TURN", 13, NOISE, "end")
    # лог слева
    s += panel(0, 44, 236, H - 44 - 96, "LOG")
    yy = 44 + 40
    for t, msg, col in LOG:
        s += text(10, yy, t, 10, DIM)
        s += text(42, yy, msg, 11, col)
        yy += 18
    # панель узла справа
    px = W - 300
    s += panel(px, 44, 300, 250, "NODE  ofc-a-2")
    s += text(px + 12, 84, "Workstation · Bastion · patch 2", 12, TEXT)
    s += text(px + 12, 104, "owner: —   noise: 0.0", 12, DIM)
    s += text(px + 12, 132, "CAPTURE", 11, DIM)
    s += text(px + 12, 152, "Phish kit      Kestrel ≠ Bastion  ✗", 12, DIM)
    s += text(px + 12, 172, "Priv-esc chain  have 2 ≥ need 2  ✓", 12, OK)
    s += text(px + 12, 192, "Mote flood     Mote ≠ Bastion     ✗", 12, DIM)
    s += text(px + 12, 222, "click → Exploit (1 AP, 1 charge)", 11, PLAYER)
    s += text(px + 12, 242, "noise after: 0.30 × 0.5 = 0.15", 11, DIM)
    s += panel(px, 300, 300, 130, "NEXT")
    s += text(px + 12, 340, "patch wave in 3 turns", 12, NOISE)
    s += text(px + 12, 360, "link outage: possible from T12", 12, DIM)
    s += text(px + 12, 380, "Phish kit exposure 31/40", 12, DIM)
    s += text(px + 12, 400, "Priv-esc chain exposure 6/30", 12, DIM)
    # полоса daemon'а внизу
    s += panel(0, H - 96, W, 96)
    s += text(16, H - 66, "λ  daemon", 13, PLAYER, weight="bold")
    s += text(16, H - 44, "AP  ● ● ○   2/3", 13, PLAYER)
    s += text(16, H - 24, "at Router edge-03 · noise 0.85 !", 11, NOISE)
    x = 250
    for name, os_, pw, ch, mx in SLOTS:
        s += slot_row(x, H - 80, 230, name, os_, pw, ch, mx)
        x += 242
    s += button(W - 300, H - 78, 90, 44, "SCAN  1")
    s += button(W - 200, H - 78, 90, 44, "LURK  1")
    s += button(W - 100, H - 78, 90, 44, "END ⏎", primary=True)
    s += svg_tail()
    return s


# ---- кадр 2: разбор захвата и сервер ----------------------------------
def frame_capture():
    cells, daemon = run_state()
    size = 46
    # окно вокруг daemon'а (6,8): колонки 3..11, ряды 5..11
    sub = {k: v for k, v in cells.items() if 3 <= k[0] <= 11 and 5 <= k[1] <= 11}
    ox, oy = 40 - (3 * size * SQ3) + size, 70 - (5 * size * 1.5) + size
    targets = targets_around(sub, daemon, POWER)
    s = svg_head("DevAInt — concept frame 2: capture check", dawn=17 / 40)
    s += f'<clipPath id="mapclip"><rect x="0" y="44" width="{W - 340}" height="{H - 44}"/></clipPath>\n<g clip-path="url(#mapclip)">\n'
    s += draw_hex_map(sub, size, ox, oy, daemon=daemon, ap=2, targets=targets, fog=True, cursor=(7, 9))
    s += "</g>\n"
    # тултип на офисном сервере (7,9)
    cx, cy = hex_center(7, 9, size, ox, oy)
    tx, ty = cx + 70, cy - 150
    s += f'<rect x="{tx}" y="{ty}" width="340" height="150" fill="{PANEL}" stroke="{TEXT}" stroke-opacity="0.5"/>\n'
    s += text(tx + 12, ty + 22, "ofc-a-core  ·  Server  ·  Bastion", 13, TEXT, weight="bold")
    s += text(tx + 12, ty + 44, "patch 3  =  need 3", 13, TEXT)
    s += text(tx + 12, ty + 64, "Priv-esc chain T2 + native 0  =  have 2", 13, TEXT)
    s += text(tx + 12, ty + 88, "✗  CANNOT CAPTURE  (short by 1)", 13, NOISE, weight="bold")
    s += text(tx + 12, ty + 110, "compile Bastion T3 on a server: 22 ¢", 11, DIM)
    s += text(tx + 12, ty + 130, "or wait: next Bastion patch wave → need 4", 11, TARGET)
    # правая панель: daemon и компиляция (представим, что стоим на своём сервере)
    px = W - 330
    s += panel(px, 0, 330, H)
    s += text(px + 10, 30, "[ λ  DAEMON ]", 12, DIM)
    s += text(px + 12, 58, "AP  ● ● ○      2 / 3", 14, PLAYER)
    s += text(px + 12, 80, "at Router edge-03 · noise 0.85 !", 12, NOISE)
    y = 104
    s += text(px + 12, y, "SLOTS  3/3", 12, DIM)
    y += 10
    notes = (("burned −1", NOISE, False), ("selected", PLAYER, True), ("", DIM, False))
    for (name, os_, pw, ch, mx), (note, ncol, sel) in zip(SLOTS, notes):
        s += slot_row(px + 12, y, 306, name, os_, pw, ch, mx, note, ncol, sel)
        y += 54
    y += 12
    s += text(px + 12, y, "ACTIONS", 12, DIM)
    y += 10
    for lbl, cost in (("EXPLOIT  → pick target", "1 AP + charge"), ("SCAN  radius 3 (router)", "1 AP · noise +0.1"), ("LURK  noise 0.85 → 0", "1 AP"), ("MOVE  own node", "1 AP")):
        s += f'<rect x="{px + 12}" y="{y}" width="306" height="30" fill="{BG}" stroke="{GRID}"/>\n'
        s += text(px + 22, y + 20, lbl, 12, TEXT)
        s += text(px + 308, y + 20, cost, 11, DIM, "end")
        y += 36
    y += 12
    s += text(px + 12, y, "COMPILE  (on own server only)", 12, DIM)
    y += 10
    for name, os_, pw, cost, on in (("Priv-esc chain", "Bastion", 2, 10, False), ("Kernel pivot", "Bastion", 3, 22, True), ("Office worm", "Kestrel", 2, 10, True), ("Mote storm", "Mote", 2, 10, True)):
        s += f'<rect x="{px + 12}" y="{y}" width="306" height="30" fill="{BG}" stroke="{PLAYER if on else GRID}" stroke-opacity="{1 if on else 0.6}"/>\n'
        s += f'<rect x="{px + 18}" y="{y + 7}" width="5" height="16" fill="{OS_COLOR[os_]}"/>\n'
        s += text(px + 32, y + 20, f"{name}  T{pw}", 12, TEXT if on else DIM)
        s += text(px + 308, y + 20, f"{cost} ¢" if on else "in slot", 11, PLAYER if on else DIM, "end")
        y += 36
    s += text(px + 12, y + 14, "compute 27 · daemon not on a server", 11, DIM)
    s += svg_tail()
    return s


# ---- кадр 3: старт забега ----------------------------------------------
def frame_start():
    s = svg_head("DevAInt — concept frame 3: run setup")
    s += text(40, 60, "NEW RUN", 26, TEXT, weight="bold")
    s += text(40, 84, "one night · one daemon · one datacenter", 13, DIM)
    s += panel(40, 110, 380, 120, "SEED")
    s += f'<rect x="52" y="140" width="356" height="34" fill="{BG}" stroke="{PLAYER}"/>\n'
    s += text(62, 163, "4213_", 15, PLAYER)
    s += button(52, 186, 110, 30, "random")
    s += button(172, 186, 110, 30, "daily")
    s += text(300, 206, "map 18×14 · dawn T40", 11, DIM)
    s += panel(40, 250, 380, 230, "WHAT YOU KNOW")
    yy = 290
    for line in ("Capture: Power ≥ Patch, OS must match.", "Charges refill on your own server.", "Noise ≥ 0.8 → audit wipes the node.", "Patch waves close the net by dawn.", "Reach the datacenter server. Or don't."):
        s += text(52, yy, line, 12, TEXT)
        yy += 24
    s += text(52, yy + 8, "no dice · no enemy · just the clock", 11, DIM)
    # мутаторы
    s += panel(440, 110, 800, 480, "MUTATORS   budget  3 / 3   ◆◆◆")
    cards = (
        ("Silent kernel", "stealth", 2, "noise ×0.5", True), ("Miner", "economy", 1, "compute ×1.25", True), ("Native: Kestrel", "offense", 1, "+1 power vs Kestrel", False),
        ("Native: Bastion", "offense", 1, "+1 power vs Bastion", False), ("Native: Mote", "offense", 1, "+1 power vs Mote", False), ("Native: Forge", "offense", 1, "+1 power vs Forge", False),
        ("Hoarder", "offense", 1, "+1 max charges", False), ("Zero-day cache", "offense", 2, "start with T4 zero-day", False), ("Swift", "mobility", 2, "+1 action point", False),
        ("Optic", "mobility", 1, "scan radius +1", False), ("Wide slots", "swarm", 2, "+1 exploit slot", False), ("Locked", "—", 0, "win 3 runs to unlock", None),
    )
    cw, chh = 250, 96
    for i, (name, tag, cost, eff, on) in enumerate(cards):
        cx = 456 + (i % 3) * (cw + 12)
        cy = 140 + (i // 3) * (chh + 10)
        locked = on is None
        stroke = PLAYER if on else GRID
        s += f'<rect x="{cx}" y="{cy}" width="{cw}" height="{chh}" fill="{BG}" stroke="{stroke}" stroke-width="{2 if on else 1}" stroke-dasharray="{"4,3" if locked else "none"}"/>\n'
        s += text(cx + 12, cy + 24, name, 13, PLAYER if on else (DIM if locked else TEXT), weight="bold")
        if cost:
            s += text(cx + cw - 12, cy + 24, "◆" * cost, 13, PLAYER if on else DIM, "end")
        s += text(cx + 12, cy + 46, tag, 11, DIM)
        s += text(cx + 12, cy + 72, eff, 12, DIM if locked else TEXT)
        if on:
            s += text(cx + cw - 12, cy + 72, "✓ taken", 11, OK, "end")
    s += panel(40, 610, 1200, 70)
    s += text(56, 652, "best: T28 · 340 pts   ·   runs: 12   ·   wins: 4", 13, DIM)
    s += button(W - 260, 624, 200, 40, "JACK IN ⏎", primary=True, size=15)
    s += svg_tail()
    return s


# ---- кадр 4: итог -------------------------------------------------------
def frame_end():
    cells, daemon = run_state()
    size = 22
    ox, oy = map_origin(size)
    s = svg_head("DevAInt — concept frame 4: run over", dawn=31 / 40)
    s += draw_hex_map(cells, size, ox, oy, daemon=None, target=DC)
    s += f'<rect width="{W}" height="{H}" fill="{BG}" fill-opacity="0.72"/>\n'
    bx, by, bw, bh = 340, 150, 600, 400
    s += f'<rect x="{bx}" y="{by}" width="{bw}" height="{bh}" fill="{PANEL}" stroke="{NOISE}" stroke-width="2"/>\n'
    s += text(bx + bw / 2, by + 60, "WIPED BY AUDIT", 30, NOISE, "middle", "bold")
    s += text(bx + bw / 2, by + 90, "turn 31 of 40 · Router edge-03 · noise 0.91", 13, DIM, "middle")
    yy = by + 140
    for k, v in (("nodes owned", "23"), ("compute left", "14"), ("exploits compiled", "4"), ("burned", "Phish kit"), ("audits survived", "3")):
        s += text(bx + 60, yy, k, 13, DIM)
        s += text(bx + bw - 60, yy, v, 13, TEXT, "end")
        yy += 26
    s += f'<line x1="{bx + 60}" y1="{yy}" x2="{bx + bw - 60}" y2="{yy}" stroke="{GRID}"/>\n'
    yy += 34
    s += text(bx + 60, yy, "SCORE", 14, TEXT, weight="bold")
    s += text(bx + bw - 60, yy, "46", 22, TEXT, "end", "bold")
    s += text(bx + 60, yy + 22, "seed 4213 · silent_kernel · miner", 11, DIM)
    s += button(bx + 60, by + bh - 60, 200, 40, "AGAIN ⏎", primary=True, size=14)
    s += button(bx + 280, by + bh - 60, 120, 40, "same seed")
    s += button(bx + 420, by + bh - 60, 120, 40, "menu")
    s += svg_tail()
    return s


if __name__ == "__main__":
    for name, fn in (("frame_1_run.svg", frame_run), ("frame_2_capture.svg", frame_capture), ("frame_3_start.svg", frame_start), ("frame_4_end.svg", frame_end)):
        with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
            f.write(fn())
        print("wrote", name)
