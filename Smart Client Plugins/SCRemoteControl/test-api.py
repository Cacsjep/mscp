"""
End-to-end test script for the SCRemoteControl REST API.

Usage:
    python test-api.py                       # runs the full test pass
    python test-api.py --token <token>       # override the bundled token
    python test-api.py --base http://host:9500
    python test-api.py --demo                # skip tests, run the live fill-meter demo only

No third-party dependencies. Uses stdlib urllib + json so it runs on any Python 3.6+.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from typing import Any

# Update these or pass --token / --base on the command line.
DEFAULT_BASE = "http://localhost:9500"
DEFAULT_TOKEN = "334e559dea1c4930009ec24709c6d6ce"


# ────────────────────────────────────────────────────────────────────────────
# Pretty output
# ────────────────────────────────────────────────────────────────────────────

class C:
    YEL = "\033[33m"
    CYAN = "\033[36m"
    GREEN = "\033[32m"
    RED = "\033[31m"
    GRAY = "\033[90m"
    BOLD = "\033[1m"
    OFF = "\033[0m"


def banner(text: str, color: str = C.YEL) -> None:
    bar = "=" * 60
    print(f"\n{color}{bar}{C.OFF}")
    print(f"{color}{C.BOLD} {text}{C.OFF}")
    print(f"{color}{bar}{C.OFF}")


def ok(msg: str) -> None: print(f"  {C.GREEN}✓{C.OFF} {msg}")
def fail(msg: str) -> None: print(f"  {C.RED}✗{C.OFF} {msg}")
def info(msg: str) -> None: print(f"  {C.GRAY}-{C.OFF} {msg}")


# ────────────────────────────────────────────────────────────────────────────
# HTTP helper
# ────────────────────────────────────────────────────────────────────────────

class Client:
    def __init__(self, base: str, token: str):
        self.base = base.rstrip("/")
        self.token = token

    def call(self, method: str, path: str, body: dict | None = None,
             expect: int | None = None, with_auth: bool = True,
             query: dict | None = None) -> tuple[int, Any]:
        url = self.base + path
        if query:
            url += "?" + urllib.parse.urlencode(query)
        data = None
        headers = {"Content-Type": "application/json"}
        if with_auth:
            headers["Authorization"] = f"Bearer {self.token}"
        if body is not None:
            data = json.dumps(body).encode("utf-8")
        req = urllib.request.Request(url, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=15) as resp:
                raw = resp.read()
                status = resp.status
                payload = json.loads(raw) if raw else None
        except urllib.error.HTTPError as e:
            raw = e.read()
            status = e.code
            try:
                payload = json.loads(raw)
            except Exception:
                payload = raw.decode("utf-8", errors="replace") if raw else None
        except Exception as ex:
            return 0, {"error": f"{type(ex).__name__}: {ex}"}

        label = f"{C.CYAN}{method:6}{C.OFF} {path}{(' ' + urllib.parse.urlencode(query)) if query else ''}"
        if expect is None or status == expect:
            tag = f"{C.GREEN}{status}{C.OFF}"
        else:
            tag = f"{C.RED}{status} (expected {expect}){C.OFF}"
        print(f"  {tag}  {label}")
        if status == 0 or (expect is not None and status != expect):
            if isinstance(payload, (dict, list)):
                snippet = json.dumps(payload, indent=2)[:600]
                print(f"    {C.GRAY}{snippet}{C.OFF}")
            else:
                print(f"    {C.GRAY}{payload}{C.OFF}")
        return status, payload


# ────────────────────────────────────────────────────────────────────────────
# SVG generators (used by both tests and the demo)
# ────────────────────────────────────────────────────────────────────────────

GAUGE_BANDS = [
    (0, 20, "#ef4444"),
    (20, 40, "#f97316"),
    (40, 60, "#facc15"),
    (60, 80, "#84cc16"),
    (80, 100, "#22c55e"),
]

COOL_BANDS = [
    (0, 20, "#1d4ed8"),
    (20, 40, "#2563eb"),
    (40, 60, "#0ea5e9"),
    (60, 80, "#06b6d4"),
    (80, 100, "#14b8a6"),
]

WARM_BANDS = [
    (0, 20, "#b91c1c"),
    (20, 40, "#ea580c"),
    (40, 60, "#f59e0b"),
    (60, 80, "#eab308"),
    (80, 100, "#84cc16"),
]

PINK_BANDS = [
    (0, 20, "#e11d48"),
    (20, 40, "#f97316"),
    (40, 60, "#facc15"),
    (60, 80, "#a3e635"),
    (80, 100, "#22c55e"),
]


def _text_width(text: str, font_size: float, factor: float = 0.55) -> float:
    """Rough sans-serif glyph-advance estimate. The plugin's SVG subset has no
    text-anchor, so we have to center text manually by offsetting x."""
    return len(text) * font_size * factor


def _band_color(value: float, bands: list[tuple[int, int, str]] = GAUGE_BANDS) -> str:
    for low, high, color in bands:
        if low <= value <= high:
            return color
    return bands[-1][2]


def _polar(cx: float, cy: float, r: float, deg: float) -> tuple[float, float]:
    rad = math.radians(deg)
    return cx + r * math.cos(rad), cy + r * math.sin(rad)


def _arc_points(cx: float, cy: float, r: float, start_deg: float, end_deg: float, steps: int = 18) -> str:
    pts = []
    for i in range(steps + 1):
        t = i / steps
        deg = start_deg + (end_deg - start_deg) * t
        x, y = _polar(cx, cy, r, deg)
        pts.append(f"{x:.1f},{y:.1f}")
    return " ".join(pts)


def _semi_gauge(parts: list[str], cx: float, cy: float, r: float, value: float,
                title: str = "", number: str | None = None, needle: bool = True,
                ring_w: float = 12, card: bool = True,
                bands: list[tuple[int, int, str]] = GAUGE_BANDS) -> None:
    if card:
        parts.append(
            f"<rect x='{cx - r - 22:.1f}' y='{cy - r - 18:.1f}' width='{2 * r + 44:.1f}' "
            f"height='{r + 52:.1f}' rx='20' ry='20' fill='#05070a' fill-opacity='0.58' "
            f"stroke='white' stroke-opacity='0.12' stroke-width='1'/>"
        )
    for low, high, color in bands:
        start = 180 - high * 1.8
        end = 180 - low * 1.8
        parts.append(
            f"<polyline points='{_arc_points(cx, cy, r, start, end)}' fill='none' "
            f"stroke='{color}' stroke-width='{ring_w}' stroke-linecap='round' stroke-linejoin='round'/>"
        )
    parts.append(
        f"<polyline points='{_arc_points(cx, cy, r - ring_w - 6, 180, 0)}' fill='none' "
        f"stroke='white' stroke-opacity='0.08' stroke-width='2'/>"
    )
    if needle:
        deg = 180 - value * 1.8
        nx, ny = _polar(cx, cy, r - 14, deg)
        parts.append(
            f"<line x1='{cx:.1f}' y1='{cy:.1f}' x2='{nx:.1f}' y2='{ny:.1f}' "
            f"stroke='#d1d5db' stroke-width='3' stroke-linecap='round'/>"
        )
        parts.append(
            f"<circle cx='{cx:.1f}' cy='{cy:.1f}' r='6' fill='#111827' stroke='white' stroke-opacity='0.55' stroke-width='1'/>"
        )
    if number:
        tx = cx - _text_width(number, 12, 0.50) / 2
        chip_top = cy - 36
        parts.append(
            f"<rect x='{cx - 15:.1f}' y='{chip_top:.1f}' width='30' height='18' rx='8' ry='8' "
            f"fill='#111827' fill-opacity='0.92' stroke='white' stroke-opacity='0.25' stroke-width='1'/>"
        )
        parts.append(
            f"<text x='{tx:.1f}' y='{chip_top + 14:.1f}' fill='white' font-size='12' font-weight='bold'>{number}</text>"
        )


def _donut_gauge(parts: list[str], cx: float, cy: float, r: float, value: float) -> None:
    parts.append(
        f"<rect x='{cx - r - 16:.1f}' y='{cy - r - 16:.1f}' width='{2 * r + 32:.1f}' "
        f"height='{2 * r + 32:.1f}' rx='18' ry='18' fill='#05070a' fill-opacity='0.36' "
        f"stroke='white' stroke-opacity='0.08' stroke-width='1'/>"
    )
    for i, (_, _, color) in enumerate(COOL_BANDS):
        start = -90 + i * 72
        end = start + 72
        parts.append(
            f"<polyline points='{_arc_points(cx, cy, r, start, end)}' fill='none' "
            f"stroke='{color}' stroke-width='12' stroke-linecap='round'/>"
        )
    # Needle: short pointer from just outside the center cap to just inside the ring
    deg = -90 + value * 3.6
    sx, sy = _polar(cx, cy, 16, deg)
    nx, ny = _polar(cx, cy, r - 9, deg)
    parts.append(
        f"<line x1='{sx:.1f}' y1='{sy:.1f}' x2='{nx:.1f}' y2='{ny:.1f}' "
        f"stroke='white' stroke-width='3' stroke-linecap='round'/>"
    )
    # Tick dot riding on the ring at the value position
    tx_dot, ty_dot = _polar(cx, cy, r, deg)
    parts.append(
        f"<circle cx='{tx_dot:.1f}' cy='{ty_dot:.1f}' r='4' fill='white' stroke='#111827' stroke-width='1.5'/>"
    )
    parts.append(f"<circle cx='{cx:.1f}' cy='{cy:.1f}' r='13' fill='#111827' fill-opacity='0.92'/>")
    number = str(int(round(value)))
    tx = cx - _text_width(number, 12, 0.50) / 2
    parts.append(f"<text x='{tx:.1f}' y='{cy + 3:.1f}' fill='white' font-size='12' font-weight='bold'>{number}</text>")


def _linear_gauge(parts: list[str], x: float, y: float, w: float, h: float, value: float,
                  rounded: bool = False, bands: list[tuple[int, int, str]] = GAUGE_BANDS) -> None:
    radius = h / 2 if rounded else 7
    parts.append(
        f"<rect x='{x:.1f}' y='{y:.1f}' width='{w:.1f}' height='{h:.1f}' rx='{radius:.1f}' ry='{radius:.1f}' "
        f"fill='#05070a' fill-opacity='0.40' stroke='white' stroke-opacity='0.08' stroke-width='1'/>"
    )
    inner_x, inner_y = x + 6, y + 6
    inner_w, inner_h = w - 12, h - 12
    for low, high, color in bands:
        seg_x = inner_x + inner_w * (low / 100.0)
        seg_w = inner_w * ((high - low) / 100.0)
        parts.append(
            f"<rect x='{seg_x:.1f}' y='{inner_y:.1f}' width='{seg_w:.1f}' height='{inner_h:.1f}' "
            f"rx='{max(2, inner_h / 3):.1f}' ry='{max(2, inner_h / 3):.1f}' fill='{color}'/>"
        )
    marker_x = inner_x + inner_w * (value / 100.0)
    chip_w = 22
    chip_h = 14
    chip_x = marker_x - chip_w / 2
    chip_y = y - 22
    parts.append(
        f"<rect x='{chip_x:.1f}' y='{chip_y:.1f}' width='{chip_w}' height='{chip_h}' rx='5' ry='5' "
        f"fill='#111827' fill-opacity='0.92' stroke='white' stroke-opacity='0.25' stroke-width='1'/>"
    )
    # Arrow connects chip to the top of the bar (apex points down into the bar)
    parts.append(
        f"<polygon points='{marker_x:.1f},{y + 1:.1f} {marker_x - 5:.1f},{chip_y + chip_h:.1f} {marker_x + 5:.1f},{chip_y + chip_h:.1f}' "
        f"fill='#111827' stroke='white' stroke-opacity='0.5' stroke-width='1'/>"
    )
    pct = str(int(round(value)))
    tx = marker_x - _text_width(pct, 8, 0.50) / 2
    parts.append(f"<text x='{tx:.1f}' y='{chip_y + 10:.1f}' fill='white' font-size='8' font-weight='bold'>{pct}</text>")


def _thermo_gauge(parts: list[str], x: float, y: float, value: float,
                  bands: list[tuple[int, int, str]] = GAUGE_BANDS, inner_w: float = 6) -> None:
    outer_w, tube_h = 14, 150
    bulb_r = 13
    cx = x + outer_w / 2
    cy = y + tube_h + 2
    fill_color = _band_color(value, bands)
    parts.append(
        f"<rect x='{x - 22:.1f}' y='{y - 12:.1f}' width='70' height='196' rx='18' ry='18' "
        f"fill='#05070a' fill-opacity='0.36' stroke='white' stroke-opacity='0.08' stroke-width='1'/>"
    )
    # Outer bulb (drawn first so the tube overlaps cleanly on top of it)
    parts.append(
        f"<circle cx='{cx:.1f}' cy='{cy:.1f}' r='{bulb_r}' fill='white' fill-opacity='0.12'/>"
    )
    # Tube shell - extended down a few px so it visually merges with the bulb
    parts.append(
        f"<rect x='{x:.1f}' y='{y:.1f}' width='{outer_w}' height='{tube_h + 6:.1f}' rx='7' ry='7' "
        f"fill='white' fill-opacity='0.12'/>"
    )
    inner_x = x + (outer_w - inner_w) / 2
    inner_y = y + 7
    inner_h = tube_h - 10
    parts.append(
        f"<rect x='{inner_x:.1f}' y='{inner_y:.1f}' width='{inner_w}' height='{inner_h:.1f}' rx='3' ry='3' fill='#0b0f14'/>"
    )
    # Inner bulb color (drawn before the level so the level mercury connects into it)
    parts.append(f"<circle cx='{cx:.1f}' cy='{cy:.1f}' r='{bulb_r - 4}' fill='{fill_color}'/>")
    level_h = inner_h * (value / 100.0)
    level_y = inner_y + inner_h - level_h
    # Mercury column extends a few px past the inner tube so it visually fuses with the bulb
    parts.append(
        f"<rect x='{inner_x:.1f}' y='{level_y:.1f}' width='{inner_w}' height='{level_h + 6:.1f}' rx='3' ry='3' fill='{fill_color}'/>"
    )
    mark_y = inner_y + inner_h - level_h
    parts.append(
        f"<line x1='{x - 10:.1f}' y1='{mark_y:.1f}' x2='{x - 2:.1f}' y2='{mark_y:.1f}' stroke='{fill_color}' stroke-width='2'/>"
    )
    pct = f"{int(round(value))}%"
    chip_w = 28
    chip_h = 14
    chip_x = x + 20
    chip_y = mark_y - chip_h / 2
    parts.append(
        f"<rect x='{chip_x:.1f}' y='{chip_y:.1f}' width='{chip_w}' height='{chip_h}' rx='5' ry='5' "
        f"fill='#111827' fill-opacity='0.92'/>"
    )
    tx = chip_x + (chip_w - _text_width(pct, 8, 0.50)) / 2
    parts.append(f"<text x='{tx:.1f}' y='{chip_y + 10:.1f}' fill='white' font-size='8' font-weight='bold'>{pct}</text>")


def gauge_svg(value: float) -> str:
    """Multi-style gauge showcase used by the demo overlay."""
    value = max(0.0, min(100.0, float(value)))
    values = [
        value,
        max(0.0, min(100.0, value * 0.88 + 6)),
        max(0.0, min(100.0, 100.0 - value * 0.45)),
        max(0.0, min(100.0, 35.0 + value * 0.5)),
    ]

    parts: list[str] = [
        "<rect x='18' y='18' width='964' height='324' rx='24' ry='24' fill='#05070a' fill-opacity='0.10'/>"
    ]

    _semi_gauge(parts, 150, 118, 44, values[0], "", str(int(round(values[0]))), True, 10, False, GAUGE_BANDS)
    _donut_gauge(parts, 385, 104, 42, values[1])
    _linear_gauge(parts, 520, 184, 210, 26, values[2], True, PINK_BANDS)
    _thermo_gauge(parts, 835, 92, values[3], COOL_BANDS, 10)

    return "<svg viewBox='0 0 1000 360'>" + "".join(parts) + "</svg>"


def simple_box_svg(text: str) -> str:
    return (
        "<svg viewBox='0 0 1000 1000'>"
        "<rect x='80' y='80' width='340' height='200' rx='14' ry='14' "
        "fill='red' fill-opacity='0.35' stroke='red' stroke-width='5'/>"
        f"<text x='110' y='200' fill='white' font-size='52' font-weight='bold'>{text}</text>"
        "</svg>"
    )


# ────────────────────────────────────────────────────────────────────────────
# Tests
# ────────────────────────────────────────────────────────────────────────────

def section_auth(c: Client) -> None:
    banner("Auth")
    # No token
    code, _ = c.call("GET", "/api/status", expect=401, with_auth=False)
    if code == 401: ok("rejects requests without a token")
    else: fail(f"expected 401, got {code}")

    # Bad token
    bad = Client(c.base, "deadbeef" * 4)
    code, _ = bad.call("GET", "/api/status", expect=401)
    if code == 401: ok("rejects an invalid token")
    else: fail(f"expected 401, got {code}")


def section_discovery(c: Client) -> dict:
    banner("Discovery")
    _, status = c.call("GET", "/api/status", expect=200)
    if status: info(f"server mode={status.get('mode')}  version={status.get('version')}")

    _, views = c.call("GET", "/api/views", expect=200)
    _, cameras = c.call("GET", "/api/cameras", expect=200)
    _, workspaces = c.call("GET", "/api/workspaces", expect=200)
    _, windows = c.call("GET", "/api/windows", expect=200)

    info(f"views={len(views or [])}  cameras={len(cameras or [])}  "
         f"workspaces={len(workspaces or [])}  windows={len(windows or [])}")

    return {
        "views": views or [],
        "cameras": cameras or [],
        "workspaces": workspaces or [],
        "windows": windows or [],
    }


def section_actions(c: Client, d: dict) -> None:
    banner("Actions")
    if d["views"]:
        c.call("POST", "/api/views/switch",
               body={"viewId": d["views"][0]["id"], "windowIndex": 0}, expect=200)
    else:
        info("no views available, skipping /api/views/switch")

    if d["cameras"]:
        ids = [cam["id"] for cam in d["cameras"][:4]]
        c.call("POST", "/api/cameras/show",
               body={"cameraIds": ids, "windowIndex": 0}, expect=200)
        time.sleep(1.5)
        c.call("POST", "/api/cameras/set",
               body={"cameraId": ids[0], "slotIndex": 0, "windowIndex": 0}, expect=200)
    else:
        info("no cameras available, skipping show/set")

    if d["workspaces"]:
        c.call("POST", "/api/workspaces/switch",
               body={"workspaceId": d["workspaces"][0]["id"]}, expect=200)

    c.call("POST", "/api/application/control", body={"command": "ToggleFullscreen"}, expect=200)
    time.sleep(1)
    c.call("POST", "/api/application/control", body={"command": "ToggleFullscreen"}, expect=200)


def section_overlay_crud(c: Client, d: dict) -> None:
    banner("Overlay CRUD")
    if not d["cameras"]:
        info("no cameras available, skipping overlay tests")
        return

    cam_id = d["cameras"][0]["id"]
    info(f"target camera: {d['cameras'][0]['name']} ({cam_id})")

    # Make sure the camera is on screen so we exercise the displayed=true path.
    c.call("POST", "/api/cameras/show",
           body={"cameraIds": [cam_id], "windowIndex": 0}, expect=200)
    time.sleep(1)

    # Clean slate
    c.call("DELETE", "/api/overlays", expect=200)

    # POST (create)
    code, body = c.call("POST", "/api/overlays", body={
        "overlayId": "test-box",
        "cameraId": cam_id,
        "svg": simple_box_svg("HELLO"),
    }, expect=201)
    if code == 201 and body and body.get("displayed"): ok("overlay displayed=true on first POST")
    elif code == 201: ok("overlay created (queued, camera not in viewport)")

    # GET list
    _, lst = c.call("GET", "/api/overlays", expect=200)
    if lst and any(o["overlayId"] == "test-box" for o in lst): ok("overlay appears in list")
    else: fail("overlay missing from list")

    # GET single
    _, single = c.call("GET", "/api/overlays/test-box", expect=200)
    if single and single.get("svg"): ok("GET returns the original SVG body")

    # POST same id with new SVG → upsert (200 OK, replaced=true)
    code, body = c.call("POST", "/api/overlays", body={
        "overlayId": "test-box",
        "cameraId": cam_id,
        "svg": simple_box_svg("UPDATED"),
    }, expect=200)
    if body and body.get("replaced"): ok("POST same overlayId returns replaced=true")
    else: fail("upsert did not set replaced=true")

    # Off-screen camera (use a different camera id we won't show)
    if len(d["cameras"]) > 1:
        off_id = d["cameras"][-1]["id"]
        code, body = c.call("POST", "/api/overlays", body={
            "overlayId": "test-offscreen",
            "cameraId": off_id,
            "svg": simple_box_svg("OFF"),
        }, expect=201)
        if body and not body.get("displayed") and body.get("warning"):
            ok("off-screen POST returns 201 with warning field")
        c.call("DELETE", "/api/overlays/test-offscreen", expect=200)

    # TTL (1 second, then verify it pruned)
    c.call("POST", "/api/overlays", body={
        "overlayId": "test-ttl",
        "cameraId": cam_id,
        "svg": simple_box_svg("TTL"),
        "ttlSeconds": 1,
    }, expect=201)
    time.sleep(2)
    code, _ = c.call("GET", "/api/overlays/test-ttl", expect=404)
    if code == 404: ok("TTL pruning worked, overlay gone after expiry")

    # DELETE by id
    code, _ = c.call("DELETE", "/api/overlays/test-box", expect=200)
    code, _ = c.call("GET", "/api/overlays/test-box", expect=404)
    if code == 404: ok("DELETE removed the overlay")

    # DELETE by cameraId (post a couple, then bulk delete)
    for i in range(3):
        c.call("POST", "/api/overlays", body={
            "overlayId": f"bulk-{i}",
            "cameraId": cam_id,
            "svg": simple_box_svg(f"#{i}"),
        }, expect=201)
    code, body = c.call("DELETE", "/api/overlays", query={"cameraId": cam_id}, expect=200)
    if body and body.get("removed", 0) >= 3: ok(f"DELETE by cameraId removed {body['removed']} overlays")

    # DELETE all
    c.call("POST", "/api/overlays", body={
        "overlayId": "any-1", "cameraId": cam_id, "svg": simple_box_svg("ANY"),
    }, expect=201)
    code, body = c.call("DELETE", "/api/overlays", expect=200)
    _, lst = c.call("GET", "/api/overlays", expect=200)
    if lst == []: ok("DELETE all wiped the registry")


def section_overlay_validation(c: Client, d: dict) -> None:
    banner("Overlay validation")
    cam_id = d["cameras"][0]["id"] if d["cameras"] else "00000000-0000-0000-0000-000000000001"

    # Missing fields
    c.call("POST", "/api/overlays", body={}, expect=400)
    c.call("POST", "/api/overlays",
           body={"overlayId": "x", "cameraId": cam_id}, expect=400)
    c.call("POST", "/api/overlays",
           body={"overlayId": "x", "svg": "<svg/>"}, expect=400)

    # Bad GUID
    c.call("POST", "/api/overlays",
           body={"overlayId": "x", "cameraId": "not-a-guid", "svg": "<svg/>"},
           expect=400)

    # Unknown camera (well-formed GUID, doesn't exist)
    c.call("POST", "/api/overlays",
           body={"overlayId": "x", "cameraId": "11111111-2222-3333-4444-555555555555",
                 "svg": "<svg/>"},
           expect=404)

    # Bad SVG (not XML)
    if d["cameras"]:
        c.call("POST", "/api/overlays",
               body={"overlayId": "x", "cameraId": cam_id, "svg": "this is not svg"},
               expect=400)

    # Wrong root element
    if d["cameras"]:
        c.call("POST", "/api/overlays",
               body={"overlayId": "x", "cameraId": cam_id, "svg": "<html/>"},
               expect=400)


def section_clear(c: Client) -> None:
    banner("Clear")
    c.call("POST", "/api/clear", body={"windowIndex": 0, "delaySeconds": 3}, expect=200)


# ────────────────────────────────────────────────────────────────────────────
# Live demos
# ────────────────────────────────────────────────────────────────────────────

def demo_fill_meter(c: Client, cam_id: str, seconds: int = 30) -> None:
    """Animates a fill-level gauge on the given camera for N seconds.

    Repost on the same overlayId every tick; the overlay updates in place
    with no flicker. Useful manual confirmation that the upsert path works
    smoothly under repeated POSTs.
    """
    banner(f"Live demo: fill-meter on {cam_id} for {seconds}s", C.GREEN)
    import math
    deadline = time.time() + seconds
    t0 = time.time()
    try:
        while time.time() < deadline:
            t = time.time() - t0
            # Smooth oscillation 0..100 with a slow drift, easier to eyeball
            value = 50 + 45 * math.sin(t * 0.6)
            c.call("POST", "/api/overlays", body={
                "overlayId": "demo-fill-meter",
                "cameraId":  cam_id,
                "svg":       gauge_svg(value),
            }, expect=200)  # 200 after first POST because it's an upsert
            time.sleep(0.3)
    finally:
        c.call("DELETE", "/api/overlays/demo-fill-meter", expect=200)


# ────────────────────────────────────────────────────────────────────────────
# Entry
# ────────────────────────────────────────────────────────────────────────────

def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--base", default=DEFAULT_BASE)
    p.add_argument("--token", default=DEFAULT_TOKEN)
    p.add_argument("--demo", action="store_true",
                   help="skip the test pass, just run the live fill-meter demo")
    p.add_argument("--demo-camera",
                   help="camera FQID for --demo (defaults to first listed camera)")
    p.add_argument("--demo-seconds", type=int, default=30)
    args = p.parse_args()

    c = Client(args.base, args.token)

    print(f"{C.BOLD}SCRemoteControl API tests{C.OFF}")
    print(f"  base : {args.base}")
    print(f"  token: {args.token[:6]}{'…' if len(args.token) > 6 else ''}")

    discovery = section_discovery(c)

    if args.demo:
        cam = args.demo_camera or (discovery["cameras"][0]["id"] if discovery["cameras"] else None)
        if not cam:
            fail("no camera available for --demo")
            return 1
        demo_fill_meter(c, cam, args.demo_seconds)
        return 0

    section_auth(c)
    section_actions(c, discovery)
    section_overlay_crud(c, discovery)
    section_overlay_validation(c, discovery)
    section_clear(c)

    banner("Done", C.GREEN)
    return 0


if __name__ == "__main__":
    sys.exit(main())
