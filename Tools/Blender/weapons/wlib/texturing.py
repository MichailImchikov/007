"""
texturing.py - procedural PBR "source" materials (Cycles), GPU baking of ONE atlas per asset and the
final '<Id>_Main' material that the FBX carries.

Source materials (Metal_Black, Polymer_Stipple, ...) are only bake inputs. Each one builds:
  base colour with low-frequency variation, edge wear (Bevel-node normal vs. surface normal, broken up
  by noise), cavity grime (AO node), random scratches (Voronoi edge lines), roughness variation, and a
  normal made of a small-radius Bevel-node normal (rounded edges) + a bump pattern
  (fine noise / parkerizing grain / polymer stipple / horizontal ribs / diamond checkering / knurling).

Baking (per asset, shared UV atlas): BaseColor (EMIT pass, sRGB), Metallic+Roughness (EMIT pass),
tangent-space Normal (OpenGL +Y), AO. Output files:
  <Id>_BaseColor.png, <Id>_Normal.png, <Id>_MetallicSmoothness.png (R metal, A smoothness), <Id>_Occlusion.png
"""
import os

import bpy
import numpy as np

from . import core
from .pipeline import Isolate, activate, asset_meshes

SPECIAL = core.SPECIAL_MATERIALS

_P = dict(metal=0.0, rough=0.6, wear_c='303030', wear_m=0.0, wear_r=0.45, wear=0.4, grime=0.45, scratch=0.15,
          var=0.08, pattern='poly', bump=0.12)


def _mk(**kw):
    d = dict(_P)
    d.update(kw)
    return d


SRC = {
    'Metal_Black': _mk(base='1c1c1e', metal=0.70, rough=0.42, wear_c='8d9095', wear_m=1.0, wear_r=0.30, wear=0.55,
                       grime=0.55, scratch=0.30, var=0.12, pattern='fine', bump=0.10),
    'Metal_Dark': _mk(base='2b2d30', metal=0.85, rough=0.55, wear_c='808388', wear_m=1.0, wear_r=0.28, wear=0.60,
                      grime=0.60, scratch=0.35, var=0.15, pattern='park', bump=0.18),
    'Metal_Worn': _mk(base='5a5d61', metal=1.0, rough=0.30, wear_c='aaadb1', wear_m=1.0, wear_r=0.18, wear=0.70,
                      grime=0.50, scratch=0.60, var=0.10, pattern='fine', bump=0.06),
    'Metal_Nitride': _mk(base='5d6064', metal=0.85, rough=0.50, wear_c='a5a8ac', wear_m=1.0, wear_r=0.25, wear=0.50,
                         grime=0.45, scratch=0.40, var=0.08, pattern='park', bump=0.10),
    'Metal_Knurl': _mk(base='1e1f21', metal=0.80, rough=0.45, wear_c='8d9095', wear_m=1.0, wear_r=0.30, wear=0.55,
                       grime=0.60, scratch=0.15, var=0.10, pattern='knurl', bump=0.70),
    'Polymer_Black': _mk(base='1b1b1b', pattern='poly', bump=0.12),
    'Polymer_Stipple': _mk(base='1b1b1b', rough=0.70, pattern='stipple', bump=0.85, wear=0.25),
    'Polymer_Ribs': _mk(base='1b1b1b', pattern='ribs', bump=0.65),
    'Polymer_Checker': _mk(base='1b1b1b', pattern='checker', bump=0.65),
    'Polymer_Tan': _mk(base='8b7355', wear_c='a8916f', pattern='poly', bump=0.12),
    'Polymer_OD': _mk(base='4b5320', wear_c='68703a', pattern='poly', bump=0.12),
    'Wood': _mk(base='6b3e1f', rough=0.50, wear_c='8a5a32', wear=0.35, grime=0.4, scratch=0.2, var=0.25,
                pattern='wood', bump=0.30),
    'Rubber': _mk(base='121212', rough=0.88, wear_c='1e1e1e', wear_r=0.8, wear=0.2, grime=0.3, scratch=0.0,
                  pattern='rubber', bump=0.25),
    'Rubber_Ribs': _mk(base='121212', rough=0.88, wear_c='1e1e1e', wear_r=0.8, wear=0.2, grime=0.3, scratch=0.0,
                       pattern='ribs_coarse', bump=0.9),
    'Brass': _mk(base='b5a642', metal=1.0, rough=0.30, wear_c='d6c77a', wear_m=1.0, wear_r=0.20, wear=0.40,
                 grime=0.40, scratch=0.20, var=0.06, pattern='fine', bump=0.04),
    'Paint_White': _mk(base='e3e3da', rough=0.55, wear_c='b8b8b0', wear=0.3, grime=0.35, scratch=0.0, var=0.03,
                       pattern='none', bump=0.0),
}


