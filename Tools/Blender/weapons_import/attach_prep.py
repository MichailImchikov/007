# Blender (background): one attachment from a source file into the project's attachment convention + FBX export.
#   Blender space: forward (muzzle) -Y, up +Z, right -X, metres. Gun space here: u = forward, v = up, w = right.
#   optic : origin = bottom centre of the mount; Lens_Rear (glass, PiP / reticle surface); Sight_Point on the axis at it
#   muzzle: origin = rear face centre; Socket_Muzzle at the front face
#   grip  : origin = top centre of the clamp; Grip_L on the grip (forward = down the grip, up = to the front)
#   mag   : origin = top centre (the seat)
# usage: blender -b --factory-startup --disable-autoexec --python attach_prep.py -- cfg.json id out.fbx out.json preview.png
import bpy, bmesh, sys, json, math, os
from mathutils import Vector, Matrix

a = sys.argv[sys.argv.index("--") + 1:]
cfg = json.load(open(a[0], encoding="utf-8"))[a[1]]
aid, out_fbx, out_json, out_png = a[1], a[2], a[3], a[4]
kind = cfg["kind"]

bpy.ops.wm.read_factory_settings(use_empty=True)
src = cfg["src"]
if src.lower().endswith(".blend"):
    bpy.ops.wm.open_mainfile(filepath=src, load_ui=False, use_scripts=False)
else:
    bpy.ops.import_scene.fbx(filepath=src)
for o in [x for x in bpy.data.objects if x.type == 'MESH']:
    mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
for o in list(bpy.data.objects):
    if o.type != 'MESH':
        bpy.data.objects.remove(o)
keep = set(cfg["objects"])
for o in list(bpy.data.objects):
    if o.name not in keep:
        bpy.data.objects.remove(o)
objs = [o for o in bpy.data.objects]
for o in objs:
    for m in list(o.modifiers):
        o.modifiers.remove(m)
    o.data = o.data.copy()
    o.data.transform(o.matrix_world)
    o.matrix_world = Matrix.Identity(4)

# ---- orientation (source forward / up -> Blender -Y / +Z) and scale
AX = {"+X": Vector((1, 0, 0)), "-X": Vector((-1, 0, 0)), "+Y": Vector((0, 1, 0)), "-Y": Vector((0, -1, 0)), "+Z": Vector((0, 0, 1)), "-Z": Vector((0, 0, -1))}
f_src, u_src = AX[cfg.get("fwd", "+X")], AX[cfg.get("up", "+Z")]
r_src = f_src.cross(u_src)
f_dst, u_dst = Vector((0, -1, 0)), Vector((0, 0, 1)); r_dst = f_dst.cross(u_dst)
R = (Matrix((f_dst, u_dst, r_dst)).transposed() @ Matrix((f_src, u_src, r_src)).transposed().inverted()).to_4x4()
for o in objs:
    o.data.transform(Matrix.Scale(cfg.get("scale", 1.0), 4) @ R)
    if cfg.get("mirror_lr"):   # left <-> right (e.g. a side bracket that belongs on the left of the receiver)
        o.data.transform(Matrix.Scale(-1, 4, Vector((1, 0, 0))))
        o.data.flip_normals()

def g(p): return Vector((-p.y, p.z, -p.x))      # Blender -> gun
def b(u, v, w): return Vector((-w, -u, v))      # gun -> Blender
def verts(o): return [g(v.co) for v in o.data.vertices]
def box(ps):
    return (Vector((min(p.x for p in ps), min(p.y for p in ps), min(p.z for p in ps))),
            Vector((max(p.x for p in ps), max(p.y for p in ps), max(p.z for p in ps))))

