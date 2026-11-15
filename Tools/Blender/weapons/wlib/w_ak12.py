"""
AK-12 (5.45x39), 2018+ production look, matte black (reference photos: AK-12 6P70):
stamped riveted receiver, rigidly locked dust cover whose full-length Picatinny rail continues into the
handguard's integral top rail (one long rail), small diopter rear-sight tower at the rear of the rail,
big stamped selector with finger shelf, right-side charging handle, paddle magazine release, ergonomic
grip, waffle-pattern polymer magazine, long handguard with short 3/9 rails and a slotted lower half,
exposed barrel + gas tube, tall front-sight / gas block with two ears, bayonet lug, cleaning rod, large
slotted muzzle brake (part of Body; Socket_Muzzle at its flat front face), skeleton side-folding stock.

Gun space (mm): u forward, v up, w right; origin = trigger blade front face centre.
"""
import math

from mathutils import Vector

from .core import MB, Asset, fillet, rrect, T, Rw, about, clean_poly, annulus_sector, circle
from .parts import (pic_rail, rail_frame, GripAxis, grip_loft, box_mag, CART_545, pin_heads, strut,
                    pic_clamp, hex_screw, ridged_knob)

ID = 'AK12'
VB = 50.0
RAIL_TOP = 86.0
SIGHT = 104.0
U_BF = 128.0
U_CROWN = U_BF + 415.0        # 543
BRK0, U_TIP = 533.0, 625.0     # muzzle brake
DC0, DC1 = -90.0, 176.0        # dust cover (rear .. rear-sight block)
HG0, HG1 = 176.0, 341.0        # handguard
GB0, GB1 = 418.0, 452.0        # gas block / front sight base
MAG_SEAT = (80.0, 34.0)
U_BUTT = -312.0
BOLT_TRAVEL = 0.115

MB_ = 'Metal_Black'
MD = 'Metal_Dark'
MW = 'Metal_Worn'
PB = 'Polymer_Black'
RU = 'Rubber'


