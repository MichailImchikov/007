"""
Remington M700, precision / tactical configuration, black (reference photos supplied by the user):
adjustable precision polymer stock (Archangel style): straight comb with an adjustable cheek riser and its
height knob, length-of-pull spacer section with a dial knob, thick rubber butt pad, near-vertical pistol
grip with palm swell (closed "thumbhole" frame), rear monopod hook under the butt, wide flat-bottom forend
with a front sling stud (+ short Picatinny adapter for the Bipod). Round M700 receiver with recoil lug and a
Picatinny scope base, bolt with a tactical knob on the right, detachable box magazine protruding slightly
below the stock, heavy free-floated plain barrel with a recessed crown (no muzzle device by default).
Default attachments: Optic_Scope8x (4-16x50 style) + Bipod.

Gun space (mm): u forward, v up, w right; origin = trigger blade front face centre. Overall ~1.06 m.
"""
import math

from .core import MB, Asset, fillet, rrect, T, clean_poly, annulus_sector, interp, circle
from .parts import (pic_rail, rail_frame, cartridge, CART_762, pin_heads, GripAxis, grip_loft, box_mag, strut,
                    ridged_knob, hex_screw, wire_loop)

ID = 'M700'
VB = 45.0
U_BF = 120.0
U_CROWN = U_BF + 610.0        # 730
U_BUTT = -330.0
MAG_SEAT = (68.0, 29.0)          # top round sits below the port's lower edge
BOLT_TRAVEL = 0.09
RAIL_TOP = 72.0
BIPOD_U0, BIPOD_U1 = 372.0, 404.0

MB_ = 'Metal_Black'
MD = 'Metal_Dark'
MW = 'Metal_Worn'
PB = 'Polymer_Black'

# stock loft tables (u -> value)
TOP = [(-300, 34.0), (-52, 34.0), (-44, 45.0), (140, 45.0), (152, 41.0), (440, 38.0)]
BOT = [(-300, -66.0), (-250, -66.0), (-172, -62.5), (-150, -59.5), (-140, -55.0), (-133, -46.0), (-128, -33.0),
       (-124, -18.0), (-120, -6.0), (-115, 1.5), (-106, 5.2), (-94, 6.0), (-56, 6.0), (-46, 4.0), (-40, -2.0),
       (128, -2.0), (140, 6.0), (430, 8.0), (440, 12.0)]
HWT = [(-300, 17.0), (-250, 17.0), (-60, 15.5), (-44, 20.0), (140, 22.0), (160, 25.0), (430, 24.0), (440, 21.0)]
HWB = [(-300, 20.0), (-250, 20.0), (-140, 17.5), (-110, 15.0), (-60, 14.0), (-44, 20.0), (140, 22.0), (160, 26.0), (430, 25.0), (440, 22.0)]


def barrel_r(u):
    return interp([(140.0, 15.2), (240.0, 12.4), (730.0, 12.2)], u)


def channel(u):
    if -80.0 <= u < -44.0:
        return 11.2, VB
    if -44.0 <= u <= 140.0:
        return 17.7, VB
    if u > 140.0:
        return barrel_r(u) + 2.5, VB
    return 0.0, 0.0


def stock_ring(u, n=56, p=4.0, scale=1.0):
    top, bot = interp(TOP, u), interp(BOT, u)
    hwt, hwb = interp(HWT, u) * scale, interp(HWB, u) * scale
    ch_r, ch_c = channel(u)
    cy, hh = (top + bot) / 2.0, (top - bot) / 2.0 * scale
    pts = []
    for k in range(n):
        th = math.pi / 2 + 2 * math.pi * k / n
        ct, st = math.cos(th), math.sin(th)
        y = cy + hh * math.copysign(abs(st) ** (2.0 / p), st)
        f = (y - bot) / max(top - bot, 1e-6)
        hw = hwb + (hwt - hwb) * f
        x = hw * math.copysign(abs(ct) ** (2.0 / p), ct)
        if ch_r > 0 and st > 0 and abs(x) < ch_r:
            y = min(y, ch_c - math.sqrt(ch_r * ch_r - x * x))
        pts.append((u, y, x))
    return pts


