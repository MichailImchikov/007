# Blender (background): normalise one source weapon model into the project's weapon convention and save a .blend.
#   Blender space of the result: muzzle along -Y, up +Z, the gun's right (ejection) side -X, metres,
#   origin at the trigger (gun space u=fwd, v=up, w=right  ->  x=-w, y=-u, z=v).
#   Parts: Body, Mag (+ Bolt / Slide / IronSights when the source has them separable).
# usage: blender -b --factory-startup --disable-autoexec --python weapon_prep.py -- config.json weapon_id out.blend
import bpy, bmesh, sys, json, math, os
from mathutils import Vector, Matrix

a = sys.argv[sys.argv.index("--") + 1:]
cfg_all = json.load(open(a[0], encoding="utf-8"))
wid, out = a[1], a[2]
cfg = cfg_all[wid]
src = cfg["src"]

bpy.ops.wm.read_factory_settings(use_empty=True)
if cfg.get("srcs"):
    # several FBX files placed like the pack's prefab: Unity local offset (x, y, z) -> Blender (-x, -z, y)
    for f, off in cfg["srcs"]:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=f)
        for o in set(bpy.data.objects) - before:
            if o.parent is None:
                o.location = o.location + Vector((-off[0], -off[2], off[1]))
elif src.lower().endswith(".blend"):
    bpy.ops.wm.open_mainfile(filepath=src, load_ui=False, use_scripts=False)
else:
    bpy.ops.import_scene.fbx(filepath=src)
bpy.context.view_layer.update()
scene = bpy.context.scene

# unparent the meshes keeping their world placement (FBX roots carry the axis conversion), then drop everything that
# is not geometry; vertex groups stay
for o in [x for x in bpy.data.objects if x.type == 'MESH']:
    mw = o.matrix_world.copy()
    o.parent = None
    o.matrix_world = mw
for o in list(bpy.data.objects):
    if o.type in ('CAMERA', 'LIGHT', 'EMPTY'):
        bpy.data.objects.remove(o)
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
drop = set(cfg.get("drop_objects", []))
for o in list(meshes):
    if o.name in drop or o.hide_render:
        bpy.data.objects.remove(o)
meshes = [o for o in bpy.data.objects if o.type == 'MESH']

# bake armature deformation away (rest pose) and every transform into the vertices
for o in meshes:
    for m in list(o.modifiers):
        o.modifiers.remove(m)
    mw = o.matrix_world.copy()
    o.parent = None
    o.matrix_world = mw
for o in list(bpy.data.objects):
    if o.type == 'ARMATURE':
        bpy.data.objects.remove(o)
for o in meshes:
    o.data.transform(o.matrix_world)
    o.matrix_world = Matrix.Identity(4)

# ---- orientation: the source's muzzle / up axes -> Blender (-Y, +Z)
AX = {"+X": Vector((1, 0, 0)), "-X": Vector((-1, 0, 0)), "+Y": Vector((0, 1, 0)), "-Y": Vector((0, -1, 0)), "+Z": Vector((0, 0, 1)), "-Z": Vector((0, 0, -1))}
f_src, u_src = AX[cfg["muzzle"]], AX[cfg.get("up", "+Z")]
r_src = f_src.cross(u_src)          # gun right in the source
f_dst, u_dst = Vector((0, -1, 0)), Vector((0, 0, 1))
r_dst = f_dst.cross(u_dst)          # -X
S = Matrix((f_src, u_src, r_src)).transposed()   # columns: source basis
D = Matrix((f_dst, u_dst, r_dst)).transposed()
R = (D @ S.inverted()).to_4x4()
if cfg.get("mirror"):
    R = Matrix.Scale(-1, 4, Vector((1, 0, 0))) @ R
for o in meshes:
    o.data.transform(R)
    if cfg.get("mirror"):
        o.data.flip_normals()

def bounds(objs):
    lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for v in o.data.vertices:
            for i in range(3):
                lo[i] = min(lo[i], v.co[i]); hi[i] = max(hi[i], v.co[i])
    return lo, hi

lo, hi = bounds(meshes)
length = hi.y - lo.y
s = cfg["length"] / length
for o in meshes:
    o.data.transform(Matrix.Scale(s, 4))
lo, hi = bounds(meshes)