# ------------------------------------------------------------------------------ node helpers
class NB:
    def __init__(self, mat):
        self.nt = mat.node_tree
        self.nt.nodes.clear()

    def n(self, t, name=None, **kw):
        node = self.nt.nodes.new(t)
        if name:
            node.name = name
        for k, v in kw.items():
            setattr(node, k, v)
        return node

    def link(self, a, b):
        self.nt.links.new(a, b)

    def put(self, sock, v):
        if isinstance(v, (int, float)):
            sock.default_value = v
        elif isinstance(v, tuple):
            sock.default_value = v
        else:
            self.link(v, sock)

    def math(self, op, a, b=None, c=None, clamp=False):
        m = self.n('ShaderNodeMath', operation=op, use_clamp=clamp)
        self.put(m.inputs[0], a)
        if b is not None:
            self.put(m.inputs[1], b)
        if c is not None:
            self.put(m.inputs[2], c)
        return m.outputs[0]

    def vmath(self, op, a, b=None, scale=None):
        m = self.n('ShaderNodeVectorMath', operation=op)
        self.put(m.inputs[0], a)
        if b is not None:
            self.put(m.inputs[1], b)
        if scale is not None:
            self.put(m.inputs[3], scale)
        return m.outputs[1] if op in ('DOT_PRODUCT', 'LENGTH', 'DISTANCE') else m.outputs[0]

    def remap(self, v, fmin, fmax, tmin=0.0, tmax=1.0, clamp=True):
        m = self.n('ShaderNodeMapRange', clamp=clamp)
        self.put(m.inputs['Value'], v)
        m.inputs['From Min'].default_value = fmin
        m.inputs['From Max'].default_value = fmax
        m.inputs['To Min'].default_value = tmin
        m.inputs['To Max'].default_value = tmax
        return m.outputs['Result']

    def rgb(self, hexcol):
        r = self.n('ShaderNodeRGB')
        r.outputs[0].default_value = (*core.hex_to_linear(hexcol), 1.0)
        return r.outputs[0]

    def lerp_vec(self, a, b, t):
        d = self.vmath('SUBTRACT', b, a)
        s = self.vmath('SCALE', d, scale=t)
        return self.vmath('ADD', a, s)

    def lerp(self, a, b, t):
        return self.math('MULTIPLY_ADD', t, self.math('SUBTRACT', b, a), a)

    def noise(self, vec, scale, detail=2.0, rough=0.5):
        t = self.n('ShaderNodeTexNoise')
        self.put(t.inputs['Vector'], vec)
        t.inputs['Scale'].default_value = scale
        t.inputs['Detail'].default_value = detail
        t.inputs['Roughness'].default_value = rough
        return t.outputs['Fac']

    def wave(self, vec, scale, direction='Z', distortion=0.0, profile='SIN'):
        t = self.n('ShaderNodeTexWave', wave_type='BANDS', bands_direction=direction, wave_profile=profile)
        self.put(t.inputs['Vector'], vec)
        t.inputs['Scale'].default_value = scale
        t.inputs['Distortion'].default_value = distortion
        t.inputs['Detail'].default_value = 0.0
        return t.outputs['Fac']

    def mapping(self, vec, rot=(0.0, 0.0, 0.0), scale=(1.0, 1.0, 1.0)):
        m = self.n('ShaderNodeMapping')
        self.put(m.inputs['Vector'], vec)
        m.inputs['Rotation'].default_value = rot
        m.inputs['Scale'].default_value = scale
        return m.outputs['Vector']