def stock(mb):
    stations = ([-300, -294, -286, -276, -266, -256, -246] + list(range(-234, -160, 12)) +
                [-160, -153, -147, -142] + list(range(-139, -101, 3)) + [-100, -96, -90, -82, -74, -66] +
                [-62, -58, -54, -50, -47, -45.5, -44, -42, -38, -32, -24, -15] + list(range(-4, 130, 12)) +
                [128, 132, 136, 140, 144, 148, 152, 160] + list(range(172, 425, 14)) + [425, 430, 434, 437, 440])
    mb.loft([stock_ring(u) for u in stations], PB)
    # vertical pistol grip with palm swell (closed thumbhole frame with the lower spar)
    gax = GripAxis(top=(-50.0, 16.0), rake=12.0, length=100.0)

    def hw(t):
        return 14.5 + 1.2 * math.sin(math.pi * t) - 0.5 * t

    def hf(t):
        return 18.0 + 1.6 * math.exp(-((t - 0.35) / 0.1) ** 2) - 0.8 * t

    def hb(t):
        return 23.0 + 3.0 * math.exp(-((t - 0.45) / 0.18) ** 2) - 1.5 * t

    grip_loft(mb, gax, 'Polymer_Stipple', hw, hf, hb, steps=30, n=32, p=2.8)
    # monopod hook under the butt
    hook = fillet([(-262, -64), (-292, -64), (-292, -78), (-285, -86), (-262, -86), (-256, -80), (-264, -78),
                   (-280, -78), (-282, -70), (-262, -70)], [2, 2, 3, 4, 4, 3, 2, 2, 2, 2], 2)
    mb.prism(hook, 'w', -9.0, 9.0, PB, bevel=1.2)
    mb.cyl(-9.5, 9.5, 2.5, MB_, axis='w', c=(-286.0, -72.0), seg=10)
    # length-of-pull spacer section with dial knob, thick rubber pad
    mb.prism(fillet([(-300, 36), (-300, -64), (-314, -64), (-314, 36)], 3.0, 2), 'w', -18.0, 18.0, PB, bevel=1.5)
    for u in (-303.5, -309.0):
        mb.box2((u - 0.5, -62.0, -18.3), (u + 0.5, 34.0, 18.3), 'Rubber')
    ridged_knob(mb, -18.0, -25.0, 10.0, MB_, 'w', (-307.0, -14.0), ridges=16, depth=0.6)
    pad = fillet([(-314, 38), (-314, -66), (U_BUTT + 2, -64), (U_BUTT, -58), (U_BUTT, 32), (U_BUTT + 2, 37)],
                 [2, 2, 2, 5, 5, 2], 2)
    mb.prism(pad, 'w', -20.0, 20.0, 'Rubber_Ribs', bevel=2.5, seg=2)
    # adjustable cheek riser on two posts + height knob
    riser = fillet([(-138, 37.5), (-284, 37.5), (-285, 50), (-278, 57), (-164, 57), (-142, 47)],
                   [2, 3, 5, 6, 8, 5], 3)
    mb.prism(riser, 'w', -16.0, 16.0, PB, bevel=2.5, seg=2)
    for u in (-166.0, -262.0):
        mb.cyl(30.0, 39.0, 4.2, MW, axis='v', c=(u, 0.0), seg=16)
    ridged_knob(mb, -16.5, -24.0, 8.0, MB_, 'w', (-214.0, 22.0), ridges=14, depth=0.6)
    # integrated trigger guard, magwell rim (bottom metal) and magazine release paddle
    path = fillet([(22.0, -2.0), (19.0, -14.0), (10.0, -21.0), (-14.0, -21.0), (-26.0, -12.0), (-30.0, 2.0),
                   (-31.0, 8.0)], [0, 4, 5, 6, 5, 3, 0], 3, closed=False)
    mb.sweep([(u, v, 0.0) for u, v in path], rrect(-6.0, -2.4, 6.0, 2.4, 1.4, 2), PB)
    rim_o = rrect(20.0, -17.5, 116.0, 17.5, 4.0, 3)
    rim_i = rrect(23.5, -15.5, 112.5, 15.5, 2.5, 3)
    mb.tube_prism(rim_o, rim_i, 'v', -5.5, -1.0, MB_)
    mb.prism(fillet([(13.0, -3.0), (21.0, -3.0), (21.0, -12.0), (15.0, -14.0)], 1.0, 2), 'w', -6.0, 6.0, MB_, bevel=0.5)
    # front sling stud + Picatinny adapter for the bipod; rear swivel cup
    b = interp(BOT, (BIPOD_U0 + BIPOD_U1) / 2.0)
    mb.box2((BIPOD_U0 - 2.0, b - 1.0, -10.5), (BIPOD_U1 + 2.0, b + 3.0, 10.5), MB_, bevel=0.8)
    pic_rail(mb, BIPOD_U0, BIPOD_U1, MB_, frame=rail_frame('bottom', b - 1.0), H=6.0)
    for u in (BIPOD_U0 + 6.0, BIPOD_U1 - 6.0):
        hex_screw(mb, (u, b - 7.0, 0.0), 'v', 2.6, 0.8, MB_, sign=-1)
    bs = interp(BOT, 418.0)
    mb.cyl(bs - 7.0, bs + 2.0, 3.6, MB_, axis='v', c=(418.0, 0.0), seg=14, chamfer=0.6)
    wire_loop(mb, (418.0, bs - 10.5, 0.0), 4.5, 1.3, MW, plane='uv', seg=16)
    for s in (1, -1):
        mb.cyl(s * 20.0, s * 21.2, 7.0, MB_, axis='w', c=(-240.0, -40.0), seg=20, chamfer=0.4)
    return gax, hf, hb


