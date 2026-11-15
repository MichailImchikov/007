"""
attachments.py - muzzle devices, optics, underbarrel, side-rail items and extended magazines.

Every attachment root origin is its mounting point (gun space mm, u forward / v up / w right):
  muzzle      : centre of the rear face, body along +u            (Socket_Muzzle at the new muzzle)
  top optic   : bottom centre of the clamp on the rail top, body up (+v)
  underbarrel : top centre of the clamp at the rail surface, body down (-v)
  side rail   : clamp mounting face centre, body towards the right (+w)
  magazine    : top centre where it seats in the well (same as the weapon's Mag origin)

Optics (integration rule): the body is HOLLOW along the sight axis (tube walls / open window only),
glass lives in separate meshes named Lens* (Lens_Front, Lens_Rear, Lens) with Glass_Lens, the reticle in
a mesh named Reticle; Sight_Point sits on the optical axis at the rear glass / window.
"""
import math

from mathutils import Vector

from .core import MB, Asset, fillet, rrect, circle, arc, T, Ru, Rw, about, annulus_sector, clean_poly
from .parts import (pic_clamp, ridged_knob, star, box_mag, CART_556, CART_545, CART_9MM, hex_screw, wire_loop)

MB_ = 'Metal_Black'
MD = 'Metal_Dark'
MW = 'Metal_Worn'
MK = 'Metal_Knurl'
PB = 'Polymer_Black'
PT = 'Polymer_Tan'
RU = 'Rubber'
GL = 'Glass_Lens'
RR = 'Reticle_Red'
EL = 'Emitter_Light'
ELZ = 'Emitter_Laser'

RIFLES = ['M4A1', 'AK12', 'M14EBR']


# ------------------------------------------------------------------------------ helpers
def slotted_ring(mb, u0, u1, r_in, r_out, slots, mat, cy=0.0):
    """Bars of a ring between angular slots [(centre_deg, width_deg)] (angles in the w/v plane,
    0 = right, 90 = up)."""
    iv = sorted(((c - w / 2.0) % 360.0, w) for c, w in slots)
    for i, (s0, w) in enumerate(iv):
        e0 = s0 + w
        nxt = iv[(i + 1) % len(iv)][0]
        if nxt <= e0:
            nxt += 360.0
        n = max(3, int((nxt - e0) / 8.0) + 1)
        mb.prism(annulus_sector(0.0, cy, r_in, r_out, e0, nxt, n), 'u', u0, u1, mat, bevel=0.3)


def flats_circle(r, flat, n=32):
    """Circle with two wrench flats at |w| = flat."""
    return [(max(-flat, min(flat, x)), y) for x, y in circle(0.0, 0.0, r, n)]


def knurl_ring(mb, u0, u1, r_out, r_in, ridges, mat, cy=0.0):
    """Hollow knurled ring around the bore axis (keeps the sight line open)."""
    outer = star(0.0, cy, r_out, 0.6, ridges)
    inner = circle(0.0, cy, r_in, len(outer))
    with mb.no_bevel():
        mb.tube_prism(outer, inner, 'u', u0, u1, mat)


def lenses(a, H, glasses):
    """glasses = [(name, u, radius)] -> separate Glass_Lens meshes on the optical axis (v = H)."""
    for name, u, r in glasses:
        m = MB()
        m.cyl(u - 0.4, u + 0.4, r, GL, c=(H, 0.0), seg=32)
        a.mesh(m, name, origin=(u, H, 0.0), bevel_mm=0.0)


# ------------------------------------------------------------------------------ muzzle devices
def muzzle_flash_hider():
    a = Asset('Muzzle_FlashHider', 'attachment', 'Muzzle', muzzleLength=0.055, compatible=RIFLES)
    mb = MB()
    mb.tube(0.0, 1.6, 10.4, 3.4, MD, seg=24)                                         # crush washer
    mb.lathe([(1.6, 3.4), (1.6, 10.3), (2.8, 11.0), (14.0, 11.0), (14.0, 3.4)], MB_, seg=32, closed=True)
    slotted_ring(mb, 14.0, 48.5, 7.6, 11.0, [(90, 22), (40, 22), (140, 22), (-10, 22), (190, 22)], MB_)
    mb.lathe([(48.5, 7.6), (48.5, 11.0), (53.8, 11.0), (55.0, 10.0), (55.0, 7.6)], MB_, seg=32, closed=True)
    mb.tube(14.0, 48.5, 3.6, 2.9, MB_, seg=12)
    a.mesh(mb, 'Body')
    a.socket('Socket_Muzzle', (55.0, 0.0, 0.0))
    return a


