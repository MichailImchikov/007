"""
preview.py - Workbench review renders (not exported): bare weapons, weapons with attachments
mounted at their sockets (collection instances), socket markers and an attachment contact sheet.
"""
import math
import os

import bpy
import numpy as np
from mathutils import Vector, Matrix

# brighter-than-real colours so dark parts stay readable in the review renders
REVIEW_COLORS = {
    'Metal_Black': (66, 68, 74), 'Metal_Dark': (92, 96, 104), 'Metal_Worn': (150, 154, 160),
    'Polymer_Black': (58, 58, 58), 'Polymer_Tan': (165, 138, 102), 'Polymer_OD': (98, 108, 58),
    'Wood': (140, 84, 44), 'Rubber': (30, 30, 30), 'Glass_Lens': (90, 150, 175),
    'Reticle_Red': (255, 30, 30), 'Emitter_Laser': (255, 40, 40), 'Emitter_Light': (255, 238, 190),
    'Brass': (205, 172, 70),
}
MARKERS = {'Marker_Pos': (255, 220, 0), 'Marker_Fwd': (255, 0, 220), 'Marker_Up': (0, 210, 60)}


def _lin(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


class ReviewPalette:
    def __enter__(self):
        self.saved = {}
        for name, rgb in REVIEW_COLORS.items():
            m = bpy.data.materials.get(name)
            if m:
                self.saved[name] = tuple(m.diffuse_color)
                m.diffuse_color = (*[_lin(c) for c in rgb], 1.0)
        for name, rgb in MARKERS.items():
            m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
            m.diffuse_color = (*[_lin(c) for c in rgb], 1.0)
        return self

    def __exit__(self, *exc):
        for name, col in self.saved.items():
            bpy.data.materials[name].diffuse_color = col
        for name in MARKERS:
            m = bpy.data.materials.get(name)
            if m:
                bpy.data.materials.remove(m)


def setup_render():
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'MATERIAL'
    sh.show_cavity = True
    sh.cavity_type = 'BOTH'
    sh.cavity_ridge_factor = 1.0
    sh.cavity_valley_factor = 1.0
    sh.curvature_ridge_factor = 1.0
    sh.curvature_valley_factor = 1.0
    sh.show_object_outline = True
    sh.object_outline_color = (0.05, 0.05, 0.05)
    sh.show_specular_highlight = True
    sh.show_shadows = False
    sc.display.render_aa = '16'
    if sc.world is None:
        sc.world = bpy.data.worlds.new('ReviewWorld')
    sc.world.color = (0.62, 0.64, 0.67)
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGB'
    try:
        sc.view_settings.view_transform = 'Standard'
    except Exception:
        pass


# ------------------------------------------------------------------------------ visibility helpers
def layer_coll(name):
    def rec(lc):
        if lc.collection.name == name:
            return lc
        for c in lc.children:
            r = rec(c)
            if r:
                return r
        return None
    return rec(bpy.context.view_layer.layer_collection)


def show_only(collection_names):
    keep = set(collection_names) | {'_Studio', '_Preview'}
    for lc in bpy.context.view_layer.layer_collection.children:
        lc.exclude = lc.collection.name not in keep


def preview_collection():
    c = bpy.data.collections.get('_Preview')
    if c is None:
        c = bpy.data.collections.new('_Preview')
        bpy.context.scene.collection.children.link(c)
    return c


def clear_preview():
    c = bpy.data.collections.get('_Preview')
    if c is None:
        return
    for o in list(c.objects):
        data = o.data
        bpy.data.objects.remove(o)
        if data is not None and data.users == 0:
            if isinstance(data, bpy.types.Mesh):
                bpy.data.meshes.remove(data)
            elif isinstance(data, bpy.types.Camera):
                bpy.data.cameras.remove(data)
            elif isinstance(data, bpy.types.Curve):
                bpy.data.curves.remove(data)
    bpy.data.collections.remove(c)


def child(root, export_name):
    for o in root.children_recursive:
        if o.get('export_name') == export_name:
            return o
    return None


# ------------------------------------------------------------------------------ markers
def _marker_mesh(name, size):
    import bmesh
    bm = bmesh.new()
    mats = ['Marker_Pos', 'Marker_Fwd', 'Marker_Up']
    r = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=size * 0.18)
    for f in set(f for v in r['verts'] for f in v.link_faces):
        f.material_index = 0

    def arrow(direction, length, mi):
        res = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=6,
                                    radius1=size * 0.05, radius2=size * 0.05, depth=length)
        d = Vector(direction).normalized()
        rot = Vector((0, 0, 1)).rotation_difference(d).to_matrix().to_4x4()
        M = Matrix.Translation(d * (length / 2)) @ rot
        bmesh.ops.transform(bm, matrix=M, verts=res['verts'])
        for f in set(f for v in res['verts'] for f in v.link_faces):
            f.material_index = mi
    arrow((0, -1, 0), size * 1.6, 1)
    arrow((0, 0, 1), size * 0.8, 2)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(bpy.data.materials[m])
    return me