def receiver(mb):
    c = (VB, 0.0)
    ro, ri = 17.25, 9.2
    mb.prism(annulus_sector(0.0, VB, ri, ro, 45.0, 315.0, 18), 'u', -40.0, -28.0, MB_, bevel=0.6)
    mb.tube(-28.0, 25.0, ro, ri, MB_, axis='u', c=c, seg=36)
    mb.prism(annulus_sector(0.0, VB, ri, ro, 62.0, 340.0, 18), 'u', 25.0, 108.0, MB_, bevel=0.6)
    mb.tube(108.0, 140.0, ro, ri, MB_, axis='u', c=c, seg=36, chamfer=1.0)
    # recoil lug (visible sliver between receiver and barrel), rear tang, safety, bolt release
    mb.prism(rrect(-19.0, 22.0, 19.0, VB + 13.0, 3.0, 2), 'u', 140.0, 145.0, MB_, bevel=0.5)
    mb.box2((-58.0, 37.0, -7.5), (-40.0, 41.5, 7.5), MB_, bevel=1.0)
    mb.prism(fillet([(-56.0, 36.0), (-46.0, 36.0), (-47.0, 41.0), (-55.0, 41.0)], 1.0, 2), 'w', 12.0, 18.0, MB_,
             bevel=0.4)
    mb.box2((-36.0, 26.0, -4.0), (-26.0, 30.0, 4.0), MB_, bevel=0.6)
    # one-piece scope base with screws
    mb.prism(rrect(-8.6, 59.5, 8.6, 65.5, 1.0), 'u', -36.0, 136.0, MB_, bevel=0.8)
    pic_rail(mb, -34.0, 134.0, MB_, frame=T(0, 65.0, 0), H=RAIL_TOP - 65.0)
    for u in (-24.0, 4.0, 112.0, 126.0):
        mb.cyl(64.5, 65.6, 2.6, MB_, axis='v', c=(u, -9.6), seg=10)
    # heavy free-floated barrel, recessed target crown
    prof = [(138.0, 0.0), (138.0, 15.2), (240.0, 12.4), (U_CROWN - 2.0, 12.2), (U_CROWN, 11.0), (U_CROWN, 6.0),
            (U_CROWN - 2.5, 4.2), (U_CROWN - 12.0, 3.9), (U_CROWN - 12.0, 0.0)]
    mb.lathe(prof, MD, seg=36, axis='u', c=c)


def bolt(mb):
    c = (VB, 0.0)
    mb.cyl(-34.0, U_BF, 8.9, MW, axis='u', c=c, seg=24, chamfer=0.6)
    for s in (1, -1):                                                     # locking lugs
        mb.box2((U_BF - 14.0, VB - 3.5, s * 7.0), (U_BF - 1.0, VB + 3.5, s * 10.8), MW, bevel=0.5)
    mb.box2((U_BF - 30.0, VB - 1.2, 8.0), (U_BF - 2.0, VB + 1.2, 9.6), MW, bevel=0.3)       # extractor
    mb.lathe([(-36.0, 0), (-36.0, 10.6), (-45.0, 10.6), (-62.0, 9.2), (-68.0, 7.6), (-72.0, 5.0), (-72.0, 0)],
             MB_, seg=28, axis='u', c=c)
    mb.cyl(-72.0, -79.0, 4.2, MB_, axis='u', c=(VB - 2.5, 0.0), seg=14, chamfer=0.8)
    mb.cyl(-33.0, -20.0, 10.6, MW, axis='u', c=c, seg=28, chamfer=0.8)
    path = [(-26.5, VB, 7.0), (-28.0, VB - 2.5, 18.0), (-30.5, VB - 8.0, 28.0), (-33.5, VB - 15.0, 37.0),
            (-36.0, VB - 21.5, 44.0)]
    sec = [(4.2 * math.cos(2 * math.pi * k / 12), 4.2 * math.sin(2 * math.pi * k / 12)) for k in range(12)]
    mb.sweep(path, sec, MB_, ref=(1.0, 0.0, 0.0))
    # tactical knob (axis along w): tapered with grip grooves
    kc = (-37.0, VB - 25.0)
    prof = [(40.0, 0), (40.0, 5.0), (42.0, 8.0)]
    for k in range(4):
        w = 44.0 + k * 4.0
        prof += [(w, 9.0 + k * 0.6), (w + 2.6, 9.0 + k * 0.6), (w + 3.0, 8.4 + k * 0.6), (w + 4.0, 9.0 + k * 0.6)]
    prof += [(61.0, 11.4), (63.0, 10.6), (64.0, 7.0), (64.4, 0)]
    mb.lathe(prof, MB_, seg=24, axis='w', c=kc)