# ---- join into Body (+ the lens split off by object name or by material)
bpy.ops.object.select_all(action='DESELECT')
lens_objs = [o for o in objs if o.name in set(cfg.get("lens_objects", []))]
body_objs = [o for o in objs if o not in lens_objs]
for o in body_objs: o.select_set(True)
bpy.context.view_layer.objects.active = body_objs[0]
if len(body_objs) > 1: bpy.ops.object.join()
body = bpy.context.view_layer.objects.active; body.name = "Body"; body.data.name = "Body"
lens = None
if lens_objs:
    bpy.ops.object.select_all(action='DESELECT')
    for o in lens_objs: o.select_set(True)
    bpy.context.view_layer.objects.active = lens_objs[0]
    if len(lens_objs) > 1: bpy.ops.object.join()
    lens = bpy.context.view_layer.objects.active
elif cfg.get("lens_material"):
    names = cfg["lens_material"]
    idx = [i for i, m in enumerate(body.data.materials) if m and m.name in names]
    if idx:
        bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active = body; body.select_set(True)
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='DESELECT'); bpy.ops.object.mode_set(mode='OBJECT')
        n = 0
        for p in body.data.polygons:
            p.select = p.material_index in idx; n += p.select
        if n:
            bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.separate(type='SELECTED'); bpy.ops.object.mode_set(mode='OBJECT')
            lens = [x for x in bpy.context.selected_objects if x != body][0]
# drop empty / degenerate faces left in the body
for o in [body] + ([lens] if lens else []):
    o.select_set(False)

BV = verts(body)
lo, hi = box(BV + (verts(lens) if lens else []))

# ---- lens: keep only the rear-most pane as Lens_Rear (front panes become Lens_Front), or build a disc at the eyepiece
lens_front = None
if lens and kind == "optic":
    lv = verts(lens); llo, lhi = box(lv)
    if lhi.x - llo.x > 0.02:   # two panes: split by the middle
        mid = (llo.x + lhi.x) / 2
        bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active = lens; lens.select_set(True)
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='DESELECT'); bpy.ops.object.mode_set(mode='OBJECT')
        for v in lens.data.vertices:
            v.select = g(v.co).x > mid
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_linked(delimit=set()); bpy.ops.mesh.separate(type='SELECTED'); bpy.ops.object.mode_set(mode='OBJECT')
        rest = [x for x in bpy.context.selected_objects if x != lens]
        if rest: lens_front = rest[0]
        if lens_front and len(lens_front.data.polygons) == 0:   # stray vertices, no pane
            bpy.data.objects.remove(lens_front); lens_front = None
if kind == "optic" and not lens and cfg.get("lens_auto"):
    la = cfg["lens_auto"]   # {"from_rear": metres in from the rear end, "radius": m, "v": axis height above the body bottom (or null: centre of the rear end)}
    u0 = lo.x + la.get("from_rear", 0.004)
    rear = [p for p in BV if p.x < lo.x + 0.012]
    rlo, rhi = box(rear)
    cv = lo.y + la["v"] if la.get("v") is not None else (rlo.y + rhi.y) / 2
    cw = (rlo.z + rhi.z) / 2
    r = la["radius"]
    me = bpy.data.meshes.new("Lens_Rear"); bm = bmesh.new()
    ring = [bm.verts.new(b(u0, cv + r * math.cos(t * 2 * math.pi / 24), cw + r * math.sin(t * 2 * math.pi / 24))) for t in range(24)]
    bm.faces.new(list(reversed(ring)))
    bm.to_mesh(me); bm.free()
    lens = bpy.data.objects.new("Lens_Rear", me); bpy.context.scene.collection.objects.link(lens)
if lens:
    lens.name = "Lens_Rear"; lens.data.name = "Lens_Rear"
if lens_front:
    lens_front.name = "Lens_Front"; lens_front.data.name = "Lens_Front"

# ---- origin and sockets
S = {}
lo, hi = box(verts(body))
if kind == "optic":
    llo, lhi = box(verts(lens))
    axis_v, axis_w = (llo.y + lhi.y) / 2, (llo.z + lhi.z) / 2
    mount = [p for p in BV if p.y < lo.y + 0.006]
    mlo, mhi = box(mount)
    origin = Vector(((mlo.x + mhi.x) / 2, lo.y, axis_w))
    S["Sight_Point"] = Vector((llo.x, axis_v, axis_w))