def _pattern(b, P, kind):
    """Height pattern (0..1) in millimetre object space."""
    import math
    if kind == 'none':
        return None
    if kind == 'fine':
        return b.noise(P, 1.2, 2.0)
    if kind == 'park':
        return b.noise(P, 4.0, 1.0)
    if kind == 'poly':
        return b.noise(P, 2.0, 2.0)
    if kind == 'rubber':
        return b.noise(P, 2.5, 2.0)
    if kind == 'stipple':
        v = b.n('ShaderNodeTexVoronoi', feature='F1')
        b.put(v.inputs['Vector'], P)
        v.inputs['Scale'].default_value = 0.85
        return b.remap(v.outputs['Distance'], 0.0, 0.48, 1.0, 0.0)
    if kind in ('ribs', 'ribs_coarse'):
        pitch = 1.6 if kind == 'ribs' else 3.2
        w = b.wave(P, 0.314 / pitch, 'Z')
        return b.remap(w, 0.25, 0.75)
    if kind == 'checker':
        pitch = 1.3
        h1 = b.wave(b.mapping(P, rot=(math.radians(45), 0, 0)), 0.314 / pitch, 'Z')
        h2 = b.wave(b.mapping(P, rot=(math.radians(-45), 0, 0)), 0.314 / pitch, 'Z')
        return b.remap(b.math('MINIMUM', h1, h2), 0.1, 0.6)
    if kind == 'knurl':
        pitch = 1.0
        h1 = b.wave(P, 0.628 / pitch, 'DIAGONAL')
        h2 = b.wave(b.mapping(P, scale=(-1.0, 1.0, 1.0)), 0.628 / pitch, 'DIAGONAL')
        return b.remap(b.math('MINIMUM', h1, h2), 0.1, 0.6)
    if kind == 'wood':
        t = b.n('ShaderNodeTexWave', wave_type='RINGS', rings_direction='Y', wave_profile='SIN')
        b.put(t.inputs['Vector'], P)
        t.inputs['Scale'].default_value = 0.08
        t.inputs['Distortion'].default_value = 6.0
        t.inputs['Detail'].default_value = 3.0
        return t.outputs['Fac']
    return None


