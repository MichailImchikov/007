"""
core.py - geometry/material/asset helpers shared by all weapon and attachment builders.

MODELLING SPACE ("gun space", millimetres)
    u = forward (towards the muzzle), v = up, w = towards the weapon's RIGHT side (ejection side).
    Origin = the asset's origin (weapons: centre of the trigger blade's front face).
    (u, v, w) is right-handed (u x v = w).

BLENDER SPACE (metres, what gets exported)
    forward = -Y, up = +Z, right = -X     ->     x = -w, y = -u, z = v
    With the project's FBX settings Unity receives Unity(x, y, z) = Blender(-x, z, -y) = gun(w, v, u).

Every builder accumulates bmesh geometry in gun space (mm) inside an MB ("mesh builder");
MB.to_object() converts it to a Blender mesh object (metres) with the origin where requested.
"""
import math

import bpy
import bmesh
from mathutils import Vector, Matrix

MM = 0.001
DEG = math.pi / 180.0

# Round-part resolution: lathe/cylinder segment counts >= 10 are multiplied by this (rounded to a
# multiple of 4, capped at 64). Counts below 10 (octagons, hex nuts ...) are left exact.
SEG_SCALE = 1.0


def seg_count(seg, exact=False):
    if exact or seg < 10:
        return seg
    return max(8, min(48, int(round(seg * SEG_SCALE / 4.0)) * 4))

# gun space (u, v, w) -> Blender (x, y, z)
G2B3 = Matrix(((0.0, 0.0, -1.0),
               (-1.0, 0.0, 0.0),
               (0.0, 1.0, 0.0)))
G2B = G2B3.to_4x4()


def g2b(p):
    """gun-space point (mm) -> Blender Vector (m)."""
    return (G2B3 @ Vector(p)) * MM


def b2unity(v):
    """Blender vector -> Unity vector (list)."""
    return [-v[0], v[2], -v[1]]


# --------------------------------------------------------------------------------------
# Materials (names are mapped by the Unity side - keep them exact)
# --------------------------------------------------------------------------------------
MATERIALS = {
    #  name            sRGB hex   metallic roughness  extras
    'Metal_Black':   ('1c1c1e', 0.8, 0.45, {}),
    'Metal_Dark':    ('2b2d30', 1.0, 0.35, {}),
    'Metal_Worn':    ('5a5d61', 1.0, 0.30, {}),
    'Polymer_Black': ('1a1a1a', 0.0, 0.60, {}),
    'Polymer_Tan':   ('8b7355', 0.0, 0.60, {}),
    'Polymer_OD':    ('4b5320', 0.0, 0.60, {}),
    'Wood':          ('6b3e1f', 0.0, 0.50, {}),
    'Rubber':        ('111111', 0.0, 0.90, {}),
    'Glass_Lens':    ('3a5560', 0.0, 0.05, {'alpha': 0.3}),
    'Reticle_Red':   ('ff1a1a', 0.0, 0.50, {'emission': 'ff1a1a', 'strength': 6.0}),
    'Emitter_Laser': ('ff2020', 0.0, 0.40, {'emission': 'ff2020', 'strength': 6.0}),
    'Emitter_Light': ('fff0d2', 0.0, 0.30, {'emission': 'fff0d2', 'strength': 8.0}),
    'Brass':         ('b5a642', 1.0, 0.30, {}),
    # v2 source materials (baked into the per-asset atlas; see texturing.py)
    'Metal_Nitride':   ('55585c', 0.85, 0.48, {}),
    'Metal_Knurl':     ('1e1f21', 0.80, 0.45, {}),
    'Polymer_Stipple': ('1a1a1a', 0.0, 0.70, {}),
    'Polymer_Ribs':    ('1a1a1a', 0.0, 0.62, {}),
    'Polymer_Checker': ('1a1a1a', 0.0, 0.62, {}),
    'Rubber_Ribs':     ('111111', 0.0, 0.90, {}),
    'Paint_White':     ('e6e6dc', 0.0, 0.50, {}),
}

# Special materials keep their own (untextured) material in the FBX; everything else is baked.
SPECIAL_MATERIALS = ('Glass_Lens', 'Reticle_Red', 'Emitter_Laser', 'Emitter_Light')

