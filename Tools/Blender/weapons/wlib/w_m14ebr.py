"""
M14 EBR (Sage EBR chassis style, 7.62x51), matte black (reference photos: Mk 14 Mod 0 / M14 EBR-RI):
angular aluminium chassis with one long continuous top rail from the receiver over the forend, forend
with rectangular lightening cutouts, short 3/9 rails and a bottom rail; M14 receiver visible with the
reciprocating operating rod + handle on the RIGHT; M14 rear aperture sight with windage/elevation knobs
at the receiver heel; thin barrel with the M14 gas cylinder, gas lock and front band underneath; front
sight post with ears and bayonet lug right behind a long cylindrical brake with drilled holes (brake in
Body, Socket_Muzzle at its tip); big M14 trigger guard with the safety at its front; A2-style grip;
telescoping stock on a tube with skeleton side plates (round holes), adjustable cheek riser and pad;
straight 20-rd steel magazine.

Gun space (mm): u forward, v up, w right; origin = trigger blade front face centre. Overall 0.89 m.
"""
import math

from mathutils import Vector

from .core import MB, Asset, fillet, rrect, T, Rw, about, clean_poly, annulus_sector, circle
from .parts import (pic_rail, rail_frame, GripAxis, grip_loft, box_mag, CART_762, pin_heads, star, strut,
                    hex_screw, ridged_knob, wire_loop)

ID = 'M14EBR'
VB = 52.0
RAIL_TOP = 94.0
SIGHT = 100.0
U_BF = 90.0
U_CROWN = U_BF + 457.0        # 547
FS0, FS1 = 514.0, 536.0        # front sight base
BRK0, U_TIP = 537.0, 617.0     # drilled muzzle brake
CH0, CH1 = -75.0, 140.0        # rear chassis
FE0, FE1 = 140.0, 400.0        # forend
FE_TOP, FE_BOT, FE_HW = 87.0, 22.0, 26.0
MAG_SEAT = (86.0, 36.0)
U_BUTT = -272.0
BOLT_TRAVEL = 0.10

MB_ = 'Metal_Black'
MD = 'Metal_Dark'
MW = 'Metal_Worn'
PB = 'Polymer_Black'
RU = 'Rubber'


def oct_poly(top, bot, hw, ch):
    return [(-hw + ch, bot), (hw - ch, bot), (hw, bot + ch), (hw, top - ch), (hw - ch, top),
            (-hw + ch, top), (-hw, top - ch), (-hw, bot + ch)]