def receiver(mb):
    hw_in, hw_out = 12.5, 14.5
    outline = fillet([(-95, 18), (150, 18), (150, 64), (-95, 64)], [2, 2, 1, 1], 2)
    mb.prism(outline, 'w', -hw_out, -hw_in, MB_, bevel=0.5)
    right = [(-95, 18), (150, 18), (150, 64), (118, 64), (118, 56), (102, 56), (102, 45),
             (30, 45), (30, 56), (-10, 56), (-10, 64), (-95, 64)]
    mb.prism(fillet(right, [2, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1], 2), 'w', hw_in, hw_out, MB_, bevel=0.5)
    mb.box2((-93, 41, -hw_in), (148, 62, hw_in), MB_)
    mb.box2((-93, 18.5, -hw_in), (43, 41, hw_in), MB_)
    mb.box2((117, 18.5, -hw_in), (148, 41, hw_in), MB_)
    for s in (1, -1):
        # stamped stiffening rib along the lower edge (between the rear trunnion and the magazine well)
        mb.prism(fillet([(-64, 21.0), (41, 21.0), (41, 24.0), (-64, 24.0)], 1.2, 2), 'w', s * hw_out,
                 s * (hw_out + 0.7), MB_, bevel=0.3)
        # rear trunnion plate, front trunnion bulge, magazine-well bulge, magazine guide dimple
        rt = fillet([(-94.5, 19.5), (-66, 19.5), (-66, 58.0), (-94.5, 58.0)], [1.5, 3, 3, 1.5], 2)
        mb.prism(rt, 'w', s * (hw_out - 0.5), s * (hw_out + 0.9), MB_, bevel=0.5)
        ft = fillet([(116, 19.5), (149.5, 19.5), (149.5, 52.0), (123, 52.0), (116, 45.0)], [3, 1.5, 1.5, 4, 3], 2)
        mb.prism(ft, 'w', s * (hw_out - 0.5), s * (hw_out + 1.2), MB_, bevel=0.6)
        mwb = fillet([(44, 18.0), (114, 18.0), (114, 31.0), (106, 35.5), (52, 35.5), (44, 31.0)],
                     [1, 1, 3, 4, 4, 3], 2)
        mb.prism(mwb, 'w', s * (hw_out - 0.5), s * (hw_out + 1.5), MB_, bevel=0.8)
        mb.prism(fillet([(58, 38.5), (92, 38.5), (92, 42.5), (58, 42.5)], 1.8, 2), 'w', s * hw_out,
                 s * (hw_out + 0.7), MB_, bevel=0.3)
    for (u, v) in ((122, 27), (134, 27), (128, 35), (141, 31), (128, 45), (143, 45)):
        pin_heads(mb, u, v, hw_out + 1.2, 2.0, MB_, h=0.8, seg=12)
    for (u, v) in ((-87, 27), (-87, 49), (-78, 38), (-72, 26), (-72, 51)):
        pin_heads(mb, u, v, hw_out + 0.9, 2.0, MB_, h=0.8, seg=12)
    for (u, v) in ((-3, 30), (-27, 30)):
        pin_heads(mb, u, v, hw_out, 1.9, MB_, h=0.7, seg=10)
    pin_heads(mb, 56, 27, hw_out + 1.5, 1.9, MB_, h=0.7, seg=10)
    # dust cover: separate stamped cover over the receiver walls (flared lower edge), rear lip under the
    # diopter sight, front end hinged in the rear-sight block; ONE continuous rail over cover + handguard
    dc = fillet([(-16.3, 60.5), (16.3, 60.5), (16.3, 63.5), (15.6, 65.0), (15.6, 71.0), (12.0, 79.0),
                 (-12.0, 79.0), (-15.6, 71.0), (-15.6, 65.0), (-16.3, 63.5)],
                [0.4, 0.4, 0.4, 0.4, 4, 2, 2, 4, 0.4, 0.4], 2)
    mb.prism(dc, 'u', DC0 + 8.0, DC1 - 27.0, MB_, bevel=0.6)
    for k in range(5):                                   # stamped ribs on the dust cover sides
        u = DC0 + 30.0 + k * 42.0
        for s in (1, -1):
            mb.box2((u, 65.5, s * 15.3), (u + 22.0, 69.5, s * 16.2), MB_, bevel=0.4)
    lip = fillet([(-17.0, 56.0), (17.0, 56.0), (17.0, 71.5), (12.8, 79.6), (-12.8, 79.6), (-17.0, 71.5)],
                 [1.5, 1.5, 4, 2, 2, 4], 2)
    mb.prism(lip, 'u', DC0 - 8.0, DC0 + 9.0, MB_, bevel=0.8)                                 # rear lip
    blk = fillet([(DC1 - 28, 30), (DC1, 30), (DC1, 79), (DC1 - 28, 79)], [2, 2, 2, 2], 2)
    mb.prism(blk, 'w', -15.0, 15.0, MB_, bevel=1.0)
    mb.cyl(-16.0, 16.0, 3.4, MB_, axis='w', c=(DC1 - 14.0, 72.0), seg=14)
    pic_rail(mb, DC0 + 4.0, HG1 - 2.0, MB_, frame=T(0, 79.0, 0), H=RAIL_TOP - 79.0)
    # selector lever (right side, 'fire'): long stamped plate with raised rib, front tab and an
    # outward-bent finger shelf right above the trigger guard
    lever = [(-92, 43), (-78, 41.0), (30, 50.5), (40, 52.5), (41.5, 58.5), (35, 61.0), (-78, 59.0), (-92, 55)]
    with mb.group() as g:
        mb.prism(fillet(lever, [3, 2, 1.5, 1.5, 1.5, 1.5, 2, 3], 2), 'w', hw_out, hw_out + 2.8, MB_, bevel=0.6)
        mb.prism(fillet([(-70, 47.0), (24, 54.0), (24, 56.2), (-70, 49.2)], 0.6, 1), 'w', hw_out + 2.6,
                 hw_out + 3.8, MB_)
        tab = [(20, 51.5), (35, 49.5), (42, 42.5), (37, 39.0), (23, 45.5)]
        mb.prism(fillet(tab, 1.5, 2), 'w', hw_out + 0.5, hw_out + 3.4, MB_, bevel=0.5)
        shelf = fillet([(21, hw_out + 2.0), (43, hw_out + 2.0), (43, hw_out + 9.5), (37, hw_out + 11.5),
                        (25, hw_out + 10.0)], [0, 1.5, 2.5, 3, 2], 2)
        mb.prism(shelf, 'v', 39.0, 42.0, MB_, bevel=0.6)
    mb.transform(g.verts, about(Rw(-8.0), (-84.0, 49.0, 0.0)))
    mb.cyl(hw_out, hw_out + 3.8, 6.4, MB_, axis='w', c=(-84.0, 49.0), seg=20, chamfer=0.6)


