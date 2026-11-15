"""
M4A1 carbine (5.56x45), classic military configuration (reference: US Army M4A1 photos):
flat-top upper with forward assist, ejection port + open dust cover, brass deflector and T charging
handle; forged lower with flared magwell, mag-release fence, bolt catch, selector, pins and hinged
trigger guard; A2 grip; carbine-length quad rail with ribbed ladder covers and slip ring; A2 front sight
base (post + ears, bayonet lug, sling swivel); M4 profile barrel with the M203 step; classic 6-position
stock on a buffer tube with castle nut and sling end plate; 30-rd aluminium STANAG magazine.
The A2 birdcage flash hider is the Muzzle_FlashHider attachment (default muzzle device) so that
Socket_Muzzle stays at the crown.

Gun space (mm): u forward, v up, w right; origin = trigger blade front face centre.
"""
import math

from .core import MB, Asset, fillet, rrect, arc, T, Ru, Rw, about, clean_poly, circle
from .parts import (pic_rail, rail_frame, GripAxis, grip_loft, box_mag, CART_556, pin_heads, star, buis_rear,
                    strut, hex_screw, ladder_cover, wire_loop, pic_clamp)

ID = 'M4A1'
VB = 42.0                 # bore axis height
RAIL_TOP = 70.5           # top rail surface (receiver and handguard)
SIGHT = 107.0             # iron sight line
U_BF = 108.0              # bolt face in battery
U_MUZ = U_BF + 368.0      # muzzle crown (14.5" barrel) = Socket_Muzzle
HG0, HG1 = 147.0, 312.0   # carbine quad rail
FSB0, FSB1 = 315.0, 338.0  # A2 front sight base
MAG_SEAT = (62.5, 28.0)
U_BUTT = -315.0
BOLT_TRAVEL = 0.085
HG_HALF = 19.5            # handguard body half width (rail bases)

MB_ = 'Metal_Black'
MD = 'Metal_Dark'
MW = 'Metal_Worn'
PB = 'Polymer_Black'


