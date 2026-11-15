"""
Glock 17 (9x19), Gen-3 style, two-tone (reference photos supplied by the user + measured Gen-4 side view):
matte steel-grey nitrided slide (boxy, flat sides, slightly rounded top edges, ~10 rear cocking serrations,
ejection port with square barrel hood, extractor on the right, bevelled nose, U-notch rear sight with
white outline, white-dot front sight); black polymer frame with 3 finger grooves, thumb-rest dimples,
stippled grip panels (normal map), horizontal ribs on front/back straps, small beavertail, single-slot
accessory rail with side grooves, squarish trigger guard with flat front face, thin slide stop, serrated
takedown lever, square magazine release, frame pins, trigger with safety blade; plain barrel whose crown
sits slightly recessed in the slide nose (no threads on the base pistol).

Gun space (mm): u forward, v up, w right; origin = trigger blade front face centre.
"""
import math

from mathutils import Vector, Matrix

from .core import MB, Asset, fillet, rrect, T, Rw, about, clean_poly, circle
from .parts import box_mag, CART_9MM, pin_heads, grip_slices

ID = 'Glock17'
SB, ST = 28.5, 51.5           # slide bottom / top
VB = 40.5                     # bore axis
SL0, SL1 = -82.0, 104.0       # slide rear / front
U_MUZ = SL1                   # Socket_Muzzle: slide nose face (barrel crown 0.6 mm recessed)
U_CROWN = SL1 - 0.6
FB = 14.0                     # frame bottom above the trigger (top of the trigger guard opening)
DC = 15.0                     # dust cover bottom = rail surface
MAG_SEAT = (-40.0, 25.0)
MAG_RAKE = 12.0
SLIDE_TRAVEL = 0.05
SIGHT_TOP = ST + 4.0

MB_ = 'Metal_Black'
MD = 'Metal_Dark'
MW = 'Metal_Worn'
MN = 'Metal_Nitride'
PB = 'Polymer_Black'
WHITE = 'Paint_White'


def front_u(v):
    return -15.0 + (v - 8.0) * (22.0 / 80.0)


def back_u(v):
    return -80.0 + (v - 14.0) * (17.0 / 86.0)


def slide(mb):
    sec = fillet([(-12.75, SB), (12.75, SB), (12.75, ST), (-12.75, ST)], [0.6, 0.6, 2.6, 2.6], 6)
    s = MB()
    s.prism(sec, 'u', SL0, SL1, MN)
    cut = MB()
    # ~10 rear cocking serrations on both sides
    for k in range(10):
        u = -76.0 + k * 3.7
        for sw in (1, -1):
            cut.box2((u, SB - 1.0, sw * 11.9), (u + 1.7, ST - 3.4, sw * 15.0), MN)
    # ejection port (top right) and bevelled nose, muzzle + guide-rod holes, underside channel
    cut.box2((-22.0, VB - 2.0, -4.5), (24.0, ST + 2.0, 16.0), MN)
    cut.prism([(92.0, SB - 2.0), (SL1 + 2.0, SB - 2.0), (SL1 + 2.0, SB + 7.5)], 'w', -16.0, 16.0, MN)
    cut.cyl(SL1 - 8.0, SL1 + 2.0, 7.3, MN, axis='u', c=(VB, 0.0), seg=24)
    cut.cyl(SL1 - 6.0, SL1 + 2.0, 3.1, MN, axis='u', c=(SB + 4.5, 0.0), seg=16)
    cut.box2((SL0 + 6.0, SB - 2.0, -9.0), (SL1 - 10.0, SB + 6.0, 9.0), MN)
    s.boolean(cut, mat=MN)
    mb.merge(s)
    # rear cover plate, extractor
    mb.box2((SL0 - 0.8, SB + 4.0, -8.0), (SL0 + 0.2, ST - 4.0, 8.0), MN, bevel=0.3)
    mb.box2((-26.0, VB + 1.5, 12.3), (-6.0, VB + 5.0, 13.4), MN, bevel=0.3)
    # U-notch rear sight with white outline, front sight with white dot
    t = SIGHT_TOP
    rear = [(-7.5, ST - 0.5), (7.5, ST - 0.5), (7.5, t), (1.8, t), (1.8, t - 3.1), (-1.8, t - 3.1), (-1.8, t),
            (-7.5, t)]
    mb.prism(rear, 'u', -80.5, -71.0, MB_, bevel=0.3)
    outline = [(-3.0, t - 0.2), (-1.9, t - 0.2), (-1.9, t - 3.2), (1.9, t - 3.2), (1.9, t - 0.2), (3.0, t - 0.2),
               (3.0, t - 4.3), (-3.0, t - 4.3)]
    mb.prism(outline, 'u', -80.75, -80.45, WHITE)
    mb.box2((93.5, ST - 0.5, -1.75), (99.5, t, 1.75), MB_, bevel=0.3)
    mb.cyl(93.25, 93.55, 1.05, WHITE, axis='u', c=(t - 1.8, 0.0), seg=14)