def chassis(mb):
    walls = MB()
    lwall = fillet([(CH0, 22), (CH1, 22), (CH1, 66), (CH0 + 10, 66), (CH0, 56)], [3, 0, 0, 3, 3], 2)
    walls.prism(lwall, 'w', -25.0, -18.0, MB_)
    rwall = [(CH0, 22), (CH1, 22), (CH1, 66), (126, 66), (126, 58.5), (0, 58.5), (0, 66), (CH0 + 10, 66), (CH0, 56)]
    walls.prism(fillet(rwall, [3, 0, 0, 1, 1, 1, 1, 3, 3], 2), 'w', 18.0, 25.0, MB_)
    cut = MB()
    for (u0, u1, v0, v1) in ((-50.0, -8.0, 30.0, 52.0), (2.0, 38.0, 30.0, 50.0), (104.0, 132.0, 30.0, 52.0)):
        for s in (1, -1):
            cut.prism(rrect(u0, v0, u1, v1, 5.0, 3), 'w', s * 23.2, s * 30.0, MB_)
    walls.boolean(cut, mat=MB_)
    mb.merge(walls)
    hin = 18.0
    mb.box2((CH0 + 2, 44.0, -hin), (CH1 - 2, 58.0, hin), MB_)
    mb.box2((CH0 + 2, 22.5, -hin), (42.0, 44.0, hin), MB_)
    mb.box2((131.0, 22.5, -hin), (CH1 - 2, 44.0, hin), MB_)
    mb.box2((CH0, 19.0, -25.0), (42.0, 23.0, 25.0), MB_, bevel=0.8)
    mb.box2((131.0, 19.0, -25.0), (CH1, 23.0, 25.0), MB_, bevel=0.8)
    for (u, v) in ((-60.0, 60.0), (-60.0, 28.0), (128.0, 60.0), (128.0, 28.0), (60.0, 28.0)):
        for s in (1, -1):
            hex_screw(mb, (u, v, s * 25.0), 'w', 2.6, 1.0, MB_, sign=s)
    # stock adapter + receiver extension tube
    mb.prism(fillet([(CH0, 24), (CH0, 70), (CH0 - 14, 70), (CH0 - 16, 30)], [0, 0, 3, 3], 2), 'w', -20.0, 20.0, MB_,
             bevel=1.0)
    mb.lathe([(CH0 - 14.0, 0), (CH0 - 14.0, 14.6), (-214.0, 14.6), (-216.0, 13.5), (-216.0, 0)], MB_, seg=28,
             axis='u', c=(VB + 4.0, 0.0))
    mb.prism(star(0.0, VB + 4.0, 17.0, 1.6, 8), 'u', CH0 - 16.0, CH0 - 22.0, MB_, nobevel=True)
    # M14 receiver: heel, open top with the bolt, front ring
    mb.box2((-60.0, 50.0, -14.0), (40.0, 72.0, 14.0), MD, bevel=1.4)
    mb.box2((40.0, 50.0, -14.0), (118.0, 66.0, -9.0), MD, bevel=0.8)
    mb.box2((40.0, 50.0, 9.0), (118.0, 60.0, 14.0), MD, bevel=0.8)
    mb.cyl(116.0, 142.0, 15.5, MD, axis='u', c=(VB, 0.0), seg=32, chamfer=1.0)
    mb.cyl(40.0, 118.0, 9.6, MW, axis='u', c=(VB + 3.0, 0.0), seg=24)                      # bolt
    mb.box2((104.0, VB + 3.0, 7.0), (114.0, VB + 9.0, 12.5), MW, bevel=0.6)                 # bolt roller
    # rail bridge over the receiver (open on the right for ejection / op rod)
    mb.box2((-10.0, 82.0, -14.0), (CH1, 87.0, 14.0), MB_, bevel=0.8)
    mb.prism(fillet([(-10, 64), (CH1, 64), (CH1, 87), (-10, 87)], 2, 2), 'w', -25.0, -14.0, MB_, bevel=0.8)
    for (u0, u1) in ((-10.0, 8.0), (122.0, CH1)):
        mb.box2((u0, 64.0, 13.0), (u1, 87.0, 25.0), MB_, bevel=0.8)
    # trigger guard (M14) with the safety at its front, magazine latch
    path = fillet([(40.0, 21.0), (40.0, 6.0), (34.0, -14.0), (24.0, -21.0), (-18.0, -21.0), (-28.0, -14.0),
                   (-30.0, 0.0), (-30.0, 21.0)], [0, 4, 5, 5, 6, 5, 4, 0], 3, closed=False)
    mb.sweep([(u, v, 0.0) for u, v in path], rrect(-8.0, -2.0, 8.0, 2.0, 1.2, 2), MB_)
    mb.prism(fillet([(27, 20), (35, 20), (35, 5), (31, 2.5), (27, 7)], 1.0, 2), 'w', -3.2, 3.2, MB_, bevel=0.4)
    mb.box2((36.0, 13.0, -7.0), (44.0, 21.0, 7.0), MB_, bevel=0.8)


def forend(mb):
    fe = MB()
    outer = fillet(oct_poly(FE_TOP, FE_BOT, FE_HW, 12.0), 1.0, 1)
    inner = fillet(oct_poly(FE_TOP - 3.5, FE_BOT + 3.5, FE_HW - 3.5, 10.5), 1.0, 1)
    fe.tube_prism(outer, inner, 'u', FE0, FE1, MB_)
    cut = MB()
    for k in range(6):
        u = FE0 + 18.0 + k * 33.0
        for s in (1, -1):
            if k < 4:
                cut.prism(rrect(u, 64.0, u + 24.0, 72.0, 3.5, 2), 'w', s * 20.0, s * 30.0, MB_)
            cut.prism(rrect(u, 33.0, u + 24.0, 40.0, 3.5, 2), 'w', s * 20.0, s * 30.0, MB_)
    fe.boolean(cut, mat=MB_)
    mb.merge(fe)
    mb.tube_prism(fillet(oct_poly(FE_TOP + 0.8, FE_BOT - 0.8, FE_HW + 0.8, 12.4), 1.0, 1), inner, 'u', FE1 - 6.0,
                  FE1, MB_)
    pic_rail(mb, -8.0, FE1 - 2.0, MB_, frame=T(0, FE_TOP, 0), H=RAIL_TOP - FE_TOP)
    pic_rail(mb, FE0 + 10.0, FE1 - 3.0, MB_, frame=rail_frame('bottom', FE_BOT), H=6.0)
    vc = (FE_TOP + FE_BOT) / 2.0
    pic_rail(mb, FE1 - 86.0, FE1 - 3.0, MB_, frame=rail_frame('right', FE_HW, vc=vc), H=6.0)
    pic_rail(mb, FE1 - 86.0, FE1 - 3.0, MB_, frame=rail_frame('left', FE_HW, vc=vc), H=6.0)
    return vc