# ------------------------------------------------------------------------------ upper receiver
def upper(mb):
    prof = fillet([(-50, 29), (122, 29), (131, 33), (131, 55), (-50, 55)], [1.5, 2, 2, 0, 0], 2)
    mb.prism(prof, 'w', -13.0, 10.0, MB_, bevel=0.8)
    P0, P1, PV0, PV1 = 55.0, 117.0, 35.0, 52.0
    skins = [
        fillet([(-50, 29), (P0, 29), (P0, 55), (-50, 55)], [1.5, 0, 0, 0], 2),
        [(P0, 29), (P1, 29), (P1, PV0), (P0, PV0)],
        [(P0, PV1), (P1, PV1), (P1, 55), (P0, 55)],
        fillet([(P1, 29), (122, 29), (131, 33), (131, 55), (P1, 55)], [0, 2, 2, 0, 0], 2),
    ]
    for sk in skins:
        mb.prism(sk, 'w', 10.0, 13.0, MB_, bevel=0.5)
    # rounded bolt-carrier housing (the upper is round-bellied, not a slab), except across the port
    for (u0, u1) in ((-48.0, P0 - 1.0), (P1 + 1.0, 124.0)):
        mb.cyl(u0, u1, 15.0, MB_, axis='u', c=(VB, 0.0), seg=32, chamfer=1.2)
    mb.prism([(-13, 55), (13, 55), (11.2, 62), (-11.2, 62)], 'u', -49.0, 131.0, MB_, bevel=0.6)
    pic_rail(mb, -47.0, 129.0, MB_, frame=T(0, 62.0, 0), H=8.5)
    mb.cyl(123.0, 134.0, 16.4, MB_, axis='u', c=(VB, 0.0), seg=32, chamfer=0.8)
    mb.box2((-51.0, 58.5, -5.6), (-49.0, 63.0, 5.6), 'Rubber')                           # charging-handle slot
    # bolt carrier + bolt with extractor visible in the port
    mb.box2((P0, PV0 + 0.4, 0.0), (104.0, PV1 - 0.4, 10.6), MW, bevel=0.6)
    mb.cyl(104.0, P1, 7.4, MW, axis='u', c=(VB, 0.0), seg=18)
    mb.box2((106.0, VB + 2.0, 6.0), (116.0, VB + 5.0, 8.4), MW, bevel=0.3)                  # extractor
    mb.box2((P0 + 6, 46.0, 9.5), (P0 + 16, 50.5, 11.4), MW, bevel=0.4)                      # cam pin
    # ejection-port dust cover (open, hanging) with hinge rod, stiffening rib and latch
    mb.cyl(52.0, 120.0, 1.5, MW, axis='u', c=(33.4, 14.2), seg=10)
    cover = [(13.3, 34.2), (15.3, 34.2), (18.2, 20.0), (16.2, 20.0)]
    mb.prism(cover, 'u', 54.5, 117.5, MB_, bevel=0.4)
    mb.prism([(16.4, 28.0), (18.4, 28.0), (18.8, 25.5), (16.8, 25.5)], 'u', 57.0, 115.0, MB_)
    mb.box2((109.0, 23.0, 16.0), (114.0, 30.0, 19.4), MB_, bevel=0.4)
    # brass deflector bump behind the port
    defl = fillet([(34.0, 12.5), (54.0, 12.5), (52.5, 16.5), (45.0, 22.0), (34.0, 22.0)], [1, 1.5, 3, 3, 2], 3)
    mb.prism(defl, 'v', 36.0, 58.0, MB_, bevel=1.5)
    # round forward-assist housing (fenced) + plunger button with grooves
    fa_c = (50.5, 17.5)
    mb.lathe([(12.0, 0), (12.0, 9.2), (30.0, 9.2), (40.0, 5.5), (42.0, 0)], MB_, seg=28, axis='u', c=fa_c)
    mb.tube(10.5, 13.5, 10.4, 7.6, MB_, axis='u', c=fa_c, seg=28, chamfer=0.5)
    prof = [(-3.0, 0), (-3.0, 6.2), (-1.8, 7.4)]
    for k in range(5):
        u = -0.6 + k * 2.6
        prof += [(u, 7.4), (u + 0.5, 6.6), (u + 1.4, 6.6), (u + 1.9, 7.4)]
    prof += [(12.0, 7.4), (12.0, 0)]
    mb.lathe(prof, MB_, seg=28, axis='u', c=fa_c)