def frame(mb):
    main = [(-86.0, 17.5), (-80.0, SB), (100.0, SB), (101.5, 22.0), (99.0, DC), (40.0, DC), (34.0, FB),
            (-13.0, FB), (-19.0, 10.0), (-74.0, 10.0), (-86.0, 13.0)]
    fr = MB()
    fr.prism(fillet(main, [2, 2, 0, 2, 2, 2, 1, 1, 2, 0, 3], 2), 'w', -12.4, 12.4, PB)
    cut = MB()
    # accessory rail: side grooves + one cross slot
    for sw in (1, -1):
        cut.box2((50.0, DC + 1.5, sw * 10.8), (102.0, DC + 4.5, sw * 15.0), PB)
    cut.box2((74.0, DC - 2.0, -15.0), (79.0, DC + 2.5, 15.0), PB)
    fr.boolean(cut, mat=PB)
    mb.merge(fr)
    # squarish trigger guard with flat front face
    path = fillet([(36.0, DC), (37.0, -4.0), (35.5, -12.5), (30.0, -15.5), (-6.0, -15.5), (-12.5, -10.0),
                   (-14.5, -1.0), (-14.0, FB)], [0, 1.5, 3, 3, 6, 4, 2, 0], 3, closed=False)
    mb.sweep([(u, v, 0.0) for u, v in path], rrect(-5.0, -2.2, 5.0, 2.2, 1.2, 4), PB)
    # grip: horizontal slices, 3 finger grooves, thumb-rest dimples, texture zones
    def bump(t, c, wd=0.055):
        return math.exp(-((t - c) / wd) ** 2)

    g = MB()
    grip_slices(g, PB, 12.0, -72.0, front_u, back_u, lambda v: 14.8 - 0.25 * (12.0 - v) / 84.0,
                steps=64, n=40, p=3.2, front_bump=lambda t: 2.2 * (bump(t, 0.40) + bump(t, 0.62) + bump(t, 0.84)))
    # subtle thumb-rest dimples: shallow (0.4 mm) elongated spherical caps, no hard rim
    dimples = MB()
    dc_u, dc_v, ra, dep = -40.0, 5.0, 6.0, 0.4
    rs = (ra * ra + dep * dep) / (2.0 * dep)
    for sw in (1, -1):
        ws = 14.7
        prof = [(ws - dep, 0.0)]
        for k in range(1, 11):
            r = (ra + 1.0) * k / 10.0
            prof.append((ws - dep + rs - math.sqrt(rs * rs - r * r), r))
        prof += [(ws + 3.0, ra + 1.0), (ws + 3.0, 0.0)]
        with dimples.group() as dg:
            dimples.lathe([(sw * a, r) for a, r in prof], PB, seg=32, axis='w', c=(dc_u, dc_v), exact=True)
        dimples.transform(dg.verts, about(Matrix.Diagonal((1.45, 1.0, 1.0, 1.0)), (dc_u, dc_v, 0.0)))
    g.boolean(dimples, mat=PB)
    stip = g.mi('Polymer_Stipple')
    ribs = g.mi('Polymer_Ribs')
    for f in g.bm.faces:
        c = f.calc_center_median()
        n = f.normal
        if c.y > 0.0 or c.y < -68.0:
            continue
        if abs(n.z) > 0.75 and back_u(c.y) + 9.0 < c.x < front_u(c.y) - 7.0 and c.y < -6.0:
            f.material_index = stip
        elif abs(n.x) > 0.55:
            f.material_index = ribs
    mb.merge(g)
    # controls: slide stop (left), serrated takedown lever (both sides), square mag release (left)
    mb.prism(fillet([(-36.0, 24.0), (-22.0, 24.0), (-20.0, 27.6), (-36.0, 27.6)], 0.8, 2), 'w', -12.4, -13.6, MB_,
             bevel=0.3)
    for sw in (1, -1):
        mb.box2((14.5, 18.0, sw * 12.2), (19.5, 27.0, sw * 13.4), MB_, bevel=0.3)
        for k in range(4):
            mb.box2((14.8 + k * 1.3, 18.5, sw * 13.3), (15.4 + k * 1.3, 26.5, sw * 13.7), MB_)
    mb.box2((-30.0, -9.0, -15.8), (-21.0, -1.5, -13.0), PB, bevel=1.0)
    pin_heads(mb, 0.0, 26.0, 12.4, 1.6, MB_, h=0.5, seg=12)
    pin_heads(mb, 1.5, 19.0, 12.4, 1.6, MB_, h=0.5, seg=12)
    pin_heads(mb, 17.0, 24.0, 12.4, 1.6, MB_, h=0.5, seg=12)
    pin_heads(mb, -62.0, 4.0, 14.6, 1.6, MB_, h=0.5, seg=12)