def muzzle_brake():
    a = Asset('Muzzle_Brake', 'attachment', 'Muzzle', muzzleLength=0.062, compatible=RIFLES + ['M700'])
    mb = MB()
    mb.prism(flats_circle(13.0, 11.0), 'u', 0.0, 14.0, MB_, bevel=0.6)
    for u0, u1 in ((24.0, 29.0), (37.0, 42.0)):
        mb.tube(u0, u1, 13.0, 4.4, MB_, seg=32, chamfer=0.4)
    mb.lathe([(50.0, 4.4), (50.0, 13.0), (60.0, 13.0), (62.0, 11.2), (62.0, 6.0), (59.0, 4.4)], MB_, seg=32,
             closed=True)
    for a0, a1 in ((48, 132), (228, 312)):
        mb.prism(annulus_sector(0.0, 0.0, 6.5, 13.0, a0, a1, 9), 'u', 13.5, 50.5, MB_, bevel=0.3)
    for u in (18.0, 32.5, 45.5):
        mb.cyl(11.5, 13.2, 1.6, RU, axis='v', c=(u, 0.0), seg=10)
    a.mesh(mb, 'Body')
    a.socket('Socket_Muzzle', (62.0, 0.0, 0.0))
    return a


def muzzle_pistol_comp():
    a = Asset('Muzzle_PistolComp', 'attachment', 'Muzzle', muzzleLength=0.035, compatible=['Glock17'])
    mb = MB()
    mb.tube(0.0, 8.0, 12.0, 6.8, MB_, seg=32, chamfer=0.6)
    mb.prism(annulus_sector(0.0, 0.0, 7.0, 12.0, 128.0, 412.0, 28), 'u', 7.8, 28.2, MB_, bevel=0.3)
    for u0, u1 in ((13.5, 16.5), (21.0, 23.5)):
        mb.prism(annulus_sector(0.0, 0.0, 7.0, 12.0, 46.0, 134.0, 8), 'u', u0, u1, MB_, bevel=0.3)
    mb.lathe([(28.0, 5.0), (28.0, 12.0), (33.5, 12.0), (35.0, 10.6), (35.0, 5.0)], MB_, seg=32, closed=True)
    for s in (1, -1):
        hex_screw(mb, (4.0, 0.0, s * 11.6), 'w', 1.8, 0.4, MB_, sign=s)
    a.mesh(mb, 'Body')
    a.socket('Socket_Muzzle', (35.0, 0.0, 0.0))
    return a


def _can(mb, prof, knurl=None, flats=None):
    mb.lathe(prof, MB_, seg=40)
    if knurl:
        u0, u1, r = knurl
        mb.prism(star(0.0, 0.0, r + 0.5, 0.7, 30), 'u', u0, u1, MK, nobevel=True)
    if flats:
        u0, u1, r, f = flats
        mb.prism(flats_circle(r, f, 40), 'u', u0, u1, MB_, bevel=0.5)


def suppressor_rifle():
    a = Asset('Suppressor_Rifle', 'attachment', 'Muzzle', muzzleLength=0.17, compatible=RIFLES)
    mb = MB()
    prof = [(0.0, 0.0), (0.0, 15.0), (1.5, 16.6), (21.0, 16.6), (23.0, 19.0), (40.0, 19.0), (40.6, 18.2),
            (42.4, 18.2), (43.0, 19.0), (145.0, 19.0), (145.6, 18.2), (147.4, 18.2), (148.0, 19.0),
            (166.0, 19.0), (170.0, 16.0), (170.0, 7.0), (165.0, 4.4), (150.0, 4.4), (150.0, 0.0)]
    _can(mb, prof, knurl=(3.0, 19.0, 16.6))
    a.mesh(mb, 'Body')
    a.socket('Socket_Muzzle', (170.0, 0.0, 0.0))
    return a


def suppressor_pistol():
    a = Asset('Suppressor_Pistol', 'attachment', 'Muzzle', muzzleLength=0.15, compatible=['Glock17'])
    mb = MB()
    prof = [(0.0, 0.0), (0.0, 12.6), (1.2, 14.0), (13.0, 14.0), (15.0, 16.0)]
    for g in (40.0, 75.0, 110.0):
        prof += [(g, 16.0), (g + 0.6, 15.3), (g + 2.4, 15.3), (g + 3.0, 16.0)]
    prof += [(136.0, 16.0), (140.0, 15.8), (144.0, 15.0), (146.6, 13.8), (148.6, 12.4), (149.6, 10.6), (150.0, 8.6),
             (150.0, 5.0), (146.0, 5.0), (146.0, 0.0)]
    _can(mb, prof, knurl=(2.0, 12.5, 14.0))
    a.mesh(mb, 'Body')
    a.socket('Socket_Muzzle', (150.0, 0.0, 0.0))
    return a


def suppressor_sniper():
    a = Asset('Suppressor_Sniper', 'attachment', 'Muzzle', muzzleLength=0.23, compatible=['M700', 'M14EBR'])
    mb = MB()
    prof = [(0.0, 0.0), (0.0, 18.0), (2.0, 20.4), (30.0, 20.4), (32.5, 22.5)]
    for g in (70.0, 120.0, 170.0):
        prof += [(g, 22.5), (g + 0.8, 21.6), (g + 3.2, 21.6), (g + 4.0, 22.5)]
    prof += [(221.0, 22.5), (228.0, 20.8), (230.0, 17.0), (230.0, 9.0), (224.0, 6.0), (205.0, 5.6), (205.0, 0.0)]
    _can(mb, prof, flats=(4.0, 27.0, 21.2, 18.0))
    a.mesh(mb, 'Body')
    a.socket('Socket_Muzzle', (230.0, 0.0, 0.0))
    return a