# ------------------------------------------------------------------------------ lower receiver
def lower(mb):
    hw = 12.5
    rear = fillet([(22, 29), (-50, 29), (-50, 56), (-53, 59.5), (-66, 59.5), (-70, 55), (-70, 21),
                   (-74.5, 16), (-72, 11), (-66, 9), (-28, 9), (-24, 11), (22, 11)],
                  [0, 0, 3, 2, 2, 3, 3, 3, 3, 3, 2, 1.5, 0], 2)
    mb.prism(rear, 'w', -hw, hw, MB_, bevel=0.8)
    # receiver-extension ring (buffer tower)
    mb.cyl(-50.0, -68.5, 17.8, MB_, axis='u', c=(VB, 0.0), seg=32, chamfer=1.2)
    mb.tube(-66.0, -68.5, 19.0, 14.0, MB_, axis='u', c=(VB, 0.0), seg=32, chamfer=0.6)
    # magazine well (hollow) with the curved front web
    wall = [(22, 29), (104, 29), (104, 12), (106, -24), (108, -31), (20, -31), (21, -24), (22, 0)]
    for sd in (1, -1):
        mb.prism(wall, 'w', sd * 11.6, sd * 15.2, MB_, bevel=0.6)
    mb.prism([(96, 29), (104, 29), (104, 12), (106, -24), (108, -31), (96, -31)], 'w', -15.2, 15.2, MB_, bevel=0.6)
    mb.prism([(20, -31), (21, -24), (22, 0), (22, 29), (29.8, 29), (29.8, -31)], 'w', -15.2, 15.2, MB_, bevel=0.6)
    # flared magwell lip (sloped outward flare all round)
    side_lip = [(11.6, -34.0), (18.2, -34.0), (18.2, -31.5), (15.2, -25.0), (11.6, -25.0)]
    for sd in (1, -1):
        mb.prism([(sd * x, y) for x, y in side_lip], 'u', 28.0, 98.0, MB_, bevel=0.6)
    mb.prism([(96.0, -34.0), (111.0, -34.0), (111.0, -31.5), (106.5, -25.0), (96.0, -25.0)], 'w', -18.2, 18.2, MB_,
             bevel=0.6)
    mb.prism([(17.0, -34.0), (30.0, -34.0), (30.0, -25.0), (21.0, -25.0), (17.0, -31.5)], 'w', -18.2, 18.2, MB_,
             bevel=0.6)
    # front pivot lugs
    mb.prism(fillet([(104, 29), (124, 29), (126, 25), (124, 17), (118, 12), (104, 12)], [0, 1.5, 3, 3, 2, 0], 2),
             'w', -hw, hw, MB_, bevel=0.8)
    # hinged trigger guard: ears on the receiver, guard bow, hinge roll pin and front detent
    for (u0, u1) in ((19.0, 29.0), (-36.0, -26.0)):
        for sd in (1, -1):
            mb.box2((u0, -2.0, sd * 4.6), (u1, 11.5, sd * 7.6), MB_, bevel=0.6)
    path = fillet([(24.0, 4.0), (24.0, -15.0), (20.0, -20.0), (-24.0, -20.0), (-31.0, -12.0), (-31.5, 3.0)],
                  [0, 4, 3, 6, 4, 0], 3, closed=False)
    mb.sweep([(u, v, 0.0) for u, v in path], rrect(-5.0, -2.4, 5.0, 2.4, 1.2, 3), MB_)
    mb.cyl(-8.0, 8.0, 2.2, MW, axis='w', c=(24.0, 2.5), seg=12)
    mb.cyl(-8.0, 8.0, 1.5, MW, axis='w', c=(-31.0, 0.0), seg=10)
    # selector (left, SAFE) + indicator (right)
    mb.cyl(-hw, -(hw + 2.4), 6.0, MB_, axis='w', c=(-38.0, 19.0), seg=24, chamfer=0.6)
    lever = fillet([(-38, 15.8), (-20, 16.6), (-18.0, 19.0), (-20, 22.0), (-38, 22.2)], [0, 1.5, 1.5, 1.5, 0], 2)
    mb.prism(lever, 'w', -(hw + 2.2), -(hw + 4.6), MB_, bevel=0.5)
    mb.cyl(hw, hw + 1.2, 4.4, MB_, axis='w', c=(-38.0, 19.0), seg=16, chamfer=0.4)
    # bolt-catch paddle (left) with serrations and its roll-pin boss
    mb.cyl(-hw, -(hw + 1.8), 4.6, MB_, axis='w', c=(8.0, 25.0), seg=16, chamfer=0.5)
    catch = fillet([(0.0, 17.5), (15.0, 13.5), (17.0, 24.5), (7.0, 28.5)], 1.8, 3)
    mb.prism(catch, 'w', -hw, -(hw + 3.4), MB_, bevel=0.5)
    for k in range(4):
        mb.box2((2.0 + k * 3.2, 15.6 + k * 0.6, -(hw + 3.9)), (3.2 + k * 3.2, 23.5, -(hw + 3.2)), MB_)
    # magazine-release button with its raised U-shaped fence (right side, open towards the front)
    mc = (18.5, 1.5)
    mb.cyl(hw, hw + 4.0, 5.2, MB_, axis='w', c=mc, seg=20, chamfer=0.8)
    fence = [(mc[0] + 10.5 * math.cos(math.radians(a)), mc[1] + 10.5 * math.sin(math.radians(a)), hw + 1.4)
             for a in range(40, 330, 10)]
    mb.sweep(fence, rrect(-1.6, -1.6, 1.6, 1.6, 0.6, 2), MB_, ref=(0.0, 0.0, 1.0))
    for a in (90.0, 270.0):
        cu = mc[0] + 10.5 * math.cos(math.radians(a))
        cv = mc[1] + 10.5 * math.sin(math.radians(a))
        mb.box2((cu - 6.0, cv - 1.4, hw), (cu + 2.0, cv + 1.4, hw + 3.4), MB_, bevel=0.5)
    # takedown / pivot pin bosses + pins, trigger & hammer pins
    for (u, v) in ((115.0, 21.0), (-45.0, 24.0)):
        for sd in (1, -1):
            mb.cyl(sd * hw, sd * (hw + 1.2), 5.4, MB_, axis='w', c=(u, v), seg=20, chamfer=0.5)
        pin_heads(mb, u, v, hw + 1.2, 3.0, MW, h=0.6, seg=16)
    pin_heads(mb, -4.0, 20.0, hw, 2.0, MW, h=0.5, seg=12)
    pin_heads(mb, -22.0, 21.0, hw, 2.0, MW, h=0.5, seg=12)