def build_source_material(name):
    p = SRC[name]
    mat = bpy.data.materials.new(name)
    if mat.node_tree is None:
        mat.use_nodes = True
    b = NB(mat)
    out = b.n('ShaderNodeOutputMaterial', name='OUTPUT')
    bsdf = b.n('ShaderNodeBsdfPrincipled', name='BSDF')
    emit = b.n('ShaderNodeEmission', name='EMIT')
    tc = b.n('ShaderNodeTexCoord')
    P = b.vmath('SCALE', tc.outputs['Object'], scale=1000.0)          # object space in millimetres
    nA = b.noise(P, 0.03, 3.0)                                         # ~3 cm colour / roughness variation
    nB = b.noise(P, 0.22, 5.0, 0.6)                                    # wear breakup
    # edge wear: bevel-node normal deviates from the surface normal close to edges
    bev = b.n('ShaderNodeBevel', samples=8)
    bev.inputs['Radius'].default_value = 0.0008
    geo = b.n('ShaderNodeNewGeometry')
    dot = b.vmath('DOT_PRODUCT', bev.outputs['Normal'], geo.outputs['Normal'])
    edge = b.remap(dot, 0.995, 0.93)
    wear = b.math('MULTIPLY', edge, b.remap(nB, 0.40, 0.60), clamp=True)
    wear = b.math('MULTIPLY', wear, p['wear'], clamp=True)
    # cavity grime
    ao = b.n('ShaderNodeAmbientOcclusion', samples=16, only_local=False)
    ao.inputs['Distance'].default_value = 0.004
    grime = b.math('MULTIPLY', b.remap(ao.outputs['AO'], 1.0, 0.45), p['grime'], clamp=True)
    # scratches: thin Voronoi cell edges on stretched coordinates, masked by noise
    import math
    sc_vec = b.mapping(P, rot=(0.3, 0.6, 0.9), scale=(1.0, 0.45, 0.15))
    vor = b.n('ShaderNodeTexVoronoi', feature='DISTANCE_TO_EDGE')
    b.put(vor.inputs['Vector'], sc_vec)
    vor.inputs['Scale'].default_value = 0.09
    lines = b.remap(vor.outputs['Distance'], 0.0, 0.012, 1.0, 0.0)
    scr = b.math('MULTIPLY', lines, b.remap(b.noise(P, 0.05, 2.0), 0.58, 0.72), clamp=True)
    scr = b.math('MULTIPLY', scr, p['scratch'] * 0.5, clamp=True)
    # colour
    var = b.remap(nA, 0.0, 1.0, 1.0 - p['var'], 1.0 + p['var'], clamp=False)
    c1 = b.vmath('SCALE', b.rgb(p['base']), scale=var)
    wc = b.rgb(p['wear_c'])
    c2 = b.lerp_vec(c1, wc, wear)
    c3 = b.lerp_vec(c2, b.vmath('SCALE', c2, scale=0.45), grime)
    c4 = b.lerp_vec(c3, wc, b.math('MULTIPLY', scr, 0.35))
    if p['pattern'] == 'wood':
        grain = _pattern(b, P, 'wood')
        c4 = b.lerp_vec(c4, b.vmath('SCALE', c4, scale=0.55), b.remap(grain, 0.3, 0.9))
    out_col = b.n('ShaderNodeVectorMath', name='OUT_COLOR', operation='ADD')
    b.put(out_col.inputs[0], c4)
    out_col.inputs[1].default_value = (0.0, 0.0, 0.0)
    # metal / roughness
    metal = b.math('MAXIMUM', b.lerp(p['metal'], p['wear_m'], wear), 0.0, clamp=True)
    r0 = b.math('MULTIPLY_ADD', b.math('SUBTRACT', nA, 0.5), 0.16, p['rough'])
    r1 = b.math('MULTIPLY_ADD', grime, 0.18, r0)
    r2 = b.math('MULTIPLY_ADD', scr, 0.10, r1)
    rough = b.math('MAXIMUM', b.lerp(r2, p['wear_r'], wear), 0.05, clamp=True)
    mr = b.n('ShaderNodeCombineColor', name='OUT_MR')
    b.put(mr.inputs[0], metal)
    b.put(mr.inputs[1], rough)
    mr.inputs[2].default_value = 0.0
    # normal: rounded edges (small bevel) + bump pattern
    bevN = b.n('ShaderNodeBevel', samples=8)
    bevN.inputs['Radius'].default_value = 0.0004
    normal = bevN.outputs['Normal']
    h = _pattern(b, P, p['pattern'])
    if h is not None and p['bump'] > 0:
        bump = b.n('ShaderNodeBump')
        bump.inputs['Strength'].default_value = p['bump']
        bump.inputs['Distance'].default_value = 0.0004
        b.link(h, bump.inputs['Height'])
        b.link(bevN.outputs['Normal'], bump.inputs['Normal'])
        normal = bump.outputs['Normal']
    b.link(out_col.outputs[0], bsdf.inputs['Base Color'])
    b.link(metal, bsdf.inputs['Metallic'])
    b.link(rough, bsdf.inputs['Roughness'])
    b.link(normal, bsdf.inputs['Normal'])
    b.link(bsdf.outputs[0], out.inputs['Surface'])
    b.link(emit.outputs[0], out.inputs['Surface'])
    b.link(bsdf.outputs[0], out.inputs['Surface'])
    col = core.hex_to_linear(p['base'])
    mat.diffuse_color = (*col, 1.0)
    mat.metallic = p['metal']
    mat.roughness = p['rough']
    mat['source_material'] = True
    return mat


def material_factory(name):
    if name in SRC:
        return build_source_material(name)
    return None


def set_mode(mat, mode):
    nt = mat.node_tree
    out = nt.nodes.get('OUTPUT')
    if out is None or nt.nodes.get('BSDF') is None:
        return
    if mode == 'bsdf':
        nt.links.new(nt.nodes['BSDF'].outputs[0], out.inputs['Surface'])
        return
    src = nt.nodes['OUT_COLOR'].outputs[0] if mode == 'color' else nt.nodes['OUT_MR'].outputs[0]
    emit = nt.nodes['EMIT']
    nt.links.new(src, emit.inputs['Color'])
    nt.links.new(emit.outputs[0], out.inputs['Surface'])