# ------------------------------------------------------------------------------ optics
def optic_reddot():
    """Aimpoint CompM4 style 30 mm tube on a QD riser (hollow tube, glass front/rear)."""
    H = 39.0
    a = Asset('Optic_RedDot', 'attachment', 'Optic', magnification=1.0, sightHeight=H / 1000.0, compatible=RIFLES)
    mb = MB()
    pic_clamp(mb, -16.0, 16.0, MB_, top=4.0, nut='qd')
    mb.prism(fillet([(-15.5, 3.5), (15.5, 3.5), (11.0, 24.0), (-11.0, 24.0)], [1, 1, 3, 3], 2), 'w', -10.5, 10.5,
             MB_, bevel=1.0)
    mb.box2((-8.5, 20.0, -11.0), (8.5, H - 15.0, 11.0), MB_, bevel=1.0)
    mb.tube(-8.5, 8.5, 19.6, 15.3, MB_, seg=32, c=(H, 0.0), chamfer=0.8)
    for s in (1, -1):
        hex_screw(mb, (0.0, H - 17.0, s * 11.0), 'w', 2.2, 0.8, MB_, sign=s)
    prof = [(-62.0, 15.6), (-62.0, 18.4), (-61.0, 19.4), (-47.0, 19.4), (-45.0, 15.2), (45.0, 15.2), (47.0, 19.4),
            (61.0, 19.4), (62.0, 18.4), (62.0, 15.6), (55.0, 15.6), (55.0, 14.0), (-55.0, 14.0), (-55.0, 15.6)]
    mb.lathe(prof, MB_, seg=40, c=(H, 0.0), closed=True)
    for u in (-58.0, 52.0):                                                         # grip rings on the hoods
        knurl_ring(mb, u, u + 6.0, 19.8, 15.8, 24, MK, cy=H)
    mb.cyl(H + 13.0, H + 20.0, 8.0, MB_, axis='v', c=(14.0, 0.0), seg=20)
    ridged_knob(mb, H + 20.0, H + 24.0, 8.8, MK, 'v', (14.0, 0.0), ridges=14, depth=0.6)
    mb.cyl(13.0, 20.0, 8.0, MB_, axis='w', c=(14.0, H), seg=20)
    ridged_knob(mb, 20.0, 24.0, 8.8, MK, 'w', (14.0, H), ridges=14, depth=0.6)
    mb.cyl(-13.0, -26.0, 8.0, MB_, axis='w', c=(-18.0, H), seg=20, chamfer=0.8)
    ridged_knob(mb, -26.0, -30.0, 8.6, MK, 'w', (-18.0, H), ridges=12, depth=0.6)
    a.mesh(mb, 'Body')
    lenses(a, H, [('Lens_Front', 55.0, 15.6), ('Lens_Rear', -55.0, 15.6)])
    ret = MB()
    ret.cyl(-0.15, 0.15, 1.0, RR, c=(H, 0.0), seg=16)
    a.mesh(ret, 'Reticle', origin=(0.0, H, 0.0), bevel_mm=0.0)
    a.socket('Sight_Point', (-55.0, H, 0.0))
    return a


def optic_holo():
    """EOTech EXPS style holographic sight: open window between two glass panes."""
    H = 41.0
    a = Asset('Optic_Holo', 'attachment', 'Optic', magnification=1.0, sightHeight=H / 1000.0, compatible=RIFLES)
    mb = MB()
    pic_clamp(mb, -28.0, 28.0, MB_, top=6.0, nut=None)
    for u in (-14.0, 14.0):
        mb.cyl(-13.0, -16.5, 5.0, MB_, axis='w', c=(u, -2.5), seg=16)
        mb.prism(fillet([(u - 3, -5.5), (u + 3, -5.5), (u + 3, 6.0), (u - 3, 6.0)], 1.5, 2), 'w', -16.5, -19.0, MB_,
                 bevel=0.5)
    house = fillet([(-50.0, 5.0), (34.0, 5.0), (34.0, 30.0), (-50.0, 30.0)], [3, 3, 2, 3], 2)
    mb.prism(house, 'w', -17.0, 17.0, PB, bevel=1.5, seg=2)
    hood = fillet([(-22.0, 28.0), (34.0, 28.0), (34.0, 57.0), (-14.0, 57.0), (-22.0, 49.0)], [0, 0, 3, 3, 3], 2)
    for s in (1, -1):
        mb.prism(hood, 'w', s * 15.0, s * 18.5, PB, bevel=1.0)
    mb.prism(fillet([(-14.0, 52.0), (34.0, 52.0), (34.0, 57.0), (-14.0, 57.0)], [0, 0, 2, 2], 2), 'w', -15.2, 15.2,
             PB, bevel=0.8)
    for w in (-6.0, 0.0, 6.0):
        mb.cyl(-50.0, -52.5, 2.6, RU, axis='u', c=(18.0, w), seg=12, chamfer=0.4)
    mb.box2((-44.0, 30.0, -10.0), (-26.0, 34.0, 10.0), PB, bevel=1.0)
    for s in (1, -1):
        hex_screw(mb, (20.0, 42.0, s * 18.5), 'w', 2.0, 0.6, MB_, sign=s)
    a.mesh(mb, 'Body')
    for name, u in (('Lens_Front', 30.0), ('Lens_Rear', -18.0)):
        m = MB()
        m.box2((u - 0.6, 30.0, -15.0), (u + 0.6, 52.0, 15.0), GL)
        a.mesh(m, name, origin=(u, H, 0.0), bevel_mm=0.0)
    ret = MB()
    ret.tube(26.8, 27.0, 2.2, 1.85, RR, c=(H, 0.0), seg=32)
    ret.cyl(26.8, 27.0, 0.35, RR, c=(H, 0.0), seg=10)
    a.mesh(ret, 'Reticle', origin=(26.9, H, 0.0), bevel_mm=0.0)
    a.socket('Sight_Point', (-18.0, H, 0.0))
    return a