# ------------------------------------------------------------------------------ grip, tube, stock
def grip(mb):
    gax = GripAxis(top=(-48.0, 14.0), rake=25.0, length=108.0)

    def hw(t):
        return 13.6 + 0.6 * math.sin(math.pi * min(t, 1.0)) - 0.6 * t

    def hf(t):
        nub = math.exp(-((t - 0.45) / 0.08) ** 2)
        return 20.5 + 3.6 * nub - 1.2 * t

    def hb(t):
        return 24.5 - 2.5 * t - 1.2 * math.exp(-((t - 0.15) / 0.12) ** 2)

    with mb.group() as g:
        grip_loft(mb, gax, 'Polymer_Ribs', hw, hf, hb, steps=24, n=24, p=2.5)
    return gax, hf, hb


def buffer_tube(mb):
    mb.lathe([(-66, 0), (-66, 14.6), (-243, 14.6), (-245, 13.4), (-245, 0)], MB_, seg=28, axis='u', c=(VB, 0.0))
    mb.box2((-241.0, VB - 17.4, -4.0), (-80.0, VB - 13.0, 4.0), MB_, bevel=0.6)
    for k in range(6):                                    # stock position holes (dark recesses)
        u = -110.0 - k * 20.6
        mb.box2((u - 2.6, VB - 17.6, -2.6), (u + 2.6, VB - 16.8, 2.6), 'Rubber')
    mb.prism(star(0.0, VB, 18.2, 2.6, 8), 'u', -70.5, -78.0, MB_, nobevel=True)
    # end plate with sling loop (left side)
    mb.prism(fillet([(-19.0, VB - 17), (19.0, VB - 17), (19.0, VB + 17), (-19.0, VB + 17)], 6.0, 3), 'u',
             -68.0, -70.5, MB_, bevel=0.4)
    mb.prism(fillet([(-19.0, VB - 6), (-28.0, VB - 4), (-28.0, VB + 6), (-19.0, VB + 8)], 2.0, 2), 'u', -68.0, -70.5, MB_)
    wire_loop(mb, (-69.2, VB + 1.0, -30.5), 4.2, 1.2, MW, plane='uw', seg=16)