def add_markers(root, size):
    pc = preview_collection()
    for o in root.children_recursive:
        if o.type != 'EMPTY':
            continue
        mk = bpy.data.objects.new('_mk_' + o.name, _marker_mesh('_mk_' + o.name, size))
        pc.objects.link(mk)
        mk.matrix_world = o.matrix_world.copy()


def add_label(text, loc, size, rot=(math.pi / 2, 0, -math.pi / 2)):
    pc = preview_collection()
    cu = bpy.data.curves.new('_lbl_' + text, 'FONT')
    cu.body = text
    cu.size = size
    ob = bpy.data.objects.new('_lbl_' + text, cu)
    pc.objects.link(ob)
    ob.location = loc
    ob.rotation_euler = rot
    mat = bpy.data.materials.get('Marker_Pos')
    if mat:
        cu.materials.append(mat)
    return ob


# ------------------------------------------------------------------------------ instancing
def instance_asset(asset_coll, matrix):
    pc = preview_collection()
    e = bpy.data.objects.new('_inst_' + asset_coll.name, None)
    e.instance_type = 'COLLECTION'
    e.instance_collection = asset_coll
    pc.objects.link(e)
    e.matrix_world = matrix
    return e


_POINTS = []


def world_bounds(objs, extra=()):
    """Bounds of mesh objects + (collection, matrix) instances. Also stores a vertex sample in
    _POINTS (used to frame the perspective camera tightly)."""
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    _POINTS.clear()

    def acc(ob, M):
        me = ob.data
        step = max(1, len(me.vertices) // 400)
        for i in range(0, len(me.vertices), step):
            p = M @ me.vertices[i].co
            _POINTS.append(p)
            for k in range(3):
                lo[k] = min(lo[k], p[k])
                hi[k] = max(hi[k], p[k])
    for o in objs:
        if o.type == 'MESH' and not o.hide_render:
            acc(o, o.matrix_world)
    for coll, M in extra:
        for o in coll.all_objects:
            if o.type == 'MESH':
                acc(o, M @ o.matrix_world)
    return lo, hi


# ------------------------------------------------------------------------------ cameras
def _cam():
    pc = preview_collection()
    cd = bpy.data.cameras.new('_cam')
    co = bpy.data.objects.new('_cam', cd)
    pc.objects.link(co)
    bpy.context.scene.camera = co
    return co


def _orient(co, forward, up_hint=Vector((0, 0, 1))):
    f = Vector(forward).normalized()
    r = f.cross(up_hint)
    if r.length < 1e-6:
        r = f.cross(Vector((1, 0, 0)))
    r.normalize()
    u = r.cross(f)
    M = Matrix((r, u, -f)).transposed()
    co.rotation_euler = M.to_euler()
    return M


def render_ortho(path, lo, hi, view='side', width=1600, margin=1.08):
    sc = bpy.context.scene
    c = (lo + hi) / 2
    ext = hi - lo
    co = _cam()
    co.data.type = 'ORTHO'
    if view in ('side', 'right'):  # from the weapon's right (-X), muzzle to the right of the image
        eh, ev = ext.y, ext.z
        co.location = (lo.x - 1.0, c.y, c.z)
        _orient(co, (1, 0, 0))
    elif view == 'left':           # from the weapon's left (+X), muzzle to the left (like a photo of that side)
        eh, ev = ext.y, ext.z
        co.location = (hi.x + 1.0, c.y, c.z)
        _orient(co, (-1, 0, 0))
    elif view == 'top':          # from above, muzzle to the right, right side at the bottom
        eh, ev = ext.y, ext.x
        co.location = (c.x, c.y, hi.z + 1.0)
        _orient(co, (0, 0, -1), up_hint=Vector((1, 0, 0)))
    elif view == 'front':        # looking down the muzzle
        eh, ev = ext.x, ext.z
        co.location = (c.x, lo.y - 1.0, c.z)
        _orient(co, (0, 1, 0))
    else:
        raise ValueError(view)
    eh = max(eh, 1e-3)
    ev = max(ev, 1e-3)
    aspect = max(0.4, min(3.2, (eh / ev)))
    h = int(width / aspect)
    sc.render.resolution_x = width
    sc.render.resolution_y = max(200, h)
    A = sc.render.resolution_x / sc.render.resolution_y
    co.data.ortho_scale = max(eh * margin, ev * margin * A) if A >= 1 else max(eh * margin / A, ev * margin)
    co.data.clip_start = 0.01
    co.data.clip_end = 10.0
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)