def optic_prism4x():
    """ACOG TA31 style 4x prism sight on a flat-top mount; hollow along the sight axis."""
    H = 44.0
    a = Asset('Optic_Prism4x', 'attachment', 'Optic', magnification=4.0, sightHeight=H / 1000.0, compatible=RIFLES)
    mb = MB()
    pic_clamp(mb, -24.0, 24.0, MB_, top=6.0, nut=None)
    for u in (-12.0, 12.0):
        mb.cyl(-13.0, -15.0, 3.6, MB_, axis='w', c=(u, -2.8), seg=14)
        ridged_knob(mb, -15.0, -22.0, 6.5, MK, 'w', (u, -2.8), ridges=10, depth=0.6)
    mb.box2((-30.0, 5.5, -12.0), (30.0, 26.5, 12.0), MB_, bevel=1.2)
    # forged housing: rounded box with a round bore (equal point counts for the tube prism)
    outer = rrect(-19.0, H - 16.0, 19.0, H + 17.0, 8.0, 3)
    inner = circle(0.0, H, 11.5, len(outer), 200.0)
    mb.tube_prism(outer, inner, 'u', -44.0, 34.0, MB_)
    mb.lathe([(-76.0, 9.0), (-76.0, 13.6), (-74.0, 15.6), (-60.0, 15.6), (-48.0, 13.5), (-42.0, 13.5),
              (-42.0, 11.0), (-46.0, 9.0)], MB_, seg=40, c=(H, 0.0), closed=True)
    mb.lathe([(32.0, 11.5), (32.0, 16.0), (44.0, 20.0), (74.0, 20.0), (78.0, 20.6), (78.0, 18.6), (74.0, 17.8),
              (74.0, 16.6), (40.0, 13.0), (34.0, 11.5)], MB_, seg=40, c=(H, 0.0), closed=True)
    for (axis, a0, a1, c) in (('v', H + 16.0, H + 24.0, (-4.0, 0.0)), ('w', 18.0, 26.0, (-4.0, H))):
        mb.cyl(a0, a1, 8.0, MB_, axis=axis, c=c, seg=20, chamfer=0.8)
    mb.cyl(-36.0, 26.0, 3.2, PB, axis='u', c=(H + 18.5, 0.0), seg=14, chamfer=0.8)      # fibre optic tube
    for u in (-30.0, 20.0):
        mb.box2((u - 3.0, H + 15.0, -5.0), (u + 3.0, H + 21.5, 5.0), MB_, bevel=0.8)
    a.mesh(mb, 'Body')
    lenses(a, H, [('Lens_Front', 74.2, 16.8), ('Lens_Rear', -75.4, 9.6)])
    a.socket('Sight_Point', (-76.0, H, 0.0))
    return a