elif kind == "muzzle":
    rear = [p for p in BV if p.x < lo.x + 0.004]
    rlo, rhi = box(rear)
    origin = Vector((lo.x, (rlo.y + rhi.y) / 2, (rlo.z + rhi.z) / 2))
    S["Socket_Muzzle"] = Vector((hi.x, origin.y, origin.z))
elif kind == "grip":
    top = [p for p in BV if p.y > hi.y - 0.006]
    tlo, thi = box(top)
    origin = Vector(((tlo.x + thi.x) / 2, hi.y, (tlo.z + thi.z) / 2))
    gl = cfg.get("grip_l", [0.0, -0.045])   # (u, v) from the origin, metres
    S["Grip_L"] = origin + Vector((gl[0], gl[1], 0.0))
elif kind == "mag":
    top = [p for p in BV if p.y > hi.y - 0.006]
    tlo, thi = box(top)
    origin = Vector(((tlo.x + thi.x) / 2, hi.y, (tlo.z + thi.z) / 2))
for k, v in cfg.get("sockets", {}).items():
    S[k] = origin + Vector(v) * 0.001
shift = b(-origin.x, -origin.y, -origin.z)
movers = [body] + [x for x in (lens, lens_front) if x]
for o in movers:
    o.data.transform(Matrix.Translation(shift))
for k in S:
    S[k] = S[k] - origin

# ---- materials
PALETTE = {"Metal_Black": (0.045, 0.047, 0.05, 1), "Metal_Dark": (0.1, 0.105, 0.11, 1), "Metal_Worn": (0.34, 0.34, 0.35, 1),
           "Polymer_Black": (0.04, 0.041, 0.045, 1), "Polymer_Tan": (0.5, 0.42, 0.3, 1), "Polymer_OD": (0.24, 0.27, 0.18, 1),
           "Polymer_Red": (0.45, 0.06, 0.05, 1), "Rubber": (0.03, 0.03, 0.032, 1), "Glass_Lens": (0.3, 0.45, 0.6, 0.16), "Reticle_Red": (0.6, 0.02, 0.02, 1)}
def shared(name):
    m = bpy.data.materials.get(name)
    if not m:
        m = bpy.data.materials.new(name); m.diffuse_color = PALETTE[name]
    return m
def classify(mat):
    if cfg.get("mat"):
        return cfg["mat"].get(mat.name if mat else "", cfg.get("mat_default", "Metal_Black"))
    col = (0.1, 0.1, 0.1)
    if mat and mat.use_nodes and mat.node_tree and mat.node_tree.nodes.get("Principled BSDF"):
        c = mat.node_tree.nodes.get("Principled BSDF").inputs["Base Color"].default_value; col = (c[0], c[1], c[2])
    elif mat:
        c = mat.diffuse_color; col = (c[0], c[1], c[2])
    r, gg, bb = col
    lum = 0.2126 * r + 0.7152 * gg + 0.0722 * bb
    if r > 0.25 and r > gg * 2.5: return "Polymer_Red"
    if lum < 0.05: return "Metal_Black"
    if lum < 0.15: return "Metal_Dark"
    return cfg.get("mat_default", "Metal_Dark")
for slot in body.material_slots:
    slot.material = shared(classify(slot.material))
if not body.material_slots:
    body.data.materials.append(shared(cfg.get("mat_default", "Metal_Black")))
for o in (lens, lens_front):
    if o:
        o.data.materials.clear(); o.data.materials.append(shared("Glass_Lens"))
        for pl in o.data.polygons:
            pl.material_index = 0   # (a split-off pane kept the source's slot indices)