def handguard(mb):
    hg = MB()
    outer = rrect(-24.0, 20.0, 24.0, 79.0, 8.0, 3)
    inner = rrect(-21.0, 23.0, 21.0, 76.0, 5.0, 3)
    hg.tube_prism(outer, inner, 'u', HG0, HG1, PB)
    cut = MB()
    # slotted lower half: two rows of side slots + bottom slots
    for k in range(7):
        u = HG0 + 14.0 + k * 21.0
        for vlo, vhi in ((26.0, 32.0), (36.0, 42.0)):
            cut.box2((u, vlo, -30.0), (u + 14.0, vhi, 30.0), PB, bevel=1.5)
        if k not in (3, 4):
            cut.box2((u + 1.0, 10.0, -5.0), (u + 13.0, 26.0, 5.0), PB, bevel=1.5)
    hg.boolean(cut, mat=PB)
    mb.merge(hg)
    # short 3/9 rails at the front, short bottom rail section for the underbarrel socket
    pic_rail(mb, HG1 - 78.0, HG1 - 4.0, MB_, frame=rail_frame('right', 24.0, vc=VB + 2.0), H=6.0)
    pic_rail(mb, HG1 - 78.0, HG1 - 4.0, MB_, frame=rail_frame('left', 24.0, vc=VB + 2.0), H=6.0)
    ub = HG0 + 0.6 * (HG1 - HG0)
    mb.box2((ub - 25.0, 18.5, -9.0), (ub + 25.0, 21.0, 9.0), MB_, bevel=0.5)
    pic_rail(mb, ub - 24.0, ub + 24.0, MB_, frame=rail_frame('bottom', 20.0), H=6.0)
    # retainer ring / front cap
    mb.tube_prism(rrect(-24.8, 19.2, 24.8, 79.8, 8.4, 3), inner, 'u', HG0 - 2.0, HG0 + 4.0, MB_)
    mb.tube_prism(rrect(-24.6, 19.4, 24.6, 79.4, 8.3, 3), inner, 'u', HG1 - 4.0, HG1, MB_)
    for s in (1, -1):
        hex_screw(mb, (HG0 + 12.0, 70.0, s * 24.0), 'w', 2.4, 1.0, MB_, sign=s)


def front_end(mb):
    # barrel, gas tube, cleaning rod
    mb.lathe([(148.0, 0), (148.0, 11.0), (350.0, 10.0), (GB1, 9.0), (BRK0, 8.2), (U_CROWN, 7.4), (U_CROWN, 0)],
             MD, seg=28, axis='u', c=(VB, 0.0))
    mb.cyl(HG1 - 6.0, GB0 + 2.0, 8.0, MD, axis='u', c=(VB + 17.0, 0.0), seg=20)
    mb.lathe([(HG1 - 20.0, 0), (HG1 - 20.0, 2.6), (GB0 + 30.0, 2.6), (GB0 + 32.0, 3.6), (GB0 + 36.0, 3.6),
              (GB0 + 36.0, 0)], MW, seg=12, axis='u', c=(VB - 14.0, 0.0))
    # gas block / front sight base, bayonet lug
    gb = fillet([(GB0, VB - 11.0), (GB1, VB - 11.0), (GB1 + 3.0, VB - 7.0), (GB1 + 3.0, VB + 14.0),
                 (GB1 - 4.0, VB + 26.0), (GB0, VB + 26.0)], [2, 2, 2, 3, 3, 2], 2)
    mb.prism(gb, 'w', -11.5, 11.5, MB_, bevel=0.8)
    mb.tube(GB0 - 2.0, GB0 + 6.0, 13.0, 9.0, MB_, axis='u', c=(VB, 0.0), seg=28, chamfer=0.6)
    mb.cyl(GB1, GB1 + 8.0, 8.4, MB_, axis='u', c=(VB + 17.0, 0.0), seg=20, chamfer=1.2)
    mb.prism(fillet([(GB0 + 6, VB - 11.0), (GB1 + 4, VB - 11.0), (GB1 + 4, VB - 19.0), (GB0 + 14, VB - 19.0)],
                    [0, 1.5, 1.5, 2], 2), 'w', -4.0, 4.0, MB_, bevel=0.6)
    mb.prism(fillet([(GB0 + 12, VB - 19.0), (GB0 + 24, VB - 19.0), (GB0 + 24, VB - 23.0), (GB0 + 14, VB - 23.0)],
                    1.0, 2), 'w', -4.0, 4.0, MB_, bevel=0.4)
    for u in (GB0 + 8.0, GB1 - 8.0):
        pin_heads(mb, u, VB + 4.0, 11.5, 1.8, MW, h=0.4, seg=10)
    # large muzzle brake: thick cylinder, transverse side slots through, flat front face
    brk = MB()
    brk.lathe([(BRK0, 7.6), (BRK0, 13.5), (BRK0 + 2.0, 15.0), (U_TIP - 1.5, 15.0), (U_TIP, 13.6), (U_TIP, 4.2),
               (U_TIP - 6.0, 4.2), (U_TIP - 6.0, 6.0), (BRK0 + 12.0, 6.0), (BRK0 + 12.0, 7.6)], MB_, seg=36,
              axis='u', c=(VB, 0.0), closed=True)
    cut = MB()
    for k in range(5):
        u = BRK0 + 22.0 + k * 12.5
        cut.box2((u, VB - 8.5, -20.0), (u + 6.0, VB + 8.5, 20.0), MB_, bevel=1.2)
    for k in range(3):
        u = BRK0 + 26.0 + k * 18.0
        cut.cyl(VB + 3.0, VB + 20.0, 2.6, MB_, axis='v', c=(u, 0.0), seg=12)
    brk.boolean(cut, mat=MB_)
    mb.merge(brk)