def optic_scope8x():
    """Long-range 4-16x50 style riflescope: 30 mm tube, 50 mm objective, exposed elevation/windage turrets,
    side-focus parallax knob, power ring with throw lever, two rings on a one-piece base. Hollow bore."""
    H = 38.0
    a = Asset('Optic_Scope8x', 'attachment', 'Optic', magnification=8.0, magnificationRange=[4.0, 16.0],
              sightHeight=H / 1000.0, compatible=['M700', 'M14EBR', 'M4A1', 'AK12'])
    mb = MB()
    pic_clamp(mb, -78.0, 70.0, MB_, top=8.0, nut=None)
    for u in (-52.0, 44.0):
        mb.prism(circle(u, -2.8, 5.0, 6, 30.0), 'w', 13.0, 17.5, MB_, bevel=0.4)
        mb.cyl(-13.0, -15.0, 4.0, MB_, axis='w', c=(u, -2.8), seg=14)
    for uc in (-62.0, 52.0):
        mb.box2((uc - 7.5, 7.5, -10.5), (uc + 7.5, H - 17.0, 10.5), MB_, bevel=1.0)
        mb.tube(uc - 7.5, uc + 7.5, 19.6, 15.05, MB_, c=(H, 0.0), seg=28, chamfer=0.6)
        for du in (-4.5, 4.5):
            for s in (1, -1):
                hex_screw(mb, (uc + du, H + 8.0, s * 18.6), 'w', 1.8, 0.6, MB_, sign=s)
    prof = [(-176.0, 18.2), (-176.0, 20.6), (-175.0, 21.8), (-160.0, 21.8), (-158.0, 20.6), (-150.0, 20.6),
            (-146.0, 21.8), (-124.0, 21.8), (-118.0, 18.6), (-100.0, 18.6), (-96.0, 15.0), (-30.0, 15.0),
            (-28.0, 18.8), (28.0, 18.8), (30.0, 15.0), (74.0, 15.0), (112.0, 30.0), (156.0, 30.0), (159.0, 31.0),
            (160.0, 30.0), (160.0, 28.4), (154.0, 27.4), (110.0, 27.4), (76.0, 12.6), (-96.0, 12.6),
            (-118.0, 16.4), (-172.0, 18.2)]
    mb.lathe(prof, MB_, seg=36, c=(H, 0.0), closed=True, exact=True)
    knurl_ring(mb, -116.0, -102.0, 19.2, 16.0, 20, MK, cy=H)                    # power ring
    mb.box2((-112.0, H + 18.0, -3.0), (-106.0, H + 26.0, 3.0), MB_, bevel=1.0)           # throw lever
    knurl_ring(mb, -158.0, -148.0, 22.2, 19.0, 20, RU, cy=H)                       # eyepiece grip
    # exposed elevation (top) + windage (right) turrets, side focus (left)
    mb.cyl(H + 17.0, H + 25.0, 13.0, MB_, axis='v', c=(0.0, 0.0), seg=32)
    ridged_knob(mb, H + 25.0, H + 37.0, 15.0, MK, 'v', (0.0, 0.0), ridges=16, depth=0.9)
    mb.cyl(17.0, 25.0, 12.0, MB_, axis='w', c=(0.0, H), seg=32)
    ridged_knob(mb, 25.0, 34.0, 13.5, MK, 'w', (0.0, H), ridges=14, depth=0.8)
    mb.cyl(-17.0, -24.0, 12.0, MB_, axis='w', c=(0.0, H), seg=32)
    mb.cyl(-24.0, -27.0, 21.0, MB_, axis='w', c=(0.0, H), seg=40, chamfer=1.0)          # side-focus wheel
    ridged_knob(mb, -27.0, -33.0, 9.0, MK, 'w', (0.0, H), ridges=14, depth=0.6)
    a.mesh(mb, 'Body')
    lenses(a, H, [('Lens_Front', 154.4, 27.6), ('Lens_Rear', -171.0, 18.4)])
    a.socket('Sight_Point', (-176.0, H, 0.0))
    return a


def optic_pistol_dot():
    """RMR style mini reflex sight; base contour matches the slide's top edges."""
    H = 15.25
    a = Asset('Optic_PistolDot', 'attachment', 'Optic', magnification=1.0, sightHeight=H / 1000.0,
              compatible=['Glock17'])
    mb = MB()
    base = [(-9.6, 0.0), (9.6, 0.0), (12.5, 3.0), (12.5, 9.0), (-12.5, 9.0), (-12.5, 3.0)]
    mb.prism(base, 'u', -22.5, 22.5, PB, bevel=1.0)
    ear = fillet([(-22.5, 8.5), (17.0, 8.5), (7.0, 24.5), (-11.0, 24.5), (-22.5, 14.0)], [0, 2, 3, 4, 4], 3)
    for s in (1, -1):
        mb.prism(ear, 'w', s * 10.0, s * 12.5, PB, bevel=0.8)
    mb.prism(fillet([(-11.0, 21.5), (8.6, 21.5), (7.0, 24.5), (-11.0, 24.5)], [0, 0, 1.5, 2], 2), 'w', -10.2, 10.2,
             PB, bevel=0.6)
    for u in (-18.5, 18.5):
        hex_screw(mb, (u, 9.0, 0.0), 'v', 2.2, 0.8, MB_)
    for s in (1, -1):
        mb.cyl(s * 12.5, s * 13.3, 2.0, RU, axis='w', c=(-8.0, 5.0), seg=12)
    a.mesh(mb, 'Body')
    lens = MB()
    lens.box2((2.4, 9.2, -10.0), (3.6, 21.4, 10.0), GL, xf=about(Rw(10.0), (3.0, H, 0.0)))
    a.mesh(lens, 'Lens', origin=(3.0, H, 0.0), bevel_mm=0.0)
    ret = MB()
    ret.cyl(1.0, 1.3, 1.0, RR, c=(H, 0.0), seg=16)
    a.mesh(ret, 'Reticle', origin=(1.15, H, 0.0), bevel_mm=0.0)
    a.socket('Sight_Point', (3.0, H, 0.0))
    return a