def stock(mb):
    """Classic M4 6-position stock (extended): round tube housing, angled body whose sides are deeply
    recessed (thin centre web + waffle ribs inside a raised rim), angled toe, sling loop at the bottom
    rear, release lever underneath, ribbed rubber butt plate."""
    uf = -150.0
    ub = U_BUTT + 12.0                     # front face of the butt plate
    top = 62.5
    toe = -42.0
    low = VB - 16.0
    mb.lathe([(uf, 0), (uf, 16.0), (uf - 3.0, 18.6), (ub + 14, 18.6), (ub + 6, 18.0), (ub + 6, 0)], PB,
             seg=32, axis='u', c=(VB + 2.0, 0.0))
    # thin centre web -> deep hollow recess on both sides
    web = fillet([(uf - 22, low + 2), (ub + 12, low + 2), (ub + 12, toe + 10)], [3, 0, 3], 2)
    mb.prism(web, 'w', -3.5, 3.5, PB, bevel=0.8)
    # raised rim along the angled bottom edge, a front post under the housing
    mb.prism(fillet(strut((uf - 16, low - 0.5), (ub + 14, toe + 4.5), 5.0), 2.0, 2), 'w', -14.0, 14.0, PB, bevel=1.2)
    mb.prism(fillet([(uf - 2, low + 6), (uf - 30, low - 4), (uf - 30, low + 6)], 2.0, 2), 'w', -13.0, 13.0, PB,
             bevel=0.8)
    # waffle ribs inside the recess
    def rim(u):
        return low - 0.5 + (u - (uf - 16)) * ((toe + 4.5) - (low - 0.5)) / ((ub + 14) - (uf - 16))
    for u in (-196.0, -228.0, -258.0):
        mb.box2((u - 1.6, rim(u), -12.5), (u + 1.6, low + 2.0, 12.5), PB, bevel=0.5)
    mb.box2((-258.0, 11.0, -12.5), (-199.0, 14.0, 12.5), PB, bevel=0.5)
    # butt block with angled toe
    butt = fillet([(ub + 18, top + 1.5), (ub + 18, toe + 14), (ub + 6, toe), (ub, toe), (ub, top + 1.5)],
                  [2, 6, 3, 0, 0], 2)
    mb.prism(butt, 'w', -19.5, 19.5, PB, bevel=2.5, seg=2)
    # release lever underneath the housing front
    lev = fillet([(uf - 4, VB - 15.5), (uf - 40, VB - 16.5), (uf - 42, VB - 22.0), (uf - 6, VB - 21.5)], 1.5, 2)
    mb.prism(lev, 'w', -4.5, 4.5, PB, bevel=0.8)
    for k in range(4):
        mb.box2((uf - 36 + k * 6.0, VB - 23.0, -4.0), (uf - 34 + k * 6.0, VB - 21.8, 4.0), PB)
    # sling loop at the bottom rear
    mb.box2((ub + 16, toe + 6, -5.5), (ub + 30, toe + 13, 5.5), PB, bevel=1.0)
    wire_loop(mb, (ub + 23, toe + 3.5, 0.0), 7.0, 1.4, MW, plane='uv', arc=(180.0, 360.0), seg=14)
    # ribbed rubber butt plate
    pad = fillet([(ub, top + 3.0), (ub, toe - 1.5), (U_BUTT + 2, toe + 0.5), (U_BUTT, toe + 8),
                  (U_BUTT, top - 4), (U_BUTT + 2, top + 2)], [2, 2, 2, 4, 4, 2], 2)
    mb.prism(pad, 'w', -20.0, 20.0, 'Rubber_Ribs', bevel=2.0, seg=2)
    for k in range(9):
        v = toe + 8 + k * (top - toe - 14) / 8.0
        mb.box2((U_BUTT - 1.2, v - 1.4, -17.5), (U_BUTT + 1.0, v + 1.4, 17.5), 'Rubber_Ribs', bevel=0.5)