def barrel(mb):
    c = U_CROWN
    prof = [(30.0, 0.0), (30.0, 6.9), (c - 0.6, 6.9), (c, 6.3), (c, 5.0), (c - 0.8, 4.5), (c - 10.0, 4.5),
            (c - 10.0, 0.0)]
    mb.lathe(prof, MD, seg=32, axis='u', c=(VB, 0.0))
    mb.box2((-20.0, VB - 1.6, -4.4), (23.4, ST - 0.3, 12.6), MW, bevel=0.6)                  # square hood
    mb.box2((-21.0, VB - 5.0, -4.0), (-14.0, VB + 2.0, 4.0), MD, bevel=0.6)                 # chamber face
    mb.cyl(SL1 - 2.0, SL1 - 0.5, 2.6, MD, axis='u', c=(SB + 4.5, 0.0), seg=12)              # guide rod tip


def trigger(mb):
    cu, cv = 22.0, -1.0
    R0 = math.hypot(cu, cv)
    R1 = R0 + 4.6
    front = [(cu - math.sqrt(R0 * R0 - (v - cv) ** 2), v) for v in [FB + 0.5 - 24.0 * i / 10 for i in range(11)]]
    back = [(cu - math.sqrt(R1 * R1 - (v - cv) ** 2), v) for v in [-7.5 + 22.0 * i / 10 for i in range(11)]]
    poly = front + [(front[-1][0] + 2.0, -10.8)] + back + [(-8.0, FB + 2.0), (-8.0, 21.0), (2.0, 21.0)]
    mb.prism(clean_poly(poly), 'w', -3.0, 3.0, PB, bevel=0.6)
    blade = [(cu - math.sqrt(R0 * R0 - (v - cv) ** 2) - 1.3, v) for v in [5.0 - 11.0 * i / 6 for i in range(7)]]
    blade += [(cu - math.sqrt(R0 * R0 - (v - cv) ** 2) + 1.0, v) for v in [-6.0 + 11.0 * i / 6 for i in range(7)]]
    mb.prism(clean_poly(blade), 'w', -0.8, 0.8, PB, bevel=0.2)


def magazine(mb):
    a = math.radians(MAG_RAKE)
    h = 33.5 / 2.0
    s = (MAG_SEAT[1] + 72.0 - h * math.sin(a)) / math.cos(a)
    return box_mag(mb, MAG_SEAT, top_len=33.5, bot_len=33.5, width=22.4, straight=s + 7.0, rake=MAG_RAKE,
                   mat=PB, floor_mat=PB, floor_h=7.0, cart=CART_9MM, catch_notch=False, lip=2.5,
                   ribs=[(-0.5, 0.5, 0.45, 0.95, 0.4)])


def build():
    a = Asset(ID, 'weapon', caliber='9x19mm Parabellum', capacity=17, bevel_mm=0.45, bevel_seg=2)
    body = MB()
    frame(body)
    barrel(body)
    a.mesh(body, 'Body')

    s = MB()
    slide(s)
    a.mesh(s, 'Slide', origin=(SL0, VB, 0.0))

    trig = MB()
    trigger(trig)
    a.mesh(trig, 'Trigger', origin=(-3.0, 17.0, 0.0))

    mag = MB()
    magazine(mag)
    a.mesh(mag, 'Mag', origin=(MAG_SEAT[0], MAG_SEAT[1], 0.0))

    a.socket('Socket_Muzzle', (U_MUZ, VB, 0.0))
    a.socket('Socket_Optic', (-48.0, ST, 0.0))
    a.socket('Socket_Underbarrel', (76.0, DC, 0.0))
    a.socket('Socket_Mag', (MAG_SEAT[0], MAG_SEAT[1], 0.0))
    vg = 12.0 - 0.45 * 84.0
    uc = (front_u(vg) + back_u(vg)) / 2.0
    rake = math.degrees(math.atan2((front_u(12.0) + back_u(12.0)) / 2.0 - (front_u(-72.0) + back_u(-72.0)) / 2.0, 84.0))
    a.grip_socket('Grip_R', (uc, vg, 0.0), rake)
    a.grip_socket('Grip_L', (uc, vg, -15.0), rake)
    a.socket('Sight_Iron', (-75.5, SIGHT_TOP, 0.0))
    a.socket('Eject', (2.0, (VB + ST) / 2.0, 12.75))

    a.meta['moving'] = {'Slide': {'travel': SLIDE_TRAVEL, 'note': 'slide straight back; Socket_Optic rides on the slide'}}
    a.meta['socket_parent_hint'] = {'Socket_Optic': 'Slide'}
    rk = math.radians(MAG_RAKE)
    a.meta['mag_insert'] = [0.0, round(math.cos(rk), 5), round(math.sin(rk), 5)]
    return a