# ------------------------------------------------------------------------------ underbarrel
def _flip(mb, fn):
    with mb.group() as g:
        fn()
    mb.transform(g.verts, Ru(180.0))


def grip_vertical():
    a = Asset('Grip_Vertical', 'attachment', 'Underbarrel', compatible=RIFLES)
    mb = MB()
    _flip(mb, lambda: pic_clamp(mb, -20.0, 20.0, PB, top=6.0, nut='left'))
    prof = [(-5.5, 0.0), (-5.5, 16.2), (-10.0, 16.2), (-13.0, 15.2)]
    v = -13.0
    for k in range(5):
        prof += [(v - 6.0, 14.4), (v - 12.0, 15.4), (v - 18.0, 14.6)]
        v -= 18.0
    prof += [(-106.0, 15.0), (-106.0, 0.0)]
    mb.lathe(prof, 'Polymer_Ribs', seg=32, axis='v', c=(0.0, 0.0))
    mb.lathe([(-105.0, 0.0), (-105.0, 16.4), (-110.0, 16.4), (-113.0, 14.6), (-113.6, 0.0)], PB, seg=32, axis='v',
             c=(0.0, 0.0))
    a.mesh(mb, 'Body')
    a.grip_socket('Grip_L', (0.0, -5.5 - 0.4 * (113.6 - 5.5), 0.0), 0.0)
    return a


def grip_angled():
    a = Asset('Grip_Angled', 'attachment', 'Underbarrel', compatible=RIFLES)
    mb = MB()
    _flip(mb, lambda: pic_clamp(mb, -26.0, 26.0, PB, top=1.0, nut=None))
    prof = fillet([(52.0, 0.0), (52.0, -5.0), (44.0, -11.0), (0.0, -23.0), (-20.0, -38.0), (-33.0, -42.0),
                   (-44.0, -35.0), (-50.0, -18.0), (-52.0, 0.0)], [0, 2, 5, 14, 8, 6, 8, 8, 0], 8)
    mb.prism(prof, 'w', -13.5, 13.5, 'Polymer_Stipple', bevel=3.0, seg=3)
    for k in range(5):                                       # finger ridges along the lower edge
        u = 20.0 - k * 9.0
        mb.prism(fillet([(u - 2.0, -22.0 - k * 2.6), (u + 2.0, -21.0 - k * 2.6), (u + 1.0, -26.0 - k * 2.8),
                         (u - 3.0, -27.0 - k * 2.8)], 1.0, 2), 'w', -14.0, 14.0, 'Polymer_Stipple', bevel=0.6)
    a.mesh(mb, 'Body', bevel_mm=0.5)
    a.grip_socket('Grip_L', (-30.0, -20.0, 0.0), -29.0)
    return a


def bipod():
    a = Asset('Bipod', 'attachment', 'Underbarrel', compatible=['M700', 'M14EBR', 'M4A1', 'AK12'])
    mb = MB()
    _flip(mb, lambda: pic_clamp(mb, -16.0, 16.0, MB_, top=5.0, nut='left'))
    mb.box2((-14.0, -24.0, -22.0), (14.0, -4.5, 22.0), MB_, bevel=2.0)
    mb.cyl(-31.0, 31.0, 6.0, MB_, axis='w', c=(0.0, -17.0), seg=20, chamfer=0.8)
    for s in (1, -1):
        w = s * 27.5
        mb.box2((-9.0, -25.0, w - 5.5), (12.0, -9.0, w + 5.5), MB_, bevel=1.5)
        mb.cyl(-4.0, 166.0, 6.0, MB_, axis='u', c=(-17.0, w), seg=20, chamfer=0.6)
        mb.cyl(158.0, 170.0, 7.2, MB_, axis='u', c=(-17.0, w), seg=20, chamfer=1.0)
        mb.cyl(166.0, 238.0, 4.6, MW, axis='u', c=(-17.0, w), seg=16)
        for u in (180.0, 192.0, 204.0, 216.0):
            mb.tube(u, u + 1.2, 4.9, 4.0, MW, axis='u', c=(-17.0, w), seg=16)
        mb.lathe([(236.0, 0.0), (236.0, 6.0), (240.0, 8.2), (247.0, 7.6), (251.0, 4.6), (252.5, 0.0)], RU, seg=20,
                 axis='u', c=(-17.0, w))
        # tension spring (coil)
        pts = []
        for k in range(60):
            t = k / 59.0
            ang = t * 2 * math.pi * 9
            pts.append((-4.0 + 46.0 * t, -9.0 + 2.4 * math.sin(ang), s * 36.0 + 2.4 * math.cos(ang)))
        mb.sweep(pts, [(0.7 * math.cos(2 * math.pi * k / 6), 0.7 * math.sin(2 * math.pi * k / 6)) for k in range(6)],
                 MW, ref=(0.0, 1.0, 0.0))
        mb.box2((40.0, -12.0, s * 34.0 - 2.0), (46.0, -6.0, s * 34.0 + 2.0), MB_, bevel=0.6)
    a.mesh(mb, 'Body')
    return a