def render_persp(path, lo, hi, direction=(-0.9, -0.62, 0.42), res=(1600, 1000), lens=50.0):
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = res
    co = _cam()
    co.data.type = 'PERSP'
    co.data.lens = lens
    co.data.sensor_fit = 'HORIZONTAL'
    co.data.clip_start = 0.005
    co.data.clip_end = 20.0
    d = Vector(direction).normalized()
    pts = list(_POINTS) or [Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]
    c = (lo + hi) / 2
    dist = (hi - lo).length * 1.5
    hf = 18.0 / lens
    vf = hf * res[1] / res[0]
    R = _orient(co, -d)
    for _ in range(8):
        # re-centre the view on the projected point cloud, then fit the distance
        inv = (Matrix.Translation(c + d * dist) @ R.to_4x4()).inverted()
        xs, ys, need = [], [], 0.0
        for p in pts:
            q = inv @ p
            z = max(-q.z, 1e-4)
            xs.append(q.x / (z * hf))
            ys.append(q.y / (z * vf))
        cx = (max(xs) + min(xs)) / 2
        cy = (max(ys) + min(ys)) / 2
        cam_r = R @ Vector((1, 0, 0))
        cam_u = R @ Vector((0, 1, 0))
        c = c + cam_r * (cx * dist * hf) + cam_u * (cy * dist * vf)
        need = max((max(xs) - min(xs)) / 2, (max(ys) - min(ys)) / 2)
        dist *= (need / 0.9) ** 0.8
    co.location = c + d * dist
    _orient(co, -d)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)


def render_first_person(path, root, hi, res=(1600, 900)):
    """Rough first-person look: eye behind and right of the rear sight, looking down the weapon."""
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = res
    sight = child(root, 'Sight_Iron') or child(root, 'Socket_Optic')
    grip = child(root, 'Grip_R')
    muz = child(root, 'Socket_Muzzle')
    s = sight.matrix_world.translation
    g = grip.matrix_world.translation
    m = muz.matrix_world.translation
    length = (m - g).length
    eye = Vector((s.x - 0.07 * length / 0.5, g.y + 0.35 * length, s.z + 0.05 * length / 0.5))
    target = Vector((s.x, m.y + 0.1 * length, (s.z + m.z) / 2))
    co = _cam()
    co.data.type = 'PERSP'
    co.data.lens = 24.0
    co.data.clip_start = 0.005
    co.location = eye
    _orient(co, target - eye)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)


# ------------------------------------------------------------------------------ high level
def render_config(assets_by_id, weapon_id, out_prefix, mounts=None, hide=(), views=('side', '34'),
                  markers=True):
    """mounts = {socket_name: attachment_id}. hide = export names of weapon children to hide."""
    weapon = assets_by_id[weapon_id]
    root = weapon.root
    show_only([weapon_id])
    hidden = []
    for name in hide:
        o = child(root, name)
        if o is not None:
            o.hide_render = True
            hidden.append(o)
    extra = []
    for sock, att_id in (mounts or {}).items():
        s = child(root, sock)
        att = assets_by_id[att_id]
        M = s.matrix_world.copy()
        instance_asset(att.coll, M)
        extra.append((att.coll, M))
    objs = [o for o in root.children_recursive]
    lo, hi = world_bounds(objs, extra)
    if markers:
        size = 0.02 if (hi - lo).length > 0.4 else 0.012
        add_markers(root, size)
    bpy.context.view_layer.update()
    paths = []
    for v in views:
        p = f"{out_prefix}_{v}.png"
        if v == '34':
            render_persp(p, lo, hi)
        elif v == '34b':
            render_persp(p, lo, hi, direction=(0.8, 0.9, 0.45))
        elif v == 'fp':
            render_first_person(p, root, hi)
        else:
            render_ortho(p, lo, hi, view=v)
        paths.append(p)
    for o in hidden:
        o.hide_render = False
    clear_preview()
    return paths


