"""
parts.py - reusable weapon parts built into an MB (gun space, millimetres).

Conventions: u = forward, v = up, w = right. See core.py.
"""
import math

from mathutils import Vector

from .core import (MB, T, Ru, Rw, about, fillet, rrect, circle, arc, clean_poly, lerp, interp, smoothstep)


# --------------------------------------------------------------------------------------
# Picatinny (MIL-STD-1913) rails and clamps
# --------------------------------------------------------------------------------------
RAIL_TOP_HW = 7.8      # half width of the flat top
RAIL_MAX_HW = 10.6     # half width at the widest point of the dovetail (21.2 mm)
RAIL_NECK_HW = 8.4
RAIL_PITCH = 10.0
RAIL_SLOT = 5.2


def rail_frame(side, base, vc=0.0, wc=0.0):
    """Matrix placing a rail/clamp built 'upright' (base at v=0, top facing +v, centred on w=0).
    top:    base = v of the rail base, wc = lateral centre
    bottom: base = v of the rail base (rail hangs downwards)
    right/left: base = |w| of the rail base, vc = vertical centre"""
    if side == 'top':
        return T(0, base, wc)
    if side == 'bottom':
        return T(0, base, wc) @ Ru(180)
    if side == 'right':
        return T(0, vc, base) @ Ru(90)
    if side == 'left':
        return T(0, vc, -base) @ Ru(-90)
    raise ValueError(side)