def trigger(mb):
    cu, cv = 24.0, -3.0
    R0 = math.hypot(cu, cv)
    R1 = R0 + 4.2
    front = [(cu - math.sqrt(R0 * R0 - (v - cv) ** 2), v) for v in [7.5 - 21.0 * i / 10 for i in range(11)]]
    back = [(cu - math.sqrt(R1 * R1 - (v - cv) ** 2), v) for v in [-12.0 + 19.0 * i / 10 for i in range(11)]]
    poly = front + [(front[-1][0] + 2.3, -15.6)] + back + [(-8.0, 7.5), (-8.0, 22.0), (2.0, 22.0)]
    mb.prism(clean_poly(poly), 'w', -3.0, 3.0, MW, bevel=0.6)


def magazine(mb):
    """Detachable box magazine (AICS style), protrudes ~19 mm below the stock."""
    return box_mag(mb, MAG_SEAT, top_len=86.0, bot_len=86.0, width=30.0, straight=48.0, mat='Metal_Dark',
                   floor_mat=PB, floor_h=8.0, ribs=[(-0.7, -0.5, 0.25, 0.9, 0.7), (0.5, 0.7, 0.25, 0.9, 0.7)],
                   cart=CART_762, lip=2.0, catch_notch=False)


def build():
    a = Asset(ID, 'weapon', caliber='7.62x51mm (.308 Win)', capacity=5, bevel_seg=2)
    body = MB()
    gax, hf, hb = stock(body)
    receiver(body)
    a.mesh(body, 'Body')

    b = MB()
    bolt(b)
    a.mesh(b, 'Bolt', origin=(-26.5, VB, 0.0))

    trig = MB()
    trigger(trig)
    a.mesh(trig, 'Trigger', origin=(-3.0, 20.0, 0.0))

    mag = MB()
    magazine(mag)
    a.mesh(mag, 'Mag', origin=(MAG_SEAT[0], MAG_SEAT[1], 0.0))

    a.socket('Socket_Muzzle', (U_CROWN, VB, 0.0))
    a.socket('Socket_Optic', (50.0, RAIL_TOP, 0.0))
    b0 = interp(BOT, (BIPOD_U0 + BIPOD_U1) / 2.0)
    a.socket('Socket_Underbarrel', ((BIPOD_U0 + BIPOD_U1) / 2.0, b0 - 7.0, 0.0))
    a.socket('Socket_Mag', (MAG_SEAT[0], MAG_SEAT[1], 0.0))
    t = 0.08 + 0.45 * 0.92
    gp = gax.point(t, front=(hf(t) - hb(t)) / 2.0)
    a.grip_socket('Grip_R', (gp.x, gp.y, 0.0), gax.rake)
    ug = 300.0
    a.socket('Grip_L', (ug, (interp(TOP, ug) + interp(BOT, ug)) / 2.0, 0.0))
    a.socket('Eject', (66.0, 53.0, 16.0))

    a.meta['moving'] = {'Bolt': {'travel': BOLT_TRAVEL, 'rotation_deg': 90.0,
                                 'note': 'lift handle 90 deg about the bore (local Z), then pull straight back'}}
    a.meta['mag_insert'] = [0.0, 1.0, 0.0]
    a.meta['defaultAttachments'] = {'Socket_Optic': 'Optic_Scope8x', 'Socket_Underbarrel': 'Bipod'}
    return a