def lower_parts(mb):
    path = fillet([(37.0, 19.0), (37.0, 2.0), (30.0, -15.0), (18.0, -21.5), (-20.0, -21.5),
                   (-31.0, -13.0), (-33.0, 2.0), (-33.0, 19.0)], [0, 5, 6, 6, 6, 5, 4, 0], 3, closed=False)
    mb.sweep([(u, v, 0.0) for u, v in path], rrect(-6.5, -1.6, 6.5, 1.6, 1.0, 2), MB_)
    for (u, v) in ((35.0, 17.0), (-31.0, 17.0)):
        mb.cyl(-7.0, 7.0, 1.6, MB_, axis='w', c=(u, v), seg=10)
    # paddle magazine release ahead of the trigger guard, behind the magazine
    paddle = fillet([(38.6, 19.0), (44.6, 19.0), (44.6, 6.0), (43.0, -6.0), (38.0, -9.5), (34.5, -8.0),
                     (38.6, 4.0)], [0, 1.0, 3.0, 2.5, 2.0, 2.0, 2.0], 2)
    mb.prism(paddle, 'w', -7.5, 7.5, MB_, bevel=0.8)
    for k in range(3):
        v = -6.5 + k * 3.2
        mb.box2((42.6 - k * 0.6, v - 0.7, -7.9), (44.2 - k * 0.3, v + 0.7, 7.9), MB_, bevel=0.3)
    mb.cyl(-8.2, 8.2, 2.0, MW, axis='w', c=(41.6, 13.0), seg=12)
    gax = GripAxis(top=(-46.0, 22.0), rake=17.0, length=112.0)

    def hw(t):
        return 15.0 + 0.9 * math.sin(math.pi * t) - 0.8 * t

    def hf(t):
        g = 0.0
        for c in (0.30, 0.52, 0.74):
            g += math.exp(-((t - c) / 0.07) ** 2)
        return 20.0 + 1.6 * g - 1.0 * t

    def hb(t):
        return 24.0 + 2.6 * math.exp(-((t - 0.45) / 0.2) ** 2) - 2.5 * t

    grip_loft(mb, gax, 'Polymer_Stipple', hw, hf, hb, steps=24, n=24, p=2.6)
    return gax, hw, hf, hb