# ------------------------------------------------------------------------------ handguard, FSB, barrel
def handguard(mb):
    # slip ring (delta ring) + barrel nut
    mb.lathe([(132.0, 15.0), (132.0, 22.0), (134.0, 24.5), (143.0, 24.5), (147.5, 21.5), (147.5, 15.0)], MB_,
             seg=36, axis='u', c=(VB, 0.0), closed=True)
    mb.prism(star(0.0, VB, 18.6, 1.4, 12), 'u', 128.0, 133.0, MB_, nobevel=True)
    # quad rail body: rounded square tube with oval vents in the corners
    hg = MB()
    outer = rrect(-HG_HALF, VB - HG_HALF, HG_HALF, VB + HG_HALF, 7.0, 3)
    inner = rrect(-HG_HALF + 2.6, VB - HG_HALF + 2.6, HG_HALF - 2.6, VB + HG_HALF - 2.6, 4.4, 3)
    hg.tube_prism(outer, inner, 'u', HG0, HG1, MB_)
    cut = MB()
    for k in range(6):
        u = HG0 + 22.0 + k * 22.5
        for (sv, sw) in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
            M = T(u, VB + sv * 15.5, sw * 15.5) @ Ru(-45.0 * sv * sw) @ Rw(0.0)
            cut.prism(fillet([(-6.0, -2.4), (6.0, -2.4), (6.0, 2.4), (-6.0, 2.4)], 2.3, 3), 'w', -6.0, 6.0, MB_,
                      xf=M @ Ru(90.0) @ Ru(-90.0))
    hg.boolean(cut, mat=MB_)
    mb.merge(hg)
    # rails 12/3/6/9
    pic_rail(mb, HG0 + 1.0, HG1 - 1.0, MB_, frame=rail_frame('top', VB + HG_HALF), H=RAIL_TOP - VB - HG_HALF)
    for side in ('right', 'left'):
        pic_rail(mb, HG0 + 1.0, HG1 - 1.0, MB_, frame=rail_frame(side, HG_HALF, vc=VB), H=6.0)
    pic_rail(mb, HG0 + 1.0, HG1 - 1.0, MB_, frame=rail_frame('bottom', VB - HG_HALF), H=6.0)
    # ribbed ladder covers (left full length, right rear part, bottom rear + front: sockets stay bare)
    ladder_cover(mb, HG0 + 2.0, HG1 - 2.0, PB, rail_frame('left', HG_HALF, vc=VB))
    ladder_cover(mb, HG0 + 2.0, HG0 + 96.0, PB, rail_frame('right', HG_HALF, vc=VB))
    ladder_cover(mb, HG0 + 2.0, HG0 + 64.0, PB, rail_frame('bottom', VB - HG_HALF))
    ladder_cover(mb, HG1 - 22.0, HG1 - 2.0, PB, rail_frame('bottom', VB - HG_HALF))
    # front end cap and screws
    mb.tube_prism(rrect(-HG_HALF - 0.6, VB - HG_HALF - 0.6, HG_HALF + 0.6, VB + HG_HALF + 0.6, 7.4, 3),
                  inner, 'u', HG1 - 5.0, HG1, MB_)
    for su in (HG0 + 8.0, HG1 - 9.0):
        for s in (1, -1):
            hex_screw(mb, (su, VB + 13.5, s * 13.5), 'w', 2.4, 1.2, MB_, sign=s)