def front_end(mb):
    # thin barrel
    mb.lathe([(138.0, 0), (138.0, 13.0), (220.0, 12.0), (FS0, 10.6), (U_CROWN, 10.6), (U_CROWN, 0)], MD, seg=28,
             axis='u', c=(VB, 0.0))
    # front band, gas cylinder, gas lock, plug
    gv = VB - 17.0
    mb.prism(fillet([(-14.0, gv - 9.0), (14.0, gv - 9.0), (14.0, VB + 8.0), (-14.0, VB + 8.0)], 6.0, 3), 'u',
             FE1, FE1 + 10.0, MB_, bevel=0.8)
    mb.cyl(FE1 + 10.0, 474.0, 8.0, MD, axis='u', c=(gv, 0.0), seg=24, chamfer=0.6)
    mb.prism(fillet([(-11.0, gv - 8.0), (11.0, gv - 8.0), (11.0, VB + 11.5), (-11.0, VB + 11.5)], 5.0, 3), 'u',
             474.0, 488.0, MD, bevel=0.8)
    mb.cyl(488.0, 496.0, 6.5, MD, axis='u', c=(gv, 0.0), seg=20, chamfer=1.0)
    mb.prism(star(0.0, gv, 6.6, 0.6, 6), 'u', 496.0, 499.0, MD, nobevel=True)
    # front sight base clamped on the barrel + bayonet lug
    mb.prism(rrect(-11.5, VB - 12.0, 11.5, VB + 12.0, 5.0, 3), 'u', FS0, FS1, MB_, bevel=1.0)
    mb.prism(fillet([(FS0 + 4, VB - 12.0), (FS1 + 2, VB - 12.0), (FS1 + 2, VB - 21.0), (FS0 + 10, VB - 21.0)],
                    [0, 1.5, 1.5, 2], 2), 'w', -4.2, 4.2, MB_, bevel=0.6)
    for u in (FS0 + 5.0, FS1 - 5.0):
        pin_heads(mb, u, VB, 11.5, 1.6, MW, h=0.4, seg=10)
    # long cylindrical brake with rows of drilled holes
    brk = MB()
    brk.lathe([(BRK0, 10.8), (BRK0, 12.4), (BRK0 + 1.5, 13.4), (U_TIP - 1.5, 13.4), (U_TIP, 12.2), (U_TIP, 4.8),
               (U_TIP - 5.0, 4.8), (U_TIP - 5.0, 8.0), (BRK0 + 14.0, 8.0), (BRK0 + 14.0, 10.8)], MB_, seg=36,
              axis='u', c=(VB, 0.0), closed=True)
    cut = MB()
    for k in range(6):
        u = BRK0 + 20.0 + k * 9.5
        for ang in (90.0, 45.0, 135.0, 0.0, 180.0):
            a = math.radians(ang)
            M = T(u, VB, 0.0)
            d = Vector((0.0, math.sin(a), math.cos(a)))
            rot = Vector((0.0, 0.0, 1.0)).rotation_difference(d).to_matrix().to_4x4()
            cut.cyl(8.0, 18.0, 2.1, MB_, axis='w', c=(0.0, 0.0), seg=12, xf=M @ rot)
    brk.boolean(cut, mat=MB_)
    mb.merge(brk)