def studio_setup(samples=64):
    """Cycles (GPU) studio: soft gradient world + three large area lights, OptiX denoise."""
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    try:
        sc.cycles.denoiser = 'OPTIX'
    except Exception:
        pass
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGB'
    try:
        sc.view_settings.view_transform = 'AgX'
        sc.view_settings.look = 'AgX - Medium High Contrast'
    except Exception:
        pass
    w = bpy.data.worlds.get('Studio') or bpy.data.worlds.new('Studio')
    if w.node_tree is None:
        w.use_nodes = True
    nt = w.node_tree
    nt.nodes.clear()
    tc = nt.nodes.new('ShaderNodeTexCoord')
    sep = nt.nodes.new('ShaderNodeSeparateXYZ')
    bg = nt.nodes.new('ShaderNodeBackground')
    out = nt.nodes.new('ShaderNodeOutputWorld')
    nt.links.new(tc.outputs['Generated'], sep.inputs[0])
    mr = nt.nodes.new('ShaderNodeMapRange')
    nt.links.new(sep.outputs['Z'], mr.inputs['Value'])
    mr.inputs['From Min'].default_value = -0.4
    mr.inputs['From Max'].default_value = 0.8
    mr.inputs['To Min'].default_value = 0.10
    mr.inputs['To Max'].default_value = 0.85
    nt.links.new(mr.outputs['Result'], bg.inputs['Color'])
    bg.inputs['Strength'].default_value = 0.9
    nt.links.new(bg.outputs[0], out.inputs['Surface'])
    sc.world = w
    coll = bpy.data.collections.get('_Studio') or bpy.data.collections.new('_Studio')
    if coll.name not in [c.name for c in sc.collection.children]:
        sc.collection.children.link(coll)
    for name, loc, energy, size in (('_key', (-0.6, -0.5, 1.2), 32.0, 1.2), ('_fill', (0.9, 0.2, 0.6), 16.0, 1.4),
                                    ('_rim', (0.0, 1.4, 0.9), 22.0, 1.0), ('_under', (-0.4, -0.3, -1.0), 5.0, 1.5)):
        ld = bpy.data.lights.new(name, 'AREA')
        ld.energy = energy
        ld.size = size
        lo_ = bpy.data.objects.new(name, ld)
        coll.objects.link(lo_)
        lo_.location = loc
        _orient(lo_, Vector((0.0, 0.0, 0.0)) - Vector(loc))


def studio_cleanup():
    coll = bpy.data.collections.get('_Studio')
    if coll:
        for o in list(coll.objects):
            ld = o.data
            bpy.data.objects.remove(o)
            bpy.data.lights.remove(ld)
        bpy.data.collections.remove(coll)


def render_contact_sheet(assets, out_path, cell=(560, 420), cols=5, tmp_dir=None, markers=True):
    """Render each asset alone (3/4 view, optional socket markers) and tile them into one PNG."""
    tmp_dir = tmp_dir or os.path.dirname(out_path)
    files = []
    for a in assets:
        show_only([a.id] + (['_Studio'] if bpy.data.collections.get('_Studio') else []))
        lo, hi = world_bounds(list(a.root.children_recursive))
        size = max(0.004, min(0.02, (hi - lo).length * 0.12))
        if markers:
            add_markers(a.root, size)
        p = os.path.join(tmp_dir, f"_cell_{a.id}.png")
        render_persp(p, lo, hi, direction=(-0.8, -0.85, 0.5), res=cell, lens=50.0)
        clear_preview()
        files.append((a.id, p))
    rows = (len(files) + cols - 1) // cols
    W, H = cell
    sheet = np.ones((rows * H, cols * W, 4), dtype=np.float32)
    for i, (aid, p) in enumerate(files):
        img = bpy.data.images.load(p)
        px = np.empty(W * H * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        px = px.reshape(H, W, 4)
        r, c = divmod(i, cols)
        y0 = (rows - 1 - r) * H
        sheet[y0:y0 + H, c * W:(c + 1) * W, :] = px
        # thin separator
        sheet[y0:y0 + H, c * W:c * W + 2, :3] = 0.2
        sheet[y0:y0 + 2, c * W:(c + 1) * W, :3] = 0.2
        bpy.data.images.remove(img)
        os.remove(p)
    out = bpy.data.images.new('_sheet', width=cols * W, height=rows * H, alpha=False)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = out_path
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)
    return out_path