# texturing.py installs a factory that builds the procedural (bakeable) version of each material
MATERIAL_FACTORY = None


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_to_linear(h):
    return tuple(srgb_to_linear(int(h[i:i + 2], 16) / 255.0) for i in (0, 2, 4))


def get_material(name):
    """Create (once) a plain Principled BSDF material with the exact project name."""
    mat = bpy.data.materials.get(name)
    if mat is not None:
        return mat
    if MATERIAL_FACTORY is not None:
        mat = MATERIAL_FACTORY(name)
        if mat is not None:
            return mat
    hexcol, metallic, rough, extra = MATERIALS[name]
    col = hex_to_linear(hexcol)
    mat = bpy.data.materials.new(name)
    if mat.node_tree is None:           # older Blender versions
        mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*col, 1.0)
    bsdf.inputs['Metallic'].default_value = metallic
    bsdf.inputs['Roughness'].default_value = rough
    if 'alpha' in extra:
        bsdf.inputs['Alpha'].default_value = extra['alpha']
    if 'emission' in extra:
        ecol = hex_to_linear(extra['emission'])
        bsdf.inputs['Emission Color'].default_value = (*ecol, 1.0)
        bsdf.inputs['Emission Strength'].default_value = extra['strength']
    # viewport display values (Solid / Workbench)
    mat.diffuse_color = (*col, extra.get('alpha', 1.0))
    mat.metallic = metallic
    mat.roughness = rough
    return mat


# --------------------------------------------------------------------------------------
# 2D helpers (all in mm, plain tuples)
# --------------------------------------------------------------------------------------
def clean_poly(pts, eps=1e-5, closed=True):
    out = []
    for p in pts:
        p = (float(p[0]), float(p[1]))
        if out and abs(out[-1][0] - p[0]) < eps and abs(out[-1][1] - p[1]) < eps:
            continue
        out.append(p)
    if closed and len(out) > 2 and abs(out[0][0] - out[-1][0]) < eps and abs(out[0][1] - out[-1][1]) < eps:
        out.pop()
    return out


def fillet(pts, r, segs=3, closed=True):
    """Round the corners of a polyline/polygon. r = radius or per-vertex list (0 = keep sharp)."""
    pts = clean_poly(pts, closed=closed)
    n = len(pts)
    rs = list(r) if isinstance(r, (list, tuple)) else [r] * n
    out = []
    for i in range(n):
        ri = rs[i]
        if ri <= 0 or (not closed and (i == 0 or i == n - 1)):
            out.append(pts[i])
            continue
        p = Vector(pts[i])
        a = Vector(pts[i - 1])
        b = Vector(pts[(i + 1) % n])
        d1, d2 = a - p, b - p
        l1, l2 = d1.length, d2.length
        if l1 < 1e-9 or l2 < 1e-9:
            out.append(pts[i])
            continue
        d1 /= l1
        d2 /= l2
        ang = math.acos(max(-1.0, min(1.0, d1.dot(d2))))
        if ang < 1e-3 or ang > math.pi - 1e-3:
            out.append(pts[i])
            continue
        t = min(ri / math.tan(ang / 2), 0.49 * l1, 0.49 * l2)
        rr = t * math.tan(ang / 2)
        p1 = p + d1 * t
        p2 = p + d2 * t
        c = p + (d1 + d2).normalized() * (rr / math.sin(ang / 2))
        a1 = math.atan2(p1.y - c.y, p1.x - c.x)
        a2 = math.atan2(p2.y - c.y, p2.x - c.x)
        da = a2 - a1
        while da > math.pi:
            da -= 2 * math.pi
        while da < -math.pi:
            da += 2 * math.pi
        for k in range(segs + 1):
            aa = a1 + da * k / segs
            out.append((c.x + rr * math.cos(aa), c.y + rr * math.sin(aa)))
    return out


def arc(cx, cy, r, a0, a1, n):
    """Arc points (degrees), both ends included, n segments."""
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * k / n)),
             cy + r * math.sin(math.radians(a0 + (a1 - a0) * k / n))) for k in range(n + 1)]