def stock(mb):
    """Skeleton side-folding, telescoping stock: chunky hinge block with knuckle, thick top beam with a
    raised (adjustable) cheek riser, thick angled lower strut (open triangle), tall rubber butt pad and a
    toe spur."""
    blk = fillet([(-95, 20), (-95, 82), (-104, 85.5), (-118, 82), (-118, 26), (-108, 20)], [0, 0, 3, 4, 4, 3], 2)
    mb.prism(blk, 'w', -17.0, 17.0, PB, bevel=1.5)
    mb.cyl(24.0, 78.0, 5.6, MB_, axis='v', c=(-112.0, -18.0), seg=20, chamfer=0.8)            # hinge knuckle
    for v in (40.0, 62.0):
        mb.cyl(v, v + 3.0, 6.4, MB_, axis='v', c=(-112.0, -18.0), seg=20, chamfer=0.6)
    mb.cyl(17.0, 19.6, 5.2, MB_, axis='w', c=(-106.0, 64.0), seg=20, chamfer=0.8)              # fold release
    ub = U_BUTT + 12.0
    # telescoping housing + thick top beam with a raised cheek riser
    mb.prism(rrect(-16.5, 57.0, 16.5, 84.0, 6.5, 3), 'u', -115.0, -168.0, PB, bevel=1.2)
    beam = fillet([(-160, 58), (ub + 8, 58), (ub + 8, 82), (-160, 82)], [3, 0, 0, 3], 2)
    mb.prism(beam, 'w', -15.0, 15.0, PB, bevel=2.0, seg=2)
    riser = fillet([(-252, 81), (-178, 81), (-182, 92), (-192, 97), (-240, 97), (-250, 92)], [0, 0, 4, 6, 6, 4], 2)
    mb.prism(riser, 'w', -14.0, 14.0, PB, bevel=1.5, seg=2)
    mb.box2((-247.0, 80.4, -14.4), (-183.0, 81.6, 14.4), RU)                                    # riser seam
    ridged_knob(mb, 14.8, 18.0, 4.6, MB_, 'w', (-196.0, 70.0), ridges=12, depth=0.5)          # riser lock
    for u in range(-158, -118, 9):                       # position notches on the slide bar
        mb.box2((u, 55.6, -6.0), (u + 4.0, 57.4, 6.0), RU)
    mb.box2((-186.0, 51.5, -5.0), (-166.0, 58.5, 5.0), PB, bevel=1.2)                          # adjust latch
    # thick lower strut from the hinge block bottom to the toe
    mb.prism(fillet(strut((-114.0, 30.0), (ub + 18.0, -40.0), 10.0), 3.0, 2), 'w', -13.0, 13.0, PB, bevel=2.0)
    # vertical butt, toe spur and tall rubber pad
    butt = fillet([(ub + 22, 86), (ub + 22, -42), (ub + 8, -54), (ub, -54), (ub, 86)], [4, 8, 3, 0, 0], 2)
    mb.prism(butt, 'w', -18.5, 18.5, PB, bevel=2.5, seg=2)
    spur = fillet([(ub + 20, -44), (ub + 8, -56), (ub + 1, -69), (ub - 2, -66), (ub + 1, -52)], [2, 3, 2, 1.5, 2], 2)
    mb.prism(spur, 'w', -11.0, 11.0, PB, bevel=1.2)
    pad = fillet([(ub, 89), (ub, -56), (U_BUTT + 2, -54), (U_BUTT, -46), (U_BUTT, 82), (U_BUTT + 2, 88)],
                 [2, 2, 2, 4, 4, 2], 2)
    mb.prism(pad, 'w', -19.5, 19.5, 'Rubber_Ribs', bevel=2.5, seg=2)


def bolt_carrier(mb):
    mb.box2((31.0, 45.5, 4.0), (101.0, 60.5, 13.2), MW, bevel=0.6)
    mb.box2((36.0, 48.0, 12.8), (60.0, 52.0, 13.6), MW, bevel=0.3)
    mb.box2((101.0, 56.5, 4.0), (115.0, 61.0, 13.6), MW, bevel=0.6)
    mb.box2((105.0, 55.0, 12.0), (115.0, 61.0, 25.0), MW, bevel=1.0)
    mb.lathe([(23.0, 0), (23.0, 5.0), (25.0, 7.0), (31.0, 7.0), (33.0, 5.5), (33.5, 0)], MW, seg=20,
             axis='w', c=(110.0, 58.0))
    for k in range(4):
        mb.tube(25.0 + k * 1.6, 25.6 + k * 1.6, 7.2, 6.4, MW, axis='w', c=(110.0, 58.0), seg=20)


def trigger(mb):
    cu, cv = 24.0, -1.0
    R0 = math.hypot(cu, cv)
    R1 = R0 + 4.6
    front = [(cu - math.sqrt(R0 * R0 - (v - cv) ** 2), v) for v in [17.0 - 29.0 * i / 10 for i in range(11)]]
    back = [(cu - math.sqrt(R1 * R1 - (v - cv) ** 2), v) for v in [-9.5 + 26.0 * i / 10 for i in range(11)]]
    poly = front + [(front[-1][0] + 2.6, -13.5)] + back + [(-9.0, 19.0), (-9.0, 32.0), (3.0, 32.0)]
    mb.prism(clean_poly(poly), 'w', -3.3, 3.3, MB_, bevel=0.6)