def front_sight_base(mb):
    """A2 front sight base: clamp around the barrel, triangular tower, protective ears, bayonet lug,
    sling swivel. The post itself belongs to IronSights."""
    u0, u1 = FSB0, FSB1
    mb.prism(rrect(-12.0, VB - 13.0, 12.0, VB + 13.0, 6.0, 3), 'u', u0, u1, MB_, bevel=1.0)
    tower = fillet([(u0 + 1.0, VB + 10.0), (u1 - 1.0, VB + 10.0), (u1 - 6.0, SIGHT - 9.0), (u0 + 4.0, SIGHT - 9.0)],
                   [3, 3, 2, 2], 2)
    mb.prism(tower, 'w', -7.0, 7.0, MB_, bevel=1.0)
    # ears joined by a bridge at their base
    mb.box2((u0 + 3.5, SIGHT - 10.5, -8.0), (u1 - 5.0, SIGHT - 6.0, 8.0), MB_, bevel=0.8)
    ear = fillet([(u0 + 3.5, SIGHT - 7.0), (u1 - 5.0, SIGHT - 7.0), (u1 - 6.5, SIGHT + 5.0), (u0 + 5.0, SIGHT + 5.0)],
                 [0, 0, 2.5, 2.5], 2)
    for s in (1, -1):
        mb.prism(ear, 'w', s * 4.6, s * 7.8, MB_, bevel=0.5)
    # taper pins
    for u in (u0 + 6.0, u1 - 6.0):
        pin_heads(mb, u, VB + 6.0, 12.0, 1.6, MW, h=0.4, seg=10)
    # bayonet lug and sling swivel
    mb.prism(fillet([(u0 + 6.0, VB - 12.0), (u1 + 6.0, VB - 12.0), (u1 + 6.0, VB - 22.0), (u0 + 14.0, VB - 22.0)],
                    [0, 1.5, 1.5, 2], 2), 'w', -4.5, 4.5, MB_, bevel=0.6)
    mb.box2((u0 - 1.0, VB - 17.0, -6.5), (u0 + 7.0, VB - 12.0, 6.5), MB_, bevel=0.8)
    mb.cyl(-8.0, 8.0, 1.6, MW, axis='w', c=(u0 + 3.0, VB - 15.0), seg=10)
    wire_loop(mb, (u0 + 3.0, VB - 23.0, 0.0), 8.0, 1.4, MW, plane='wv', arc=(-20.0, 200.0), seg=14)


def barrel(mb):
    prof = [(126.0, 0), (126.0, 8.4), (FSB0, 8.4), (FSB0, 9.6), (352.0, 9.6), (352.5, 8.4), (364.0, 8.4),
            (364.5, 9.6), (U_MUZ - 1.5, 9.6), (U_MUZ, 8.4), (U_MUZ, 2.9), (U_MUZ - 14.0, 2.9), (U_MUZ - 14.0, 0)]
    mb.lathe(prof, MD, seg=24, axis='u', c=(VB, 0.0))
    # gas tube from the FSB back into the upper (visible through the handguard vents)
    mb.cyl(124.0, FSB0, 2.4, MW, axis='u', c=(VB + 13.0, 0.0), seg=10)


# ------------------------------------------------------------------------------ moving / separate parts
def charging_handle(mb):
    mb.box2((-56.0, 60.4, -4.2), (40.0, 65.6, 4.2), MB_, bevel=0.5)
    tbar = fillet([(-54.5, -18.5), (-54.5, 18.5), (-57.5, 21.5), (-63.0, 21.5), (-64.5, 16.5),
                   (-64.5, -16.5), (-63.0, -21.5), (-57.5, -21.5)], 1.5, 2)
    mb.prism(tbar, 'v', 60.4, 68.4, MB_, bevel=0.8)
    latch = fillet([(-62.0, -21.0), (-47.0, -21.0), (-47.0, -24.5), (-55.0, -28.0), (-62.0, -28.0)], 1.5, 2)
    mb.prism(latch, 'v', 61.0, 67.5, MB_, bevel=0.6)
    for k in range(4):
        mb.box2((-61.0 + k * 3.0, 67.4, -26.5), (-60.0 + k * 3.0, 68.0, -21.5), MB_)


def trigger(mb):
    cu, cv = 25.0, -2.0
    R0 = math.hypot(cu, cv)
    R1 = R0 + 4.4
    front = [(cu - math.sqrt(R0 * R0 - (v - cv) ** 2), v) for v in [11.0 - 23.5 * i / 10 for i in range(11)]]
    back = [(cu - math.sqrt(R1 * R1 - (v - cv) ** 2), v) for v in [-10.0 + 21.0 * i / 10 for i in range(11)]]
    poly = front + [(front[-1][0] + 2.2, -13.6)] + back + [(-8.0, 12.0), (-8.0, 22.0), (2.0, 22.0)]
    mb.prism(clean_poly(poly), 'w', -3.2, 3.2, MB_, bevel=0.6)