def circle(cx, cy, r, n, a0=0.0):
    return [(cx + r * math.cos(math.radians(a0) + 2 * math.pi * k / n),
             cy + r * math.sin(math.radians(a0) + 2 * math.pi * k / n)) for k in range(n)]


def rrect(x0, y0, x1, y1, r, segs=3):
    return fillet([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], r, segs)


def annulus_sector(cx, cy, r_in, r_out, a0, a1, n):
    """Closed polygon of a ring sector (degrees)."""
    return arc(cx, cy, r_out, a0, a1, n) + arc(cx, cy, r_in, a1, a0, n)


def superellipse(cx, cy, a, b, p, n, a0=0.0):
    out = []
    for k in range(n):
        t = math.radians(a0) + 2 * math.pi * k / n
        ct, st = math.cos(t), math.sin(t)
        out.append((cx + a * math.copysign(abs(ct) ** (2.0 / p), ct),
                    cy + b * math.copysign(abs(st) ** (2.0 / p), st)))
    return out


def bez3(p0, p1, p2, p3, n, start=True):
    out = []
    for k in range(0 if start else 1, n + 1):
        t = k / n
        mt = 1 - t
        out.append((mt ** 3 * p0[0] + 3 * mt * mt * t * p1[0] + 3 * mt * t * t * p2[0] + t ** 3 * p3[0],
                    mt ** 3 * p0[1] + 3 * mt * mt * t * p1[1] + 3 * mt * t * t * p2[1] + t ** 3 * p3[1]))
    return out


def catmull(pts, n=4, closed=False):
    """Catmull-Rom spline through pts (n samples per segment)."""
    P = [Vector(p) for p in pts]
    m = len(P)
    out = []
    segs = m if closed else m - 1
    for i in range(segs):
        p0 = P[(i - 1) % m] if (closed or i > 0) else P[0] * 2 - P[1]
        p1 = P[i]
        p2 = P[(i + 1) % m]
        p3 = P[(i + 2) % m] if (closed or i + 2 < m) else P[-1] * 2 - P[-2]
        for k in range(n):
            t = k / n
            t2, t3 = t * t, t * t * t
            q = 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)
            out.append((q.x, q.y))
    if not closed:
        out.append((P[-1].x, P[-1].y))
    return out


def lerp(a, b, t):
    return a + (b - a) * t


def smoothstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def interp(table, x):
    """Piecewise-linear lookup in [(x, y), ...] sorted by x."""
    if x <= table[0][0]:
        return table[0][1]
    for (x0, y0), (x1, y1) in zip(table, table[1:]):
        if x <= x1:
            t = (x - x0) / (x1 - x0) if x1 != x0 else 0.0
            return y0 + (y1 - y0) * t
    return table[-1][1]


# --------------------------------------------------------------------------------------
# 4x4 helpers in gun space (Vector slots: x=u, y=v, z=w)
# --------------------------------------------------------------------------------------
def T(u=0.0, v=0.0, w=0.0):
    return Matrix.Translation((u, v, w))


def Ru(deg):
    """rotation about the u (bore) axis; +90 turns 'up' (v) into 'right' (w)."""
    return Matrix.Rotation(math.radians(deg), 4, 'X')


def Rv(deg):
    return Matrix.Rotation(math.radians(deg), 4, 'Y')


def Rw(deg):
    """rotation about the w axis (in the side-view plane); +deg turns forward (u) towards up (v)."""
    return Matrix.Rotation(math.radians(deg), 4, 'Z')


def about(M, p):
    """apply M about pivot p."""
    return T(*p) @ M @ T(-p[0], -p[1], -p[2])


# --------------------------------------------------------------------------------------
# Mesh builder
# --------------------------------------------------------------------------------------
class _Group:
    def __init__(self, mb):
        self.mb = mb
        self.verts = []

    def __enter__(self):
        self.mb._groups.append(self)
        return self

    def __exit__(self, *exc):
        self.mb._groups = [g for g in self.mb._groups if g is not self]