def grip_and_stock(mb):
    gax = GripAxis(top=(-46.0, 26.0), rake=24.0, length=112.0)

    def hw(t):
        return 13.8 + 0.6 * math.sin(math.pi * t) - 0.6 * t

    def hf(t):
        return 20.5 + 3.4 * math.exp(-((t - 0.45) / 0.08) ** 2) - 1.0 * t

    def hb(t):
        return 24.0 - 2.5 * t

    grip_loft(mb, gax, 'Polymer_Ribs', hw, hf, hb, steps=24, n=24, p=2.5)
    # telescoping stock: spine around the tube, skeleton side plates with round holes, riser, pad
    sv = VB + 4.0
    ub = U_BUTT + 12.0
    mb.prism(rrect(-12.0, sv - 15.0, 12.0, sv + 16.0, 6.0, 3), 'u', -150.0, ub + 6.0, PB, bevel=1.2)
    plate = fillet([(-150, sv + 16), (-150, sv - 16), (ub + 10, -34), (ub + 4, -40), (ub, -40), (ub, sv + 20)],
                   [3, 4, 4, 3, 2, 3], 2)
    plates = MB()
    for s in (1, -1):
        plates.prism(plate, 'w', s * 11.0, s * 16.5, PB)
    holes = MB()
    for (u, v, r) in ((-172.0, 42.0, 7.5), (-196.0, 34.0, 8.0), (-220.0, 22.0, 9.5), (-222.0, -2.0, 7.0),
                      (-198.0, 12.0, 6.0), (-244.0, 10.0, 7.0), (-244.0, 42.0, 7.0), (-246.0, -20.0, 6.0)):
        holes.cyl(-20.0, 20.0, r, PB, axis='w', c=(u, v), seg=24)
    plates.boolean(holes, mat=PB)
    mb.merge(plates)
    mb.prism(fillet([(ub + 14, sv + 20), (ub + 14, -30), (ub + 2, -40), (ub, -40), (ub, sv + 20)], [2, 6, 3, 0, 0], 2),
             'w', -18.5, 18.5, PB, bevel=2.0, seg=2)
    riser = fillet([(-150, sv + 27), (-232, sv + 28), (-234, sv + 39), (-154, sv + 38)], [3, 3, 4, 4], 2)
    mb.prism(riser, 'w', -15.5, 15.5, PB, bevel=2.5, seg=2)
    for u in (-168.0, -214.0):
        mb.cyl(sv + 14.0, sv + 28.0, 3.4, MB_, axis='v', c=(u, 0.0), seg=12)
        ridged_knob(mb, 16.5, 20.0, 4.2, MB_, 'w', (u, sv + 22.0), ridges=8, depth=0.4)
    mb.box2((-174.0, sv - 21.0, -5.0), (-150.0, sv - 15.0, 5.0), PB, bevel=1.2)
    pad = fillet([(ub, sv + 22), (ub, -41.5), (U_BUTT + 2, -39.5), (U_BUTT, -32), (U_BUTT, sv + 16), (U_BUTT + 2, sv + 21)],
                 [2, 2, 2, 4, 4, 2], 2)
    mb.prism(pad, 'w', -19.0, 19.0, 'Rubber_Ribs', bevel=2.5, seg=2)
    return gax, hf, hb


def op_rod(mb):
    # visible operating rod along the right side + its handle
    mb.box2((86.0, 58.0, 14.0), (260.0, 63.5, 18.5), MW, bevel=0.8)
    mb.box2((100.0, 58.0, 14.0), (116.0, 63.5, 21.0), MW, bevel=0.8)
    knob = fillet([(96.0, 58.0), (120.0, 58.0), (123.0, 68.0), (117.0, 76.0), (102.0, 76.0), (97.0, 69.0)],
                  [2, 2, 3, 3, 3, 2], 2)
    mb.prism(knob, 'w', 20.0, 28.0, MW, bevel=1.2)
    for k in range(4):
        mb.box2((101.0 + k * 4.5, 76.0, 21.0), (102.6 + k * 4.5, 77.0, 27.0), MW)


def trigger(mb):
    cu, cv = 25.0, -2.0
    R0 = math.hypot(cu, cv)
    R1 = R0 + 4.6
    front = [(cu - math.sqrt(R0 * R0 - (v - cv) ** 2), v) for v in [16.0 - 28.0 * i / 10 for i in range(11)]]
    back = [(cu - math.sqrt(R1 * R1 - (v - cv) ** 2), v) for v in [-10.0 + 25.0 * i / 10 for i in range(11)]]
    poly = front + [(front[-1][0] + 2.4, -14.2)] + back + [(-9.0, 17.0), (-9.0, 30.0), (3.0, 30.0)]
    mb.prism(clean_poly(poly), 'w', -3.4, 3.4, MB_, bevel=0.6)