def iron_sights(mb):
    buis_rear(mb, -30.0, RAIL_TOP, SIGHT, MB_)
    # A2 front sight post (4-position detent base) inside the FSB ears
    uc = (FSB0 + FSB1) / 2.0 - 1.0
    mb.cyl(SIGHT - 10.5, SIGHT - 6.5, 3.2, MB_, axis='v', c=(uc, 0.0), seg=16, chamfer=0.4)
    mb.box2((uc - 1.0, SIGHT - 7.0, -1.0), (uc + 1.0, SIGHT, 1.0), MB_, bevel=0.2)


def magazine(mb):
    return box_mag(mb, MAG_SEAT, top_len=65.0, bot_len=62.0, width=22.6, straight=78.0, R=300.0,
                   arc_deg=19.0, mat='Metal_Dark', floor_mat=MB_,
                   ribs=[(-0.62, -0.42, 0.34, 0.95, 0.7), (0.42, 0.62, 0.34, 0.95, 0.7),
                         (-0.12, 0.12, 0.34, 0.95, 0.7)], cart=CART_556, lip=1.5)


# ------------------------------------------------------------------------------ assembly
def build():
    a = Asset(ID, 'weapon', caliber='5.56x45mm NATO', capacity=30)
    body = MB()
    upper(body)
    lower(body)
    gax, hf, hb = grip(body)
    buffer_tube(body)
    stock(body)
    handguard(body)
    front_sight_base(body)
    barrel(body)
    a.mesh(body, 'Body')

    bolt = MB()
    charging_handle(bolt)
    a.mesh(bolt, 'Bolt', origin=(-59.0, 64.0, 0.0))

    trig = MB()
    trigger(trig)
    a.mesh(trig, 'Trigger', origin=(-3.0, 20.0, 0.0))

    sights = MB()
    iron_sights(sights)
    a.mesh(sights, 'IronSights')

    mag = MB()
    magazine(mag)
    a.mesh(mag, 'Mag', origin=(MAG_SEAT[0], MAG_SEAT[1], 0.0))

    a.socket('Socket_Muzzle', (U_MUZ, VB, 0.0))
    a.socket('Socket_Optic', (40.0, RAIL_TOP, 0.0))
    a.socket('Socket_Underbarrel', (HG0 + 0.6 * (HG1 - HG0), VB - HG_HALF - 6.0, 0.0))
    a.socket('Socket_Side', (HG1 - 24.0, VB, HG_HALF + 6.0))
    a.socket('Socket_Mag', (MAG_SEAT[0], MAG_SEAT[1], 0.0))
    t = 0.05 + 0.45 * 0.95
    gp = gax.point(t, front=(hf(t) - hb(t)) / 2.0)
    a.grip_socket('Grip_R', (gp.x, gp.y, 0.0), gax.rake)
    a.socket('Grip_L', (HG0 + 0.5 * (HG1 - HG0), VB, 0.0))
    a.socket('Sight_Iron', (-31.0, SIGHT, 0.0))
    a.socket('Eject', (86.0, 43.5, 13.0))

    a.meta['moving'] = {'Bolt': {'travel': BOLT_TRAVEL, 'note': 'charging handle, pulls straight back'}}
    a.meta['trigger_pivot'] = 'Trigger origin = trigger pin; rotate about local X (Unity X) to pull'
    a.meta['mag_insert'] = [0.0, 1.0, 0.0]
    a.meta['defaultAttachments'] = {'Socket_Muzzle': 'Muzzle_FlashHider'}
    a.meta['ironSightsNote'] = 'IronSights = rear flip-up BUIS + A2 front sight post; the A2 front sight base/ears stay in Body'
    return a