def pic_rail(mb, u0, u1, mat, frame=None, H=7.0):
    """Picatinny rail from u0 to u1, base at v=0, top surface at v=H (then moved by `frame`).
    Spine + individual cross teeth so the recoil slots are real geometry."""
    tz = H - 3.0
    with mb.no_bevel() as g:
        spine = [(-RAIL_NECK_HW, 0.0), (RAIL_NECK_HW, 0.0), (RAIL_NECK_HW, H - 5.0),
                 (RAIL_MAX_HW - 0.2, tz), (-(RAIL_MAX_HW - 0.2), tz), (-RAIL_NECK_HW, H - 5.0)]
        mb.prism(spine, 'u', u0, u1, mat)
        tooth = [(-(RAIL_MAX_HW - 0.2), tz), (RAIL_MAX_HW - 0.2, tz), (RAIL_MAX_HW, tz + 0.2),
                 (RAIL_TOP_HW, H), (-RAIL_TOP_HW, H), (-RAIL_MAX_HW, tz + 0.2)]
        L = u1 - u0
        tl = RAIL_PITCH - RAIL_SLOT
        n = max(1, int((L - tl) // RAIL_PITCH) + 1)
        start = u0 + (L - ((n - 1) * RAIL_PITCH + tl)) / 2.0
        for i in range(n):
            a = start + i * RAIL_PITCH
            mb.prism(tooth, 'u', a, a + tl, mat)
    if frame is not None:
        mb.transform(g.verts, frame)
    return g.verts


def pic_clamp(mb, u0, u1, mat, frame=None, top=4.0, hw=13.0, nut='right', nut_u=None, bevel=0.6):
    """Clamp sitting ON a rail whose top surface is at v=0: body from v=0 up to `top`,
    jaws wrapping the dovetail down to v=-6. nut: 'right' | 'left' | 'qd' | None."""
    with mb.group() as g:
        mb.prism(rrect(-hw, 0.0, hw, top, min(1.2, top * 0.4)), 'u', u0, u1, mat, bevel=min(bevel, top * 0.3))
        jaw = [(10.8, 0.4), (hw, 0.4), (hw, -6.0), (8.9, -6.0), (8.9, -5.2), (10.9, -3.1)]
        for s in (1, -1):
            mb.prism([(s * x, y) for x, y in jaw], 'u', u0 + 0.6, u1 - 0.6, mat, bevel=min(0.4, bevel))
        nu = nut_u if nut_u is not None else (u0 + u1) / 2.0
        if nut in ('right', 'left'):
            s = 1 if nut == 'right' else -1
            # hex nut on one side, bolt head on the other
            mb.prism(circle(nu, -2.8, 5.0, 6, 30.0), 'w', s * hw, s * (hw + 4.5), mat, bevel=0.4)
            mb.cyl(-s * hw, -s * (hw + 2.0), 4.0, mat, axis='w', c=(nu, -2.8), seg=12, chamfer=0.6)
        elif nut == 'qd':
            # throw lever on the right side
            mb.cyl(hw, hw + 3.0, 5.5, mat, axis='w', c=(nu, -2.6), seg=14, chamfer=0.6)
            lever = fillet([(nu - 4, -6.5), (nu + 4, -6.5), (nu + 26, -3.5), (nu + 27, 0.5), (nu + 20, 1.5), (nu - 4, 1.0)], 1.5, 2)
            mb.prism(lever, 'w', hw + 3.0, hw + 6.0, mat, bevel=0.6)
    if frame is not None:
        mb.transform(g.verts, frame)
    return g.verts


# --------------------------------------------------------------------------------------
# Small hardware
# --------------------------------------------------------------------------------------
def pin_heads(mb, u, v, hw, r, mat, h=0.8, seg=10):
    """Pin/rivet heads on both sides (w = +-hw)."""
    with mb.no_bevel():
        for s in (1, -1):
            mb.cyl(s * hw, s * (hw + h), r, mat, axis='w', c=(u, v), seg=seg, chamfer=min(0.5, h * 0.6))


def star(cx, cy, r, depth, n):
    """Knurled/ridged outline with n ridges."""
    pts = []
    for k in range(2 * n):
        rr = r if k % 2 == 0 else r - depth
        a = math.pi * k / n
        pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    return pts


def ridged_knob(mb, a0, a1, r, mat, axis, c, ridges=16, depth=0.7, cap=True):
    """Turret/knob with ridges parallel to its axis. axis 'v': c=(u, w); 'w': c=(u, v); 'u': c=(w, v)."""
    with mb.no_bevel() as g:
        mb.prism(star(c[0], c[1], r, depth, ridges), axis, a0, a1, mat)
        if cap:
            s = 1 if a1 > a0 else -1
            lc = (c[1], c[0]) if axis == 'u' else (c[0], c[1])
            mb.cyl(a1, a1 + s * 1.2, r - depth - 0.3, mat, axis=axis, c=lc, seg=max(8, ridges), chamfer=0.4)
    return g.verts


def cartridge(mb, rear, case_len, case_r, neck_r, oal, bullet_r, seg=10, pistol=False,
              case_mat='Brass', bullet_mat='Brass'):
    """Cartridge lying along +u with its case head centre at rear=(u, v, w)."""
    u0, v0, w0 = rear
    if pistol:
        prof = [(0, 0), (0, case_r), (0.9, case_r), (1.2, case_r * 0.86), (1.9, case_r * 0.86),
                (2.3, case_r), (case_len, case_r * 0.97), (case_len, 0)]
    else:
        prof = [(0, 0), (0, case_r), (0.9, case_r), (1.3, case_r * 0.83), (2.3, case_r * 0.83),
                (2.8, case_r), (case_len * 0.80, case_r * 0.96), (case_len * 0.87, neck_r),
                (case_len, neck_r), (case_len, 0)]
    mb.lathe([(u0 + a, r) for a, r in prof], case_mat, seg=seg, axis='u', c=(v0, w0))
    bl = oal - case_len
    if pistol:
        bprof = [(case_len - 1.0, 0), (case_len - 1.0, bullet_r), (case_len + bl * 0.30, bullet_r),
                 (case_len + bl * 0.65, bullet_r * 0.80), (case_len + bl * 0.90, bullet_r * 0.45), (oal, 0)]
    else:
        bprof = [(case_len - 1.0, 0), (case_len - 1.0, bullet_r), (case_len + bl * 0.30, bullet_r),
                 (case_len + bl * 0.62, bullet_r * 0.78), (case_len + bl * 0.88, bullet_r * 0.36), (oal, 0)]
    mb.lathe([(u0 + a, r) for a, r in bprof], bullet_mat, seg=seg, axis='u', c=(v0, w0))


# --------------------------------------------------------------------------------------
# Pistol grips
# --------------------------------------------------------------------------------------
class GripAxis:
    """Raked grip axis: top=(u, v) of the axis top, rake = degrees rearward from vertical."""

    def __init__(self, top, rake, length):
        a = math.radians(rake)
        self.top = Vector((top[0], top[1], 0.0))
        self.d = Vector((-math.sin(a), -math.cos(a), 0.0))     # down the grip
        self.F = Vector((math.cos(a), -math.sin(a), 0.0))      # towards the front of the grip
        self.W = Vector((0.0, 0.0, 1.0))                       # right
        self.rake = rake
        self.length = length

    def point(self, t, front=0.0, right=0.0):
        """Point at fraction t along the axis, offset towards the front / right (mm)."""
        return self.top + self.d * (t * self.length) + self.F * front + self.W * right


def grip_loft(mb, axis, mat, hw, hf, hb, steps=18, n=20, p=2.6, t0=0.0, round_bottom=True, blend=0.3):
    """Loft a pistol grip down a GripAxis. hw/hf/hb(t) -> half width / front depth / back depth
    (measured perpendicular to the axis). The top slices are horizontal (so the grip top sits flat
    under the receiver/frame) and blend to perpendicular-to-axis slices by t = blend."""
    rings = []
    alpha = math.radians(axis.rake)

    def ring(t, scale=1.0, extra=0.0):
        c = axis.top + axis.d * (t * axis.length + extra)
        g = alpha * (1.0 - smoothstep(t / blend)) if blend > 0 else 0.0
        a = alpha - g
        Ft = Vector((math.cos(a), -math.sin(a), 0.0))
        k_t = 1.0 / math.cos(g)
        w, f, b = hw(t) * scale, hf(t) * scale * k_t, hb(t) * scale * k_t
        pts = []
        for k in range(n):
            th = 2 * math.pi * k / n
            ct, st = math.cos(th), math.sin(th)
            y = (f if ct >= 0 else b) * math.copysign(abs(ct) ** (2.0 / p), ct)
            x = w * math.copysign(abs(st) ** (2.0 / p), st)
            pts.append(c + Ft * y + axis.W * x)
        return pts

    for i in range(steps + 1):
        rings.append(ring(t0 + (1.0 - t0) * i / steps))
    if round_bottom:
        rings.append(ring(1.0, 0.9, 2.0))
        rings.append(ring(1.0, 0.62, 3.6))
    return mb.loft(rings, mat)


def grip_slices(mb, mat, v_top, v_bot, front_u, back_u, half_w, steps=24, n=24, p=3.0, front_bump=None):
    """Grip lofted from HORIZONTAL slices (constant v). front_u/back_u/half_w(v) give the outline;
    front_bump(t) adds finger grooves along the front strap (t = 0 top .. 1 bottom)."""
    rings = []
    for i in range(steps + 1):
        t = i / steps
        v = v_top + (v_bot - v_top) * t
        uf, ub, hw = front_u(v), back_u(v), half_w(v)
        if front_bump:
            uf += front_bump(t)
        uc, hd = (uf + ub) / 2.0, (uf - ub) / 2.0
        ring = []
        for k in range(n):
            th = 2 * math.pi * k / n
            ct, st = math.cos(th), math.sin(th)
            ring.append((uc + hd * math.copysign(abs(ct) ** (2.0 / p), ct), v,
                         hw * math.copysign(abs(st) ** (2.0 / p), st)))
        rings.append(ring)
    return mb.loft(rings, mat)


def hex_screw(mb, center, axis, r, h, mat, sign=1):
    """Socket-head screw standing on a surface: axis 'u'|'v'|'w', head grows towards +sign.
    Hex socket = real recess (outer 12-gon ring around an inner hex)."""
    c0, c1, c2 = center
    a0 = {'u': c0, 'v': c1, 'w': c2}[axis]
    cc = {'u': (c2, c1), 'v': (c0, c2), 'w': (c0, c1)}[axis]
    outer = circle(cc[0], cc[1], r, 12)
    inner = []
    hexr = r * 0.48
    for k in range(12):
        a = math.pi * 2 * (k // 2) / 6.0
        b2 = math.pi * 2 * ((k // 2) + 1) / 6.0
        f = (k % 2) * 0.5
        pa = (math.cos(a) * (1 - f) + math.cos(b2) * f, math.sin(a) * (1 - f) + math.sin(b2) * f)
        inner.append((cc[0] + hexr * pa[0], cc[1] + hexr * pa[1]))
    top = a0 + sign * h
    with mb.no_bevel() as g:
        mb.tube_prism(outer, inner, axis, a0, top, mat)
        mb.prism(circle(cc[0], cc[1], hexr * 1.05, 6), axis, a0, top - sign * h * 0.55, mat)
    return g.verts


def ladder_cover(mb, u0, u1, mat, frame, H=6.0, pitch=7.0):
    """Ribbed ladder-style rail cover over a Picatinny rail (built upright: rail top at v=H)."""
    with mb.no_bevel() as g:
        mb.prism(rrect(-12.4, H - 3.2, 12.4, H + 1.6, 1.4, 2), 'u', u0, u1, mat, bevel=0.4)
        n = int((u1 - u0 - 3.0) // pitch)
        s0 = u0 + ((u1 - u0) - (n - 1) * pitch) / 2.0
        for i in range(n):
            a = s0 + i * pitch
            mb.box2((a - 1.6, H + 1.4, -11.0), (a + 1.6, H + 3.4, 11.0), mat, bevel=0.5)
        mb.box2((u0, H - 3.2, -12.4), (u0 + 4.0, H + 3.6, 12.4), mat, bevel=0.6)
    mb.transform(g.verts, frame)
    return g.verts


def wire_loop(mb, center, radius, wire_r, mat, plane='wv', arc=(0.0, 360.0), seg=20):
    """Round wire loop (sling loop / swivel). plane 'wv' (perpendicular to the bore), 'uv' (side plane)
    or 'uw' (horizontal)."""
    pts = []
    a0, a1 = arc
    closed = abs((a1 - a0) - 360.0) < 1e-6
    n = seg if closed else seg
    for k in range(n + (0 if closed else 1)):
        a = math.radians(a0 + (a1 - a0) * k / n)
        x, y = radius * math.cos(a), radius * math.sin(a)
        if plane == 'wv':
            pts.append((center[0], center[1] + y, center[2] + x))
        elif plane == 'uv':
            pts.append((center[0] + x, center[1] + y, center[2]))
        else:
            pts.append((center[0] + x, center[1], center[2] + y))
    sec = [(wire_r * math.cos(2 * math.pi * k / 8), wire_r * math.sin(2 * math.pi * k / 8)) for k in range(8)]
    ref = {'wv': (1.0, 0.0, 0.0), 'uv': (0.0, 0.0, 1.0), 'uw': (0.0, 1.0, 0.0)}[plane]
    with mb.no_bevel() as g:
        mb.sweep(pts, sec, mat, ref=ref, closed=closed, caps=(not closed, not closed))
    return g.verts


def strut(a, b, half):
    """Quad polygon of a straight bar of half-thickness `half` from 2D point a to b."""
    A, B = Vector(a), Vector(b)
    d = (B - A).normalized()
    n = Vector((-d.y, d.x)) * half
    return [tuple(A + n), tuple(B + n), tuple(B - n), tuple(A - n)]


def carbine_stock(mb, *, u_front, u_butt, v_axis, top, toe, mat, pad_mat='Rubber', sleeve_hw=17.5,
                  web_hw=10.0, butt_hw=19.0, pad=11.0, lever_mat=None):
    """Collapsible carbine stock riding on a buffer tube centred at v_axis: wide sleeve around the
    tube, thin recessed web with a reinforced lower edge, wide butt and a rubber pad.
    u_front = front face of the sleeve, u_butt = rear face of the pad (end of the weapon)."""
    ub = u_butt + pad                      # rear face of the stock body
    low = v_axis - 20.0                    # sleeve bottom
    mb.prism(rrect(-sleeve_hw, low, sleeve_hw, top, 8.0, 3), 'u', u_front, ub + 8.0, mat, bevel=1.5)
    butt = fillet([(ub + 16, top + 1.5), (ub + 16, toe + 12), (ub + 4, toe), (ub, toe), (ub, top + 1.5)],
                  [2, 8, 3, 0, 0], 2)
    mb.prism(butt, 'w', -butt_hw, butt_hw, mat, bevel=2.5, seg=2)
    web = fillet([(u_front - 22, low + 4), (ub + 12, low + 4), (ub + 12, toe + 8)], [3, 0, 3], 2)
    mb.prism(web, 'w', -web_hw, web_hw, mat, bevel=1.2)
    mb.prism(fillet(strut((u_front - 18, low + 1.5), (ub + 13, toe + 4), 4.2), 1.5, 2), 'w',
             -(web_hw + 3.0), web_hw + 3.0, mat, bevel=1.2)
    # sling slot (dark recess) in the web near the butt
    mb.prism(rrect(ub + 20, toe + 30, ub + 30, low - 6, 4.0, 2), 'w', -(web_hw + 0.4), web_hw + 0.4, pad_mat)
    # adjustment latch lever under the front of the sleeve
    mb.box2((u_front - 34.0, low - 5.5, -4.5), (u_front - 6.0, low + 1.0, 4.5), lever_mat or mat, bevel=1.2)
    # butt pad
    padp = fillet([(ub, top + 3.0), (ub, toe - 1.5), (u_butt + 2, toe + 0.5), (u_butt, toe + 8),
                   (u_butt, top - 4), (u_butt + 2, top + 2)], [2, 2, 2, 4, 4, 2], 2)
    mb.prism(padp, 'w', -(butt_hw + 0.5), butt_hw + 0.5, pad_mat, bevel=2.5, seg=2)


# --------------------------------------------------------------------------------------
# Box magazines (straight, curved, raked)
# --------------------------------------------------------------------------------------
class MagShape:
    """Centre line of a magazine hanging from its seat (top centre) in mag-local 2D
    coordinates (x forward, y up); straight part then an arc curving forward."""

    def __init__(self, top_len, bot_len, straight, R=0.0, arc_deg=0.0):
        self.top_len, self.bot_len = top_len, bot_len
        self.straight, self.R = straight, R
        self.L = straight + (R * math.radians(arc_deg) if R > 0 else 0.0)

    def cl(self, s):
        if self.R <= 0 or s <= self.straight:
            return Vector((0.0, -s)), Vector((1.0, 0.0))
        ph = (s - self.straight) / self.R
        c = Vector((self.R - self.R * math.cos(ph), -self.straight - self.R * math.sin(ph)))
        return c, Vector((math.cos(ph), math.sin(ph)))

    def h(self, s):
        t = max(0.0, min(1.0, s / self.L))
        return lerp(self.top_len, self.bot_len, t) / 2.0

    def samples(self, s0, s1, n_arc=12):
        ss = {s0, s1}
        if self.R > 0:
            for k in range(n_arc + 1):
                s = self.straight + (self.L - self.straight) * k / n_arc
                if s0 < s < s1:
                    ss.add(s)
            if s0 < self.straight < s1:
                ss.add(self.straight)
        return sorted(ss)

    def band(self, f0, f1, s0, s1, grow=0.0, n_arc=12):
        """Polygon between fractions f0..f1 of the half depth (-1 rear, +1 front), s0..s1 (mm)."""
        ss = self.samples(s0, s1, n_arc)
        a = []
        b = []
        for s in ss:
            c, F = self.cl(s)
            hh = self.h(min(s, self.L)) + grow
            a.append(tuple(c + F * (hh * f1)))
            b.append(tuple(c + F * (hh * f0)))
        return a + b[::-1]


def box_mag(mb, seat, *, top_len, bot_len, width, straight, R=0.0, arc_deg=0.0, rake=0.0,
            mat='Metal_Dark', floor_mat=None, floor_h=7.0, ribs=(), rib_mat=None,
            cart=None, bevel=1.0, front_lug=None, catch_notch=True, cells=None, lip=0.0, n_arc=12):
    """Build a magazine whose top centre (seat) is at gun (u, v). Returns the MagShape.
    ribs: [(f0, f1, s0, s1, height)] with s0/s1 as fractions of the length.
    cart: dict(case_len, case_r, neck_r, oal, bullet_r, pistol, stagger)."""
    shape = MagShape(top_len, bot_len, straight, R, arc_deg)
    L = shape.L
    floor_mat = floor_mat or mat
    rib_mat = rib_mat or mat
    hw = width / 2.0
    with mb.group() as g:
        body = shape.band(-1.0, 1.0, 0.0, L, n_arc=n_arc)
        mb.prism(body, 'w', -hw, hw, mat, bevel=bevel)
        # floor plate: a little larger than the body
        fp = shape.band(-1.0, 1.0, L - floor_h, L + 1.6, grow=1.6, n_arc=n_arc)
        mb.prism(fp, 'w', -(hw + 1.2), hw + 1.2, floor_mat, bevel=1.0)
        for (f0, f1, s0, s1, hgt) in ribs:
            poly = shape.band(f0, f1, s0 * L, s1 * L, n_arc=n_arc)
            for s in (1, -1):
                mb.prism(poly, 'w', s * (hw - 0.4), s * (hw + hgt), rib_mat, bevel=min(0.5, hgt * 0.6), nobevel=True)
        if cells:
            # raised grid of ribs -> recessed rectangular "waffle" cells on both sides
            rows, cols, rw, hgt, s_lo, s_hi = cells
            for i in range(rows + 1):
                sc = (s_lo + (s_hi - s_lo) * i / rows) * L
                poly = shape.band(-0.9, 0.9, sc - rw / 2, sc + rw / 2, n_arc=2)
                for s in (1, -1):
                    mb.prism(poly, 'w', s * (hw - 0.4), s * (hw + hgt), rib_mat, bevel=0.3, nobevel=True)
            for j in range(cols + 1):
                fc = -0.9 + 1.8 * j / cols
                fw = rw / max(shape.h(0.5 * L), 1.0) / 2.0
                poly = shape.band(fc - fw, fc + fw, s_lo * L, s_hi * L, n_arc=n_arc)
                for s in (1, -1):
                    mb.prism(poly, 'w', s * (hw - 0.4), s * (hw + hgt), rib_mat, bevel=0.3, nobevel=True)
        if lip > 0:
            # base-plate lip protruding at the front
            mb.prism(shape.band(0.55, 1.0, L - 2.0, L + 1.6, grow=1.6 + lip), 'w', -(hw + 1.2), hw + 1.2,
                     floor_mat, bevel=0.8)
        if front_lug:
            # locking lug at the top front (AK / M14 style)
            lw, lh, ld = front_lug
            c, F = shape.cl(0.0)
            x0 = top_len / 2.0
            mb.prism(fillet([(x0 - 1, -lh), (x0 + ld, -lh * 0.6), (x0 + ld, 0), (x0 - 1, 0)], 1.0, 2),
                     'w', -lw / 2, lw / 2, mat, bevel=0.5)
        if catch_notch:
            # small catch block on the rear wall (reads as the mag catch notch)
            x0 = -top_len / 2.0
            mb.prism(rrect(x0 - 2.2, -24.0, x0 + 0.5, -14.0, 0.8), 'w', -hw * 0.5, hw * 0.5, mat, bevel=0.3)
    M = T(seat[0], seat[1], 0.0) @ Rw(-rake)
    mb.transform(g.verts, M)
    if cart:
        # top round, horizontal, staggered to one side
        top_rear_local = Vector((-top_len / 2.0 + 2.2, cart['case_r'] * 0.85, 0.0))
        p = M @ top_rear_local
        stagger = cart.get('stagger', hw - cart['case_r'] - 1.2)
        cartridge(mb, (p.x, p.y, stagger), cart['case_len'], cart['case_r'], cart['neck_r'],
                  cart['oal'], cart['bullet_r'], seg=cart.get('seg', 10), pistol=cart.get('pistol', False))
    return shape


# Cartridge data (mm)
CART_556 = dict(case_len=44.7, case_r=4.8, neck_r=3.2, oal=57.4, bullet_r=2.85)
CART_545 = dict(case_len=39.8, case_r=5.0, neck_r=3.1, oal=57.0, bullet_r=2.8)
CART_762 = dict(case_len=51.2, case_r=5.9, neck_r=4.3, oal=71.1, bullet_r=3.9)
CART_9MM = dict(case_len=19.2, case_r=4.95, neck_r=4.8, oal=29.7, bullet_r=4.5, pistol=True)


# --------------------------------------------------------------------------------------
# Iron sights
# --------------------------------------------------------------------------------------
def buis_rear(mb, uc, rail_top, line_v, mat, aperture_r=1.4, knob=True):
    """Folding rear sight, deployed, clamped on a top rail (rail top surface at v=rail_top).
    Aperture centre at (uc, line_v)."""
    with mb.group() as g:
        pic_clamp(mb, uc - 12, uc + 12, mat, frame=T(0, rail_top, 0), top=5.0, hw=12.0, nut=None, bevel=0.0)
        mb.cyl(-12.0, -14.0, 3.6, mat, axis='w', c=(uc, rail_top - 2.8), seg=8)          # clamp screw head
        bt = rail_top + 5.0
        mb.box2((uc - 9, bt - 0.5, -9.5), (uc + 6, bt + 4.0, 9.5), mat, bevel=0.8)
        # protective ears
        ear = fillet([(uc - 8, bt), (uc + 5, bt), (uc + 2.5, line_v + 6.5), (uc - 5.5, line_v + 6.5)],
                     [0, 0, 2.5, 2.5], 2)
        for s in (1, -1):
            mb.prism(ear, 'w', s * 7.0, s * 9.5, mat, bevel=0.4)
        # aperture disc on a stem
        mb.box2((uc - 2.6, bt, -3.2), (uc + 0.6, line_v - 4.0, 3.2), mat, bevel=0.5)
        mb.tube(uc - 2.6, uc + 0.6, 5.6, aperture_r, mat, axis='u', c=(line_v, 0.0), seg=12)
        if knob:
            ridged_knob(mb, 9.5, 14.5, 4.6, mat, 'w', (uc - 2.0, bt + 1.5), ridges=8, depth=0.5)
    return g.verts


def buis_front(mb, uc, rail_top, line_v, mat):
    """Folding front sight, deployed, clamped on a top rail; post tip at (uc, line_v)."""
    with mb.group() as g:
        pic_clamp(mb, uc - 10, uc + 10, mat, frame=T(0, rail_top, 0), top=5.0, hw=12.0, nut=None, bevel=0.0)
        mb.cyl(-12.0, -14.0, 3.6, mat, axis='w', c=(uc, rail_top - 2.8), seg=8)          # clamp screw head
        bt = rail_top + 5.0
        mb.box2((uc - 8, bt - 0.5, -9.0), (uc + 8, bt + 5.0, 9.0), mat, bevel=0.8)
        wing = fillet([(uc - 7.5, bt), (uc + 7.5, bt), (uc + 4.5, line_v + 3.0), (uc - 4.5, line_v + 3.0)],
                      [0, 0, 2.0, 2.0], 2)
        for s in (1, -1):
            mb.prism(wing, 'w', s * 6.0, s * 8.5, mat, bevel=0.4)
        # post with a round base
        mb.cyl(bt + 4.5, bt + 11.0, 3.0, mat, axis='v', c=(uc, 0.0), seg=10, chamfer=0.5)
        mb.box2((uc - 1.1, bt + 10.0, -1.0), (uc + 1.1, line_v, 1.0), mat, bevel=0.2)
    return g.verts