def iron_sights(mb):
    # M14 rear aperture sight on the receiver heel: base, aperture housing, windage + elevation knobs
    uc = -42.0
    mb.prism(fillet([(uc - 12, 72.0), (uc + 10, 72.0), (uc + 8, 84.0), (uc - 10, 84.0)], 2.0, 2), 'w', -10.0, 10.0,
             MB_, bevel=0.6)
    tower = fillet([(uc - 7, 83.0), (uc + 6, 83.0), (uc + 4, SIGHT + 7.0), (uc - 5, SIGHT + 7.0)], [0, 0, 2, 2], 2)
    mb.prism(tower, 'w', -6.5, 6.5, MB_, bevel=0.6)
    mb.tube(uc - 5.0, uc + 4.0, 4.0, 1.3, MB_, axis='u', c=(SIGHT, 0.0), seg=16)
    ridged_knob(mb, -10.0, -16.5, 7.6, MB_, 'w', (uc - 1.0, 78.0), ridges=18, depth=0.6)        # elevation (left)
    mb.cyl(-10.0, -11.2, 8.6, MB_, axis='w', c=(uc - 1.0, 78.0), seg=24, chamfer=0.4)
    ridged_knob(mb, 10.0, 15.5, 6.4, MB_, 'w', (uc - 1.0, 78.0), ridges=16, depth=0.5)          # windage (right)
    mb.cyl(10.0, 11.0, 7.2, MB_, axis='w', c=(uc - 1.0, 78.0), seg=24, chamfer=0.4)
    # front sight: post with protective ears on the front sight base
    fu = (FS0 + FS1) / 2.0
    tw = fillet([(FS0 + 1, VB + 11.0), (FS1 - 1, VB + 11.0), (FS1 - 5, SIGHT - 8.0), (FS0 + 4, SIGHT - 8.0)],
                [2, 2, 2, 2], 2)
    mb.prism(tw, 'w', -5.5, 5.5, MB_, bevel=0.8)
    ear = fillet([(FS0 + 3, SIGHT - 9.0), (FS1 - 4, SIGHT - 9.0), (FS1 - 5.5, SIGHT + 4.0), (FS0 + 4.5, SIGHT + 4.0)],
                 [0, 0, 2.0, 2.0], 2)
    for s in (1, -1):
        mb.prism(ear, 'w', s * 4.2, s * 7.0, MB_, bevel=0.5)
    mb.box2((fu - 1.0, SIGHT - 9.0, -1.0), (fu + 1.0, SIGHT, 1.0), MB_, bevel=0.2)


def magazine(mb):
    return box_mag(mb, MAG_SEAT, top_len=84.0, bot_len=82.0, width=27.0, straight=132.0, mat='Metal_Dark',
                   floor_mat=MB_, ribs=[(-0.80, -0.62, 0.22, 0.88, 0.8), (0.62, 0.80, 0.22, 0.88, 0.8),
                                        (-0.25, 0.25, 0.30, 0.82, 0.6)],
                   front_lug=(12.0, 8.0, 4.0), cart=CART_762, lip=1.5)


def build():
    a = Asset(ID, 'weapon', caliber='7.62x51mm NATO', capacity=20)
    body = MB()
    chassis(body)
    vc = forend(body)
    front_end(body)
    gax, hf, hb = grip_and_stock(body)
    a.mesh(body, 'Body')

    bolt = MB()
    op_rod(bolt)
    a.mesh(bolt, 'Bolt', origin=(108.0, 64.0, 22.0))

    trig = MB()
    trigger(trig)
    a.mesh(trig, 'Trigger', origin=(-3.0, 28.0, 0.0))

    sights = MB()
    iron_sights(sights)
    a.mesh(sights, 'IronSights')

    mag = MB()
    magazine(mag)
    a.mesh(mag, 'Mag', origin=(MAG_SEAT[0], MAG_SEAT[1], 0.0))

    a.socket('Socket_Muzzle', (U_TIP, VB, 0.0))
    a.socket('Socket_Optic', (50.0, RAIL_TOP, 0.0))
    a.socket('Socket_Underbarrel', (FE0 + 0.6 * (FE1 - FE0), FE_BOT - 6.0, 0.0))
    a.socket('Socket_Side', (FE1 - 34.0, vc, FE_HW + 6.0))
    a.socket('Socket_Mag', (MAG_SEAT[0], MAG_SEAT[1], 0.0))
    t = 0.05 + 0.45 * 0.95
    gp = gax.point(t, front=(hf(t) - hb(t)) / 2.0)
    a.grip_socket('Grip_R', (gp.x, gp.y, 0.0), gax.rake)
    a.socket('Grip_L', (FE0 + 0.5 * (FE1 - FE0), vc, 0.0))
    a.socket('Sight_Iron', (-42.5, SIGHT, 0.0))
    a.socket('Eject', (95.0, 62.0, 14.0))

    a.meta['moving'] = {'Bolt': {'travel': BOLT_TRAVEL, 'note': 'operating rod + handle (right side), straight back'}}
    a.meta['mag_insert'] = [0.0, 1.0, 0.0]
    a.meta['ironSightsNote'] = 'IronSights = M14 rear aperture sight on the receiver heel + front post with ears'
    return a