# ---- loose islands inside gun-space boxes ([[u0, v0], [u1, v1]], metres, u = -y, v = z): cut away, or split out as a part
def islands_in_box(o, bx):
    bm = bmesh.new(); bm.from_mesh(o.data); bm.verts.ensure_lookup_table()
    seen = set(); hit = []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack = [v]; isl = []
        seen.add(v.index)
        while stack:
            x = stack.pop(); isl.append(x.index)
            for e in x.link_edges:
                y = e.other_vert(x)
                if y.index not in seen:
                    seen.add(y.index); stack.append(y)
        cu = sum(-bm.verts[i].co.y for i in isl) / len(isl)
        cv = sum(bm.verts[i].co.z for i in isl) / len(isl)
        if bx[0][0] <= cu <= bx[1][0] and bx[0][1] <= cv <= bx[1][1]:
            hit += isl
    bm.free()
    return hit

def select_verts(o, idx):
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='DESELECT'); bpy.ops.object.mode_set(mode='OBJECT')
    for i in idx:
        o.data.vertices[i].select = True
    bpy.ops.object.mode_set(mode='EDIT')

for bx in cfg.get("cut_boxes", []):
    for o in [x for x in bpy.data.objects if x.type == 'MESH']:
        idx = islands_in_box(o, bx)
        if idx:
            select_verts(o, idx)
            bpy.ops.mesh.delete(type='VERT')
            bpy.ops.object.mode_set(mode='OBJECT')
            print("CUT", o.name, len(idx))

# ---- parts: by object name and / or by vertex group (rigged single meshes) and / or by box
parts = cfg.get("parts", {})   # part -> {"objects": [...], "groups": [...], "box": [[u0, v0], [u1, v1]]}
for part, spec in parts.items():
    if "box" in spec:
        found = []
        for o in [x for x in bpy.data.objects if x.type == 'MESH']:
            idx = islands_in_box(o, spec["box"])
            if idx:
                select_verts(o, idx)
                bpy.ops.mesh.separate(type='SELECTED')
                bpy.ops.object.mode_set(mode='OBJECT')
                new = [x for x in bpy.context.selected_objects if x != o]
                if new:
                    new[0].name = part + "_box"
                    found.append(new[0].name)
        spec.setdefault("objects", []).extend(found)
        print("BOX", part, found)
def separate_groups(o, groups, name):
    """Split the vertices dominated by any of the vertex groups out of o into a new object."""
    idx = [g.index for g in o.vertex_groups if g.name in groups]
    if not idx:
        return None
    bm = bmesh.new(); bm.from_mesh(o.data)
    dl = bm.verts.layers.deform.verify()
    sel = set()
    for v in bm.verts:
        d = v[dl]
        best = max(d.items(), key=lambda kv: kv[1], default=(None, 0))
        if best[0] in idx and best[1] > 0.5:
            sel.add(v.index)
    bm.free()
    if not sel:
        return None
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='DESELECT')
    bpy.ops.object.mode_set(mode='OBJECT')
    for i in sel:
        o.data.vertices[i].select = True
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_linked(delimit=set())   # whole loose islands
    bpy.ops.mesh.separate(type='SELECTED')
    bpy.ops.object.mode_set(mode='OBJECT')
    new = [x for x in bpy.context.selected_objects if x != o]
    if not new:
        return None
    new[0].name = name
    return new[0]

made = {}
for part, spec in parts.items():
    objs = [bpy.data.objects[n] for n in spec.get("objects", []) if n in bpy.data.objects]
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and spec.get("groups"):
            sep = separate_groups(o, spec["groups"], part + "_tmp")
            if sep:
                objs.append(sep)
    if not objs:
        print("PART MISSING", part)
        continue
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    p = bpy.context.view_layer.objects.active
    p.name = part
    made[part] = p

rest = [o for o in bpy.data.objects if o.type == 'MESH' and o.name not in made]
bpy.ops.object.select_all(action='DESELECT')
for o in rest:
    o.select_set(True)
bpy.context.view_layer.objects.active = rest[0]
if len(rest) > 1:
    bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
body.name = "Body"
for o in [body] + list(made.values()):
    for g in list(o.vertex_groups):
        o.vertex_groups.remove(g)
    o.data.name = o.name

# ---- origin: the trigger (given in normalised metres from the rear-bottom-left corner, or auto)
lo, hi = bounds([body] + list(made.values()))
info = {"length": round(hi.y - lo.y, 4), "height": round(hi.z - lo.z, 4), "width": round(hi.x - lo.x, 4)}
json.dump(info, open(out + ".json", "w"))
bpy.ops.wm.save_as_mainfile(filepath=out)
print("PREP", wid, info, "parts", list(made.keys()))