class MB:
    """Accumulates closed primitives (gun space, mm) into one bmesh with material slots."""

    def __init__(self):
        self.bm = bmesh.new()
        self.mats = []
        self._groups = []
        self._nb()

    # ---------------------------------------------------------------- bookkeeping
    def _nb(self):
        """Per-vertex int layer 'nobevel' (1 = excluded from the global bevel modifier)."""
        lay = self.bm.verts.layers.int.get('nobevel')
        return lay if lay is not None else self.bm.verts.layers.int.new('nobevel')

    def tag_nobevel(self, verts):
        lay = self._nb()
        for v in verts:
            if v.is_valid:
                v[lay] = 1

    def no_bevel(self):
        """with mb.no_bevel(): ... -> everything built inside is excluded from the bevel modifier."""
        mb = self

        class _NB(_Group):
            def __exit__(self, *exc):
                _Group.__exit__(self, *exc)
                mb.tag_nobevel(self.verts)
        return _NB(self)

    def mi(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def group(self):
        """with mb.group() as g: ... -> g.verts holds every vertex created inside."""
        return _Group(self)

    def _component(self, seeds):
        seen = set()
        stack = [v for v in seeds if v.is_valid]
        while stack:
            v = stack.pop()
            if v in seen:
                continue
            seen.add(v)
            for e in v.link_edges:
                o = e.other_vert(v)
                if o not in seen:
                    stack.append(o)
        return list(seen)

    def _finish(self, seeds, xf):
        verts = self._component(seeds)
        if xf is not None:
            bmesh.ops.transform(self.bm, matrix=xf, verts=verts)
        for g in self._groups:
            g.verts.extend(verts)
        return verts

    def transform(self, verts, M):
        vs = [v for v in set(verts) if v.is_valid]
        bmesh.ops.transform(self.bm, matrix=M, verts=vs)

    def _bevel(self, edges, seeds, bevel, seg, m):
        if bevel <= 0:
            return seeds
        res = bmesh.ops.bevel(self.bm, geom=list(edges), offset=bevel, offset_type='OFFSET',
                              segments=seg, profile=0.5, affect='EDGES', clamp_overlap=True,
                              material=m)
        return [v for v in seeds if v.is_valid] + list(res['verts'])

    # ---------------------------------------------------------------- primitives
    @staticmethod
    def _P(axis, p, a):
        if axis == 'w':      # side profile (u, v) extruded along w
            return (p[0], p[1], a)
        if axis == 'v':      # top profile (u, w) extruded along v
            return (p[0], a, p[1])
        return (a, p[1], p[0])   # axis 'u': front profile (w, v) extruded along u

    def prism(self, poly, axis, a0, a1, mat, bevel=0.0, seg=1, xf=None, nobevel=False):
        """Extrude a 2D polygon. axis 'w': poly=(u,v); 'v': poly=(u,w); 'u': poly=(w,v)."""
        if nobevel:
            with self.no_bevel() as g:
                self.prism(poly, axis, a0, a1, mat, bevel, seg, xf)
            return g.verts
        poly = clean_poly(poly)
        n = len(poly)
        if n < 3 or abs(a1 - a0) < 1e-6:
            return []
        bm = self.bm
        lo = [bm.verts.new(self._P(axis, p, a0)) for p in poly]
        hi = [bm.verts.new(self._P(axis, p, a1)) for p in poly]
        m = self.mi(mat)
        f0 = bm.faces.new(lo[::-1])
        f1 = bm.faces.new(hi)
        faces = [f0, f1]
        for i in range(n):
            j = (i + 1) % n
            faces.append(bm.faces.new((lo[i], lo[j], hi[j], hi[i])))
        for f in faces:
            f.material_index = m
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        seeds = self._bevel(list(f0.edges) + list(f1.edges), lo + hi, bevel, seg, m)
        return self._finish(seeds, xf)

    def box(self, c, s, mat, bevel=0.0, seg=1, xf=None):
        """Axis aligned box, centre c=(u,v,w), size s=(du,dv,dw)."""
        res = bmesh.ops.create_cube(self.bm, size=1.0)
        verts = res['verts']
        for v in verts:
            v.co = Vector((c[0] + v.co.x * s[0], c[1] + v.co.y * s[1], c[2] + v.co.z * s[2]))
        m = self.mi(mat)
        for f in set(f for v in verts for f in v.link_faces):
            f.material_index = m
        edges = set(e for v in verts for e in v.link_edges)
        seeds = self._bevel(edges, list(verts), bevel, seg, m)
        return self._finish(seeds, xf)

    def box2(self, lo, hi, mat, bevel=0.0, seg=1, xf=None):
        """Box from min/max corners."""
        c = [(a + b) / 2 for a, b in zip(lo, hi)]
        s = [abs(b - a) for a, b in zip(lo, hi)]
        return self.box(c, s, mat, bevel, seg, xf)

    def lathe(self, prof, mat, seg=24, axis='u', c=(0.0, 0.0), closed=False, a0=0.0, xf=None, exact=False):
        """Surface of revolution. prof = [(a, r), ...] with a along the axis.
        axis 'u': c=(v0, w0); 'v': c=(u0, w0); 'w': c=(u0, v0).  r=0 makes a pole.
        a0 = start angle (deg). For axis 'u' angle 0 points up (+v), 90 points right (+w)."""
        prof = clean_poly(prof, closed=closed)
        seg = seg_count(seg, exact)
        bm = self.bm
        m = self.mi(mat)
        st = math.radians(a0)

        def P(a, r, t):
            ct, sn = math.cos(t), math.sin(t)
            if axis == 'u':
                return (a, c[0] + r * ct, c[1] + r * sn)
            if axis == 'v':
                return (c[0] + r * ct, a, c[1] + r * sn)
            return (c[0] + r * ct, c[1] + r * sn, a)

        rings = []
        for a, r in prof:
            if r < 1e-6:
                rings.append([bm.verts.new(P(a, 0.0, 0.0))])
            else:
                rings.append([bm.verts.new(P(a, r, st + 2 * math.pi * k / seg)) for k in range(seg)])
        faces = []
        pairs = [(i, i + 1) for i in range(len(rings) - 1)]
        if closed:
            pairs.append((len(rings) - 1, 0))
        for i, j in pairs:
            A, B = rings[i], rings[j]
            if len(A) == 1 and len(B) == 1:
                continue
            for k in range(seg):
                k1 = (k + 1) % seg
                if len(A) == 1:
                    faces.append(bm.faces.new((A[0], B[k], B[k1])))
                elif len(B) == 1:
                    faces.append(bm.faces.new((A[k], A[k1], B[0])))
                else:
                    faces.append(bm.faces.new((A[k], A[k1], B[k1], B[k])))
        if not closed:
            if len(rings[0]) > 1:
                faces.append(bm.faces.new(rings[0][::-1]))
            if len(rings[-1]) > 1:
                faces.append(bm.faces.new(rings[-1]))
        for f in faces:
            f.material_index = m
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        return self._finish([r[0] for r in rings], xf)

    def cyl(self, a0, a1, r, mat, axis='u', c=(0.0, 0.0), seg=16, chamfer=0.0, xf=None, a_start=0.0, exact=False):
        """Solid cylinder along an axis with optional end chamfers."""
        ch = min(chamfer, r * 0.45, abs(a1 - a0) * 0.45)
        s = 1.0 if a1 >= a0 else -1.0
        if ch > 0:
            prof = [(a0, 0), (a0, r - ch), (a0 + s * ch, r), (a1 - s * ch, r), (a1, r - ch), (a1, 0)]
        else:
            prof = [(a0, 0), (a0, r), (a1, r), (a1, 0)]
        return self.lathe(prof, mat, seg=seg, axis=axis, c=c, xf=xf, a0=a_start, exact=exact)

    def tube(self, a0, a1, r_out, r_in, mat, axis='u', c=(0.0, 0.0), seg=24, chamfer=0.0, xf=None, a_start=0.0,
             exact=False):
        """Hollow cylinder (closed profile lathe)."""
        ch = min(chamfer, (r_out - r_in) * 0.45)
        s = 1.0 if a1 >= a0 else -1.0
        if ch > 0:
            prof = [(a0, r_in), (a0, r_out - ch), (a0 + s * ch, r_out), (a1 - s * ch, r_out), (a1, r_out - ch), (a1, r_in)]
        else:
            prof = [(a0, r_in), (a0, r_out), (a1, r_out), (a1, r_in)]
        return self.lathe(prof, mat, seg=seg, axis=axis, c=c, closed=True, xf=xf, a0=a_start, exact=exact)

    def loft(self, rings, mat, caps=(True, True), closed=False, xf=None):
        """Skin rings of 3D points (all rings same count; a ring collapsed to one point becomes a pole)."""
        bm = self.bm
        m = self.mi(mat)
        R = []
        for ring in rings:
            pts = [Vector(p) for p in ring]
            cen = sum(pts, Vector((0.0, 0.0, 0.0))) / len(pts)
            if max((p - cen).length for p in pts) < 1e-6:
                R.append([bm.verts.new(cen)])
            else:
                R.append([bm.verts.new(p) for p in pts])
        n = max(len(r) for r in R)
        faces = []
        pairs = [(i, i + 1) for i in range(len(R) - 1)]
        if closed:
            pairs.append((len(R) - 1, 0))
        for i, j in pairs:
            A, B = R[i], R[j]
            if len(A) == 1 and len(B) == 1:
                continue
            for k in range(n):
                k1 = (k + 1) % n
                if len(A) == 1:
                    faces.append(bm.faces.new((A[0], B[k], B[k1])))
                elif len(B) == 1:
                    faces.append(bm.faces.new((A[k], A[k1], B[0])))
                else:
                    faces.append(bm.faces.new((A[k], A[k1], B[k1], B[k])))
        if not closed:
            if caps[0] and len(R[0]) > 1:
                faces.append(bm.faces.new(R[0][::-1]))
            if caps[1] and len(R[-1]) > 1:
                faces.append(bm.faces.new(R[-1]))
        for f in faces:
            f.material_index = m
        bmesh.ops.recalc_face_normals(bm, faces=faces)
        return self._finish([r[0] for r in R], xf)

    def section_loft(self, stations, axis, mat, caps=(True, True), xf=None):
        """stations = [(a, poly2d), ...]: equal-count 2D sections placed along an axis (like prism)."""
        rings = [[self._P(axis, p, a) for p in clean_poly(poly)] for a, poly in stations]
        return self.loft(rings, mat, caps=caps, xf=xf)

    def tube_prism(self, outer, inner, axis, a0, a1, mat, xf=None):
        """Hollow prism: outer and inner 2D polygons with equal point counts."""
        P = self._P
        rings = [[P(axis, p, a0) for p in outer], [P(axis, p, a1) for p in outer],
                 [P(axis, p, a1) for p in inner], [P(axis, p, a0) for p in inner]]
        return self.loft(rings, mat, caps=(False, False), closed=True, xf=xf)

    def sweep(self, path, section, mat, ref=(0.0, 0.0, 1.0), caps=(True, True), closed=False, xf=None):
        """Sweep a 2D section (x along `ref`, y along tangent x ref) along a 3D path."""
        P = [Vector(p) for p in path]
        n = len(P)
        Ts = []
        for i in range(n):
            if closed:
                d = (P[(i + 1) % n] - P[i]).normalized() + (P[i] - P[i - 1]).normalized()
            elif i == 0:
                d = P[1] - P[0]
            elif i == n - 1:
                d = P[-1] - P[-2]
            else:
                d = (P[i + 1] - P[i]).normalized() + (P[i] - P[i - 1]).normalized()
            Ts.append(d.normalized())
        X = Vector(ref)
        X = (X - X.dot(Ts[0]) * Ts[0]).normalized()
        rings = []
        for i in range(n):
            if i:
                X = (X - X.dot(Ts[i]) * Ts[i]).normalized()
            Y = Ts[i].cross(X)
            rings.append([P[i] + X * x + Y * y for (x, y) in section])
        return self.loft(rings, mat, caps=caps, closed=closed, xf=xf)

    def mirror_w(self, verts):
        """Duplicate the given geometry mirrored across w = 0 (left/right copy)."""
        vs = set(v for v in verts if v.is_valid)
        faces = set(f for v in vs for f in v.link_faces)
        edges = set(e for v in vs for e in v.link_edges)
        ret = bmesh.ops.duplicate(self.bm, geom=list(vs) + list(edges) + list(faces))
        nv = [g for g in ret['geom'] if isinstance(g, bmesh.types.BMVert)]
        nf = [g for g in ret['geom'] if isinstance(g, bmesh.types.BMFace)]
        bmesh.ops.transform(self.bm, matrix=Matrix.Diagonal((1.0, 1.0, -1.0, 1.0)), verts=nv)
        bmesh.ops.reverse_faces(self.bm, faces=nf)
        for g in self._groups:
            g.verts.extend(nv)
        return nv

    # ---------------------------------------------------------------- whole-part operations
    def _set_all_mat(self, mi):
        for f in self.bm.faces:
            f.material_index = mi

    def _to_temp_object(self, name):
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob

    def merge(self, other):
        """Append all geometry of another MB (materials remapped by name)."""
        remap = [self.mi(m) for m in other.mats]
        for f in other.bm.faces:
            f.material_index = remap[f.material_index] if f.material_index < len(remap) else 0
        me = bpy.data.meshes.new('_merge')
        other.bm.to_mesh(me)
        before = set(self.bm.verts)
        self.bm.from_mesh(me)
        bpy.data.meshes.remove(me)
        new = [v for v in self.bm.verts if v not in before]
        for g in self._groups:
            g.verts.extend(new)
        return new

    def boolean(self, cutter, mat=None, op='DIFFERENCE'):
        """Boolean this whole MB with another MB (closed shells; the cutter may contain overlapping
        shells). Cut walls get material `mat` (default: this MB's first material). Uses the MANIFOLD
        solver, falls back to EXACT with self-intersection handling, and never returns an empty mesh."""
        if not cutter.bm.faces:
            return
        cutter_mi = self.mi(mat) if mat else 0
        cutter._set_all_mat(cutter_mi)
        ot = self._to_temp_object('_bool_target')
        oc = cutter._to_temp_object('_bool_cutter')
        n_before = len(self.bm.faces)
        result = None
        for solver, use_self in (('MANIFOLD', False), ('EXACT', True)):
            ot.modifiers.clear()
            mod = ot.modifiers.new('bool', 'BOOLEAN')
            mod.operation = op
            mod.object = oc
            mod.solver = solver
            mod.use_self = use_self
            try:
                mod.material_mode = 'INDEX'
            except Exception:
                pass
            deps = bpy.context.evaluated_depsgraph_get()
            me_res = bpy.data.meshes.new_from_object(ot.evaluated_get(deps))
            if len(me_res.polygons) >= max(4, n_before // 4):
                result = me_res
                break
            bpy.data.meshes.remove(me_res)
        if result is not None:
            nb = bmesh.new()
            nb.from_mesh(result)
            self.bm.free()
            self.bm = nb
            bpy.data.meshes.remove(result)
        else:
            print('[boolean] WARNING: boolean failed, keeping the uncut part')
        for ob in (ot, oc):
            me = ob.data
            bpy.data.objects.remove(ob)
            bpy.data.meshes.remove(me)

    def assign_material(self, pred, mat, faces=None):
        """Set material `mat` on faces whose centre (gun mm) satisfies pred(u, v, w)."""
        mi = self.mi(mat)
        for f in (faces if faces is not None else self.bm.faces):
            c = f.calc_center_median()
            if pred(c.x, c.y, c.z):
                f.material_index = mi

    # ---------------------------------------------------------------- output
    def tri_estimate(self):
        return sum(len(f.verts) - 2 for f in self.bm.faces)


# --------------------------------------------------------------------------------------
# Assets: one collection + one root empty (named exactly as the asset id) per asset
# --------------------------------------------------------------------------------------
def unique_name(name, asset_id):
    return f"{name}__{asset_id}"


class Asset:
    def __init__(self, asset_id, kind, slot=None, bevel_mm=None, bevel_seg=1, **meta):
        self.id = asset_id
        self.kind = kind
        # chamfer width of the global bevel modifier (mm)
        self.bevel_mm = bevel_mm if bevel_mm is not None else (0.7 if kind == 'weapon' else 0.45)
        self.bevel_seg = bevel_seg
        self.coll = bpy.data.collections.new(asset_id)
        bpy.context.scene.collection.children.link(self.coll)
        self.root = bpy.data.objects.new(asset_id, None)
        self.root.empty_display_type = 'PLAIN_AXES'
        self.root.empty_display_size = 0.03
        self.coll.objects.link(self.root)
        self.root['export_name'] = asset_id
        self.root['asset_kind'] = kind
        self.meta = dict(id=asset_id, kind=kind, slot=slot)
        self.meta.update(meta)
        self.meshes = {}
        self.sockets = {}

    # ------------------------------------------------------------ meshes
    def mesh(self, mb, name, origin=(0.0, 0.0, 0.0), sharp_angle=30.0, bevel_mm=None):
        """Turn an MB into a mesh object parented to the root, with its origin at `origin` (gun mm).
        Bevel / weighted normals / triangulation / UVs are added later by pipeline.finalize()."""
        bm = mb.bm
        # remove slivers left by bevels on tiny fillets (zero-length edges / zero-area faces)
        bmesh.ops.dissolve_degenerate(bm, dist=0.01, edges=list(bm.edges))
        o = g2b(origin)
        M = Matrix.Translation(-o) @ G2B @ Matrix.Scale(MM, 4)
        bmesh.ops.transform(bm, matrix=M, verts=list(bm.verts))
        uname = unique_name(name, self.id)
        me = bpy.data.meshes.new(uname)
        bm.to_mesh(me)
        bm.free()
        for mname in mb.mats:
            me.materials.append(get_material(mname))
        ob = bpy.data.objects.new(uname, me)
        self.coll.objects.link(ob)
        ob.parent = self.root
        ob.location = o
        ob['export_name'] = name
        ob['bevel_mm'] = float(self.bevel_mm if bevel_mm is None else bevel_mm)
        ob['bevel_seg'] = int(self.bevel_seg)
        attr = me.attributes.get('nobevel')
        if attr is not None:
            idx = [i for i, d in enumerate(attr.data) if d.value]
            if idx:
                vg = ob.vertex_groups.new(name='NoBevel')
                vg.add(idx, 1.0, 'REPLACE')
            me.attributes.remove(attr)
        me.shade_smooth()
        me.set_sharp_from_angle(angle=math.radians(sharp_angle))
        self.meshes[name] = ob
        return ob

    # ------------------------------------------------------------ sockets (empties)
    def socket(self, name, pos, rot=(0.0, 0.0, 0.0), size=0.012):
        """Empty at gun-space pos (mm). rot = Blender euler (deg). Unrotated: Unity forward = bore."""
        e = bpy.data.objects.new(unique_name(name, self.id), None)
        e.empty_display_type = 'ARROWS'
        e.empty_display_size = size
        self.coll.objects.link(e)
        e.parent = self.root
        e.location = g2b(pos)
        e.rotation_euler = [math.radians(a) for a in rot]
        e['export_name'] = name
        self.sockets[name] = e
        return e

    def grip_socket(self, name, pos, rake_deg):
        """Hand anchor: local -Y runs DOWN the grip axis (raked rearwards by rake_deg),
        local +Z points to the front of the grip (trigger side). Negative rake = forward rake."""
        return self.socket(name, pos, rot=(90.0 + rake_deg, 0.0, 0.0))


def smart_uv(ob):
    vl = bpy.context.view_layer
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    for o in list(bpy.context.selected_objects):
        o.select_set(False)
    vl.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.003, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    ob.select_set(False)


def reset_scene():
    """Empty the current file (objects, meshes, materials, collections)."""
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    for coll in (bpy.data.objects, bpy.data.meshes, bpy.data.materials, bpy.data.collections,
                 bpy.data.cameras, bpy.data.lights, bpy.data.images):
        for item in list(coll):
            coll.remove(item)
    sc = bpy.context.scene
    sc.unit_settings.system = 'METRIC'
    sc.unit_settings.scale_length = 1.0
    for name in MATERIALS:
        get_material(name)