def iron_sights(mb):
    # diopter rear-sight tower on the rear of the rail
    uc = DC0 + 14.0
    pic_clamp(mb, uc - 10.0, uc + 10.0, MB_, frame=T(0, RAIL_TOP, 0), top=4.0, hw=12.0, nut=None, bevel=0.0)
    tower = fillet([(uc - 8.0, RAIL_TOP + 3.5), (uc + 8.0, RAIL_TOP + 3.5), (uc + 4.0, SIGHT + 7.0),
                    (uc - 6.0, SIGHT + 7.0)], [0, 0, 2.5, 2.5], 2)
    mb.prism(tower, 'w', -7.0, 7.0, MB_, bevel=0.6)
    mb.tube(uc - 7.0, uc + 5.0, 3.6, 1.2, MB_, axis='u', c=(SIGHT, 0.0), seg=16)
    ridged_knob(mb, 7.0, 11.0, 3.8, MB_, 'w', (uc - 1.0, RAIL_TOP + 8.0), ridges=8, depth=0.4)
    # front sight: post between two protective ears on top of the gas block
    fu = (GB0 + GB1) / 2.0 + 2.0
    mb.prism(rrect(fu - 7.0, VB + 25.0, fu + 7.0, VB + 30.0, 1.5, 2), 'w', -9.0, 9.0, MB_, bevel=0.5)
    ear = fillet([(fu - 6.0, VB + 29.0), (fu + 6.0, VB + 29.0), (fu + 4.0, SIGHT + 4.0), (fu - 4.0, SIGHT + 4.0)],
                 [0, 0, 2.0, 2.0], 2)
    for s in (1, -1):
        mb.prism(ear, 'w', s * 5.0, s * 8.0, MB_, bevel=0.5)
    mb.cyl(VB + 29.0, VB + 33.0, 2.6, MB_, axis='v', c=(fu, 0.0), seg=14, chamfer=0.4)
    mb.box2((fu - 1.0, VB + 32.0, -0.9), (fu + 1.0, SIGHT, 0.9), MB_, bevel=0.2)


def magazine(mb):
    return box_mag(mb, MAG_SEAT, top_len=70.0, bot_len=60.0, width=24.6, straight=22.0, R=280.0,
                   arc_deg=38.0, mat=PB, floor_mat=PB, cells=(8, 4, 2.2, 0.9, 0.16, 0.90),
                   front_lug=(9.0, 6.0, 4.0), cart=CART_545, lip=2.0, n_arc=16)


def build():
    a = Asset(ID, 'weapon', caliber='5.45x39mm', capacity=30)
    body = MB()
    receiver(body)
    handguard(body)
    front_end(body)
    gax, hw, hf, hb = lower_parts(body)
    stock(body)
    a.mesh(body, 'Body')

    bolt = MB()
    bolt_carrier(bolt)
    a.mesh(bolt, 'Bolt', origin=(110.0, 58.0, 13.0))

    trig = MB()
    trigger(trig)
    a.mesh(trig, 'Trigger', origin=(-2.0, 30.0, 0.0))

    sights = MB()
    iron_sights(sights)
    a.mesh(sights, 'IronSights')

    mag = MB()
    magazine(mag)
    a.mesh(mag, 'Mag', origin=(MAG_SEAT[0], MAG_SEAT[1], 0.0))

    a.socket('Socket_Muzzle', (U_TIP, VB, 0.0))
    a.socket('Socket_Optic', (30.0, RAIL_TOP, 0.0))
    a.socket('Socket_Underbarrel', (HG0 + 0.6 * (HG1 - HG0), 14.0, 0.0))
    a.socket('Socket_Side', (HG1 - 34.0, VB + 2.0, 30.0))
    a.socket('Socket_Mag', (MAG_SEAT[0], MAG_SEAT[1], 0.0))
    t = 0.04 + 0.45 * 0.96
    gp = gax.point(t, front=(hf(t) - hb(t)) / 2.0)
    a.grip_socket('Grip_R', (gp.x, gp.y, 0.0), gax.rake)
    a.socket('Grip_L', (HG0 + 0.5 * (HG1 - HG0), VB + 2.0, 0.0))
    a.socket('Sight_Iron', (DC0 + 13.0, SIGHT, 0.0))
    a.socket('Eject', (66.0, 53.0, 14.5))

    a.meta['moving'] = {'Bolt': {'travel': BOLT_TRAVEL, 'note': 'bolt carrier + right-side charging handle, straight back'}}
    a.meta['mag_insert'] = [0.0, 1.0, 0.0]
    a.meta['ironSightsNote'] = 'IronSights = diopter tower on the rail + front post with ears on the gas block'
    return a