# ---- hierarchy, sockets, export
root = bpy.data.objects.new(aid, None); bpy.context.scene.collection.objects.link(root)
for o in movers: o.parent = root
for k, p in S.items():
    e = bpy.data.objects.new(k, None); e.empty_display_type = 'ARROWS'; e.empty_display_size = 0.01
    bpy.context.scene.collection.objects.link(e); e.parent = root; e.location = b(p.x, p.y, p.z)
    if k == "Grip_L":
        e.rotation_euler = (math.radians(90.0 + cfg.get("rake", 0.0)), 0.0, 0.0)
bpy.ops.object.select_all(action='DESELECT')
for o in [root] + list(root.children): o.select_set(True)
bpy.context.view_layer.objects.active = root
os.makedirs(os.path.dirname(out_fbx), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={'EMPTY', 'MESH'}, apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True, axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True,
    mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False)

def u3(p): return [round(p.z, 5) + 0.0, round(p.y, 5) + 0.0, round(p.x, 5) + 0.0]
allv = [p for o in movers for p in verts(o)]
blo, bhi = box(allv)
meshes = {o.name: {"materials": [m.name for m in o.data.materials if m], "triangles": sum(len(pl.vertices) - 2 for pl in o.data.polygons)} for o in movers}
entry = {"id": aid, "kind": "attachment", "slot": cfg["slot"], "file": "Assets/_Game/Art/Weapons/Attachments/%s.fbx" % aid,
         "compatible": cfg.get("compatible", []), "dimensions": [round(bhi.z - blo.z, 5), round(bhi.y - blo.y, 5), round(bhi.x - blo.x, 5)],
         "boundsMin": u3(blo), "boundsMax": u3(bhi), "meshes": meshes,
         "sockets": {k: {"position": u3(p)} for k, p in S.items()}}
if kind == "optic": entry["magnification"] = cfg.get("magnification", 1.0)
if kind == "muzzle": entry["muzzleLength"] = round(S["Socket_Muzzle"].x, 5)
if kind == "mag": entry["capacity"] = cfg.get("capacity", 30)
json.dump(entry, open(out_json, "w"), indent=1)
bpy.ops.wm.save_as_mainfile(filepath=out_fbx[:-4] + "_final.blend")

# ---- preview: right side, sockets as spheres, lens highlighted
for k, p in S.items():
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.003, location=b(p.x, p.y, p.z) + Vector((-0.05, 0, 0)))
    s = bpy.context.active_object; m = bpy.data.materials.new("mk"); m.diffuse_color = (0, 1, 0, 1); s.data.materials.append(m)
for o in (lens, lens_front):
    if o:
        m = bpy.data.materials.new("lensmk"); m.diffuse_color = (0.2, 0.6, 1, 1); o.data.materials.clear(); o.data.materials.append(m)
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.003, location=Vector((-0.05, 0, 0)))
s = bpy.context.active_object; m = bpy.data.materials.new("org"); m.diffuse_color = (1, 0, 0, 1); s.data.materials.append(m)
cam_d = bpy.data.cameras.new("c"); cam_d.type = 'ORTHO'
cam = bpy.data.objects.new("c", cam_d); bpy.context.scene.collection.objects.link(cam)
c = b((blo.x + bhi.x) / 2, (blo.y + bhi.y) / 2, 0)
cam.location = c + Vector((-1, 0, 0)); cam.rotation_euler = (math.radians(90), 0, math.radians(-90))
cam_d.ortho_scale = max(bhi.x - blo.x, bhi.y - blo.y) * 1.3
sc = bpy.context.scene; sc.camera = cam; sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.color_type = 'MATERIAL'
sc.render.resolution_x = 500; sc.render.resolution_y = 360; sc.render.filepath = out_png
bpy.ops.render.render(write_still=True)
print("ATT", aid, "dims", entry["dimensions"], {k: [round(c * 1000) for c in u3(p)] for k, p in S.items()}, "lens", bool(lens))