# ------------------------------------------------------------------------------ GPU / Cycles
def setup_cycles(samples=64):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    prefs = bpy.context.preferences.addons['cycles'].preferences
    dev_type = None
    for t in ('OPTIX', 'CUDA'):
        try:
            prefs.compute_device_type = t
            prefs.refresh_devices()
            if any(d.type == t for d in prefs.devices):
                dev_type = t
                break
        except Exception:
            continue
    for d in prefs.devices:
        d.use = (d.type == dev_type)
    sc.cycles.device = 'GPU' if dev_type else 'CPU'
    sc.cycles.samples = samples
    sc.cycles.use_denoising = False
    return dev_type or 'CPU'


def _new_image(name, res, srgb, alpha=False):
    old = bpy.data.images.get(name)
    if old:
        bpy.data.images.remove(old)
    img = bpy.data.images.new(name, res, res, alpha=alpha, float_buffer=False)
    img.colorspace_settings.name = 'sRGB' if srgb else 'Non-Color'
    return img


def _save(img, path):
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()


def bake_asset(asset, tex_dir, res, samples=64, ao_samples=128, margin=None):
    """Bake the asset's source materials into one atlas; returns {map: path}."""
    sc = bpy.context.scene
    margin = margin or max(8, res // 128)
    meshes = asset_meshes(asset)
    bake_objs = [o for o in meshes if any(m is not None and m.name not in SPECIAL for m in o.data.materials)]
    mats = sorted({m for o in bake_objs for m in o.data.materials if m is not None}, key=lambda m: m.name)
    imgs = {'color': _new_image(f'{asset.id}_bake_color', res, True),
            'mr': _new_image(f'{asset.id}_bake_mr', res, False),
            'normal': _new_image(f'{asset.id}_bake_normal', res, False),
            'ao': _new_image(f'{asset.id}_bake_ao', res, False)}
    dummy = bpy.data.images.get('_bake_dummy') or bpy.data.images.new('_bake_dummy', 8, 8)

    def targets(img):
        for m in mats:
            nt = m.node_tree
            node = nt.nodes.get('BAKE_TARGET')
            if node is None:
                node = nt.nodes.new('ShaderNodeTexImage')
                node.name = 'BAKE_TARGET'
            node.image = dummy if m.name in SPECIAL else img
            for nd in nt.nodes:
                nd.select = False
            node.select = True
            nt.nodes.active = node

    if sc.world is None:
        sc.world = bpy.data.worlds.new('BakeWorld')
    sc.world.light_settings.distance = 0.015
    with Isolate(asset):
        activate(bake_objs)
        for key, btype, mode in (('color', 'EMIT', 'color'), ('mr', 'EMIT', 'mr'),
                                 ('normal', 'NORMAL', 'bsdf'), ('ao', 'AO', 'bsdf')):
            for m in mats:
                set_mode(m, mode)
            targets(imgs[key])
            sc.cycles.samples = ao_samples if key == 'ao' else samples
            bpy.ops.object.bake(type=btype, margin=margin, margin_type='EXTEND', use_clear=True,
                                normal_space='TANGENT', normal_r='POS_X', normal_g='POS_Y', normal_b='POS_Z',
                                target='IMAGE_TEXTURES')
        for m in mats:
            set_mode(m, 'bsdf')
    os.makedirs(tex_dir, exist_ok=True)
    paths = {
        'BaseColor': os.path.join(tex_dir, f'{asset.id}_BaseColor.png'),
        'Normal': os.path.join(tex_dir, f'{asset.id}_Normal.png'),
        'MetallicSmoothness': os.path.join(tex_dir, f'{asset.id}_MetallicSmoothness.png'),
        'Occlusion': os.path.join(tex_dir, f'{asset.id}_Occlusion.png'),
    }
    _save(imgs['color'], paths['BaseColor'])
    _save(imgs['normal'], paths['Normal'])
    px = np.empty(res * res * 4, dtype=np.float32)
    imgs['mr'].pixels.foreach_get(px)
    px = px.reshape(-1, 4)
    ms = np.zeros_like(px)
    ms[:, 0] = px[:, 0]
    ms[:, 3] = 1.0 - px[:, 1]
    img_ms = _new_image(f'{asset.id}_ms', res, False, alpha=True)
    img_ms.alpha_mode = 'STRAIGHT'
    img_ms.pixels.foreach_set(ms.ravel())
    _save(img_ms, paths['MetallicSmoothness'])
    imgs['ao'].pixels.foreach_get(px.ravel())
    ao = px.reshape(-1, 4).copy()
    ao[:, 1] = ao[:, 0]
    ao[:, 2] = ao[:, 0]
    ao[:, 3] = 1.0
    img_ao = _new_image(f'{asset.id}_occ', res, False)
    img_ao.pixels.foreach_set(ao.ravel())
    _save(img_ao, paths['Occlusion'])
    for img in list(imgs.values()) + [img_ms, img_ao]:
        bpy.data.images.remove(img)
    return paths


# ------------------------------------------------------------------------------ final material
def make_main_material(asset, paths):
    name = f'{asset.id}_Main'
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    if mat.node_tree is None:
        mat.use_nodes = True
    b = NB(mat)
    out = b.n('ShaderNodeOutputMaterial')
    bsdf = b.n('ShaderNodeBsdfPrincipled')

    def tex(key, srgb):
        img = bpy.data.images.load(paths[key], check_existing=True)
        img.reload()
        img.colorspace_settings.name = 'sRGB' if srgb else 'Non-Color'
        t = b.n('ShaderNodeTexImage')
        t.image = img
        return t

    tb = tex('BaseColor', True)
    tm = tex('MetallicSmoothness', False)
    tn = tex('Normal', False)
    b.link(tb.outputs['Color'], bsdf.inputs['Base Color'])
    sep = b.n('ShaderNodeSeparateColor')
    b.link(tm.outputs['Color'], sep.inputs['Color'])
    b.link(sep.outputs[0], bsdf.inputs['Metallic'])
    b.link(b.math('SUBTRACT', 1.0, tm.outputs['Alpha']), bsdf.inputs['Roughness'])
    nm = b.n('ShaderNodeNormalMap', space='TANGENT')
    b.link(tn.outputs['Color'], nm.inputs['Color'])
    b.link(nm.outputs['Normal'], bsdf.inputs['Normal'])
    b.link(bsdf.outputs[0], out.inputs['Surface'])
    mat.diffuse_color = (0.08, 0.08, 0.08, 1.0)
    return mat


class FinalMaterials:
    """Temporarily replace every baked (source) material slot by '<Id>_Main' (specials kept)."""

    def __init__(self, assets):
        self.assets = assets

    def __enter__(self):
        self.saved = []
        for a in self.assets:
            main = bpy.data.materials.get(f'{a.id}_Main')
            if main is None:
                continue
            for ob in asset_meshes(a):
                me = ob.data
                old = list(me.materials)
                n = len(me.polygons)
                idx = np.empty(n, dtype=np.int32)
                me.polygons.foreach_get('material_index', idx)
                used = sorted(set(int(i) for i in np.unique(idx)) if n else set())
                new_list = []
                if any(i < len(old) and old[i] is not None and old[i].name not in SPECIAL for i in used):
                    new_list.append(main)
                for i in used:
                    m = old[i] if i < len(old) else None
                    if m is not None and m.name in SPECIAL and m not in new_list:
                        new_list.append(m)
                remap = []
                for m in old:
                    remap.append(new_list.index(m) if (m is not None and m in new_list) else 0)
                remap = np.array(remap or [0], dtype=np.int32)
                new_idx = remap[np.clip(idx, 0, len(remap) - 1)]
                self.saved.append((me, old, idx))
                me.materials.clear()
                for m in new_list:
                    me.materials.append(m)
                me.polygons.foreach_set('material_index', new_idx)
                me.update()
        return self

    def __exit__(self, *exc):
        for me, old, idx in self.saved:
            me.materials.clear()
            for m in old:
                me.materials.append(m)
            me.polygons.foreach_set('material_index', idx)
            me.update()