# ------------------------------------------------------------------------------ side rail
def laser_module():
    a = Asset('Laser_Module', 'attachment', 'Side', compatible=RIFLES)
    R = Ru(90.0)          # built upright (as if on a top rail), then turned to stick out to the right
    mb = MB()
    with mb.group() as g:
        pic_clamp(mb, -15.0, 15.0, MB_, top=4.0, nut='right')
        mb.box2((-42.0, 3.5, -20.0), (42.0, 33.0, 20.0), PT, bevel=3.0, seg=2)
        mb.box2((42.0, 7.0, -18.0), (45.0, 31.0, 18.0), PT, bevel=1.0)
        mb.cyl(45.0, 47.0, 7.2, PB, axis='u', c=(19.0, 8.0), seg=24)
        mb.cyl(45.0, 47.5, 4.6, PB, axis='u', c=(25.0, -9.0), seg=20)
        mb.cyl(-42.0, -45.0, 5.0, RU, axis='u', c=(18.0, 0.0), seg=16)
        for u in (-24.0, -10.0):
            ridged_knob(mb, 33.0, 36.0, 5.5, MK, 'v', (u, 10.0), ridges=10, depth=0.5)
        mb.box2((-38.0, 33.0, -16.0), (-28.0, 37.0, -6.0), RU, bevel=1.0)
        mb.cyl(46.6, 47.2, 5.8, GL, axis='u', c=(19.0, 8.0), seg=24)
        for (u, w) in ((-34.0, 14.0), (34.0, 14.0), (-34.0, -14.0), (34.0, -14.0)):
            hex_screw(mb, (u, 33.0, w), 'v', 1.8, 0.5, MB_)
    mb.transform(g.verts, R)
    a.mesh(mb, 'Body')
    ap = MB()
    with ap.group() as ga:
        ap.cyl(46.8, 47.6, 2.6, ELZ, axis='u', c=(25.0, -9.0), seg=16)
    ap.transform(ga.verts, R)
    em = R @ Vector((47.6, 25.0, -9.0))
    a.mesh(ap, 'LaserAperture', origin=tuple(em), bevel_mm=0.0)
    a.socket('Laser_Emitter', tuple(em))
    return a


def flashlight():
    a = Asset('Flashlight', 'attachment', 'Side', compatible=RIFLES)
    R = Ru(90.0)
    vc = 25.0
    mb = MB()
    with mb.group() as g:
        pic_clamp(mb, -15.0, 15.0, MB_, top=4.0, nut='left')
        mb.prism(fillet([(-15.0, 3.5), (15.0, 3.5), (12.0, vc), (-12.0, vc)], [1, 1, 2, 2], 2), 'w', -8.0, 8.0, MB_,
                 bevel=1.0)
        mb.tube(-14.0, 14.0, 15.6, 12.4, MB_, c=(vc, 0.0), seg=32, chamfer=0.8)
        prof = [(-80.0, 0.0), (-80.0, 9.0), (-78.0, 12.0), (-64.0, 12.0), (-62.0, 12.6)]
        for k in range(6):
            u = -56.0 + k * 9.0
            prof += [(u, 12.6), (u + 0.6, 12.0), (u + 2.4, 12.0), (u + 3.0, 12.6)]
        prof += [(18.0, 12.6), (26.0, 15.6), (58.0, 15.6), (60.0, 14.6), (60.0, 13.6), (57.0, 13.2), (57.0, 0.0)]
        mb.lathe(prof, MB_, seg=32, c=(vc, 0.0))
        mb.prism(star(0.0, vc, 13.1, 0.5, 24), 'u', -60.0, -30.0, MK, nobevel=True)
        mb.cyl(-80.0, -84.0, 6.0, RU, c=(vc, 0.0), seg=20, chamfer=1.0)
        for k in range(8):
            ang = k * 45.0 + 22.5
            mb.box2((30.0, vc + 15.0, -1.2), (52.0, vc + 16.8, 1.2), MB_, xf=about(Ru(ang), (0.0, vc, 0.0)))
    mb.transform(g.verts, R)
    a.mesh(mb, 'Body')
    lens = MB()
    with lens.group() as gl:
        lens.cyl(57.0, 57.6, 13.2, EL, c=(vc, 0.0), seg=32)
    lens.transform(gl.verts, R)
    em = R @ Vector((57.7, vc, 0.0))
    a.mesh(lens, 'Lens', origin=tuple(em), bevel_mm=0.0)
    a.socket('Light_Emitter', tuple(em))
    return a


def pistol_laser_light():
    a = Asset('Pistol_LaserLight', 'attachment', 'Underbarrel', compatible=['Glock17'])
    R = Ru(180.0)
    mb = MB()
    with mb.group() as g:
        pic_clamp(mb, -6.0, 6.0, PB, top=1.5, nut=None)
        body = fillet([(-20.0, 1.0), (40.0, 1.0), (44.0, 4.0), (44.0, 30.0), (40.0, 32.0), (-14.0, 32.0),
                       (-20.0, 25.0)], [0, 2, 2, 4, 3, 6, 4], 3)
        mb.prism(body, 'w', -14.5, 14.5, PB, bevel=2.0, seg=2)
        mb.tube(42.0, 47.0, 11.8, 9.6, MB_, c=(20.0, 0.0), seg=32, chamfer=0.6)
        mb.cyl(42.0, 46.5, 3.6, MB_, c=(7.5, 0.0), seg=16, chamfer=0.4)
        for s in (1, -1):
            mb.prism(fillet([(-22.0, 6.0), (-12.0, 6.0), (-12.0, 20.0), (-24.0, 18.0)], 1.5, 2), 'w',
                     s * 14.0, s * 16.0, 'Polymer_Ribs', bevel=0.5)
            hex_screw(mb, (30.0, 24.0, s * 14.5), 'w', 1.6, 0.4, MB_, sign=s)
    mb.transform(g.verts, R)
    a.mesh(mb, 'Body')
    lens = MB()
    with lens.group() as gl:
        lens.cyl(46.0, 46.6, 9.8, EL, c=(20.0, 0.0), seg=32)
    lens.transform(gl.verts, R)
    le = R @ Vector((46.7, 20.0, 0.0))
    a.mesh(lens, 'Lens', origin=tuple(le), bevel_mm=0.0)
    ap = MB()
    with ap.group() as ga:
        ap.cyl(46.0, 46.8, 2.2, ELZ, c=(7.5, 0.0), seg=16)
    ap.transform(ga.verts, R)
    la = R @ Vector((46.8, 7.5, 0.0))
    a.mesh(ap, 'LaserAperture', origin=tuple(la), bevel_mm=0.0)
    a.socket('Light_Emitter', tuple(le))
    a.socket('Laser_Emitter', tuple(la))
    return a


# ------------------------------------------------------------------------------ magazines
def mag_m4a1_ext():
    a = Asset('Mag_M4A1_Ext', 'attachment', 'Magazine', capacity=40, compatible=['M4A1'])
    mb = MB()
    box_mag(mb, (0.0, 0.0), top_len=65.0, bot_len=63.0, width=22.6, straight=78.0, R=300.0, arc_deg=30.0,
            mat=PB, floor_mat=PB, ribs=[(-0.82, -0.55, 0.30, 0.95, 0.7), (0.55, 0.82, 0.30, 0.95, 0.7)],
            cells=(10, 2, 1.6, 0.5, 0.08, 0.28), cart=CART_556, lip=1.5, n_arc=16)
    a.mesh(mb, 'Body')
    return a


def mag_ak12_ext():
    a = Asset('Mag_AK12_Ext', 'attachment', 'Magazine', capacity=45, compatible=['AK12'])
    mb = MB()
    box_mag(mb, (0.0, 0.0), top_len=70.0, bot_len=58.0, width=24.6, straight=22.0, R=255.0, arc_deg=53.0,
            mat=PB, floor_mat=PB, cells=(11, 4, 2.2, 0.9, 0.14, 0.92), front_lug=(9.0, 6.0, 4.0), cart=CART_545,
            lip=2.0, n_arc=20)
    a.mesh(mb, 'Body')
    return a


def mag_glock17_ext():
    a = Asset('Mag_Glock17_Ext', 'attachment', 'Magazine', capacity=33, compatible=['Glock17'])
    mb = MB()
    box_mag(mb, (0.0, 0.0), top_len=33.5, bot_len=35.0, width=22.4, straight=200.0, rake=12.0, mat=PB,
            floor_mat=PB, floor_h=8.0, cart=CART_9MM, catch_notch=False, lip=2.5,
            ribs=[(-0.5, 0.5, 0.45, 0.97, 0.4)], cells=(12, 1, 1.4, 0.4, 0.50, 0.95))
    a.mesh(mb, 'Body')
    return a


BUILDERS = [
    ('Muzzle_FlashHider', muzzle_flash_hider),
    ('Muzzle_Brake', muzzle_brake),
    ('Muzzle_PistolComp', muzzle_pistol_comp),
    ('Suppressor_Rifle', suppressor_rifle),
    ('Suppressor_Pistol', suppressor_pistol),
    ('Suppressor_Sniper', suppressor_sniper),
    ('Optic_RedDot', optic_reddot),
    ('Optic_Holo', optic_holo),
    ('Optic_Prism4x', optic_prism4x),
    ('Optic_Scope8x', optic_scope8x),
    ('Optic_PistolDot', optic_pistol_dot),
    ('Grip_Vertical', grip_vertical),
    ('Grip_Angled', grip_angled),
    ('Bipod', bipod),
    ('Laser_Module', laser_module),
    ('Flashlight', flashlight),
    ('Pistol_LaserLight', pistol_laser_light),
    ('Mag_M4A1_Ext', mag_m4a1_ext),
    ('Mag_AK12_Ext', mag_ak12_ext),
    ('Mag_Glock17_Ext', mag_glock17_ext),
]
