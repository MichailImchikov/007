# Blender (background): sockets, origin, shared materials and FBX export of one prepared weapon (weapon_prep.py output).
# Gun space (u fwd, v up, w right), Blender = (x=-w, y=-u, z=v). Exports like Tools/Blender/weapons/wlib/export.py.
# usage: blender -b --factory-startup --disable-autoexec --python weapon_finish.py -- cfg.json id prep.blend out.fbx out.json preview.png
import bpy, sys, json, math, os
from mathutils import Vector, Matrix

a = sys.argv[sys.argv.index("--") + 1:]
cfg = json.load(open(a[0], encoding="utf-8"))[a[1]]
wid, prep, out_fbx, out_json, out_png = a[1], a[2], a[3], a[4], a[5]
bpy.ops.wm.open_mainfile(filepath=prep, load_ui=False, use_scripts=False)
kind = cfg.get("kind", "rifle")

def g(p):   # Blender co -> gun (u, v, w)
    return Vector((-p.y, p.z, -p.x))
def b(u, v, w):  # gun -> Blender
    return Vector((-w, -u, v))

objs = {o.name: o for o in bpy.data.objects if o.type == 'MESH'}
body = objs["Body"]
def verts(o):
    return [g(o.matrix_world @ v.co) for v in o.data.vertices]
BV = verts(body)
allv = [p for o in objs.values() for p in verts(o)]
umin = min(p.x for p in allv); umax = max(p.x for p in allv)
L = umax - umin

def box(ps):
    return (Vector((min(p.x for p in ps), min(p.y for p in ps), min(p.z for p in ps))),
            Vector((max(p.x for p in ps), max(p.y for p in ps), max(p.z for p in ps))))

# ---- bore: the front 2.5 % of the gun (barrel / muzzle device), centroid in v and w
front = [p for p in BV if p.x > umax - 0.025 * L] or [p for p in allv if p.x > umax - 0.025 * L]
vb = sum(p.y for p in front) / len(front)
wb = sum(p.z for p in front) / len(front)
ov = cfg.get("bore_v")
if ov is not None:
    vb = ov
muzzle = Vector((umax, vb, wb))

# ---- magazine (or the grip region for pistols)
mag = objs.get("Mag")
if mag:
    mlo, mhi = box(verts(mag))
else:
    mlo = mhi = None

# ---- pistol grip: the body below the receiver, behind the magazine (pistols: the magazine is in the grip)
if kind in ("pistol", "revolver"):
    gp = [p for p in BV if p.y < vb - 0.035 and p.x < umin + 0.45 * L]
else:
    ref = mlo.x if mag else umin + 0.45 * L
    gp = [p for p in BV if p.y < vb - 0.05 and ref - 0.16 < p.x < ref - 0.005]
if not gp:
    gp = [p for p in BV if p.y < vb - 0.03]
glo, ghi = box(gp)
gc = Vector((sum(p.x for p in gp) / len(gp), sum(p.y for p in gp) / len(gp), 0.0))
# the hand's height on the grip (the project's M4A1: 75 mm under the bore, 70 mm behind the trigger; Glock: 66 / 56)
pistolish = kind in ("pistol", "revolver")
hand_drop, trig_drop, trig_ahead = (0.066, 0.04, 0.05) if pistolish else (0.072, 0.042, 0.062)
if pistolish:
    band = [p for p in BV if vb - 0.08 < p.y < vb - 0.045 and p.x < umin + 0.5 * L]
else:
    ref = mlo.x if mag else umin + 0.45 * L
    band = [p for p in BV if vb - 0.095 < p.y < vb - 0.055 and ref - 0.16 < p.x < ref - 0.03]
gu = sum(p.x for p in band) / len(band) if band else gc.x
grip_hand = Vector((gu, vb - hand_drop, 0.0))
o = cfg.get("origin")
origin = Vector(o) if o else Vector((gu + trig_ahead, vb - trig_drop, 0.0))

# ---- sockets (gun space, before the origin shift)
S = {}
rake = cfg.get("rake", 20.0)
S["Socket_Muzzle"] = muzzle
S["Grip_R"] = grip_hand
if mag:
    S["Socket_Mag"] = Vector(((mlo.x + mhi.x) / 2, mhi.y - 0.004, 0.0))
# top of the receiver over the magazine well
if kind in ("pistol", "revolver"):
    top = [p for p in BV if abs(p.z) < 0.01 and umin + 0.1 * L < p.x < umin + 0.5 * L]
else:
    c = ((mlo.x + mhi.x) / 2) if mag else (umin + 0.45 * L)
    top = [p for p in BV if abs(p.z) < 0.012 and c - 0.07 < p.x < c + 0.03]
vtop = max(p.y for p in top) if top else vb + 0.03
S["Socket_Optic"] = Vector(((mlo.x + mhi.x) / 2 - 0.01 if mag and kind not in ("pistol", "revolver") else umin + 0.3 * L, vtop, 0.0))
# support hand on the handguard
if kind in ("pistol", "revolver"):
    S["Grip_L"] = S["Grip_R"] + Vector((0.0, 0.0, -0.015))
    S["Socket_Underbarrel"] = Vector((umax - 0.04, vb - 0.03, 0.0))
else:
    start = mhi.x if mag else gc.x + 0.08
    ugl = start + cfg.get("grip_l_frac", 0.42) * (umax - start)
    ugl = min(ugl, start + cfg.get("grip_l_max", 0.30))
    S["Grip_L"] = Vector((ugl, vb, 0.0))
    near = [p for p in BV if abs(p.x - (ugl + 0.02)) < 0.02 and abs(p.z) < 0.015]
    vbot = min(p.y for p in near) if near else vb - 0.025
    S["Socket_Underbarrel"] = Vector((ugl + 0.02, vbot, 0.0))
    side = [p for p in BV if abs(p.x - (ugl + 0.06)) < 0.02 and abs(p.y - vb) < 0.02]
    wside = max(p.z for p in side) if side else 0.02
    S["Socket_Side"] = Vector((ugl + 0.06, vb, wside))
# ejection port: right side of the receiver
rec = [p for p in BV if abs(p.y - (vb + 0.003)) < 0.015 and (S["Grip_R"].x < p.x < S["Grip_R"].x + 0.12)]
S["Eject"] = Vector((S["Grip_R"].x + (0.09 if kind not in ("pistol", "revolver") else 0.06), vb + 0.004, max([p.z for p in rec], default=0.012)))
# iron sight line: the rear sight's top (separated IronSights or the body)
irons = objs.get("IronSights")
iv = verts(irons) if irons else []
if iv:
    rear = [p for p in iv if p.x < (min(q.x for q in iv) + max(q.x for q in iv)) / 2] or iv
    S["Sight_Iron"] = Vector((min(p.x for p in rear), max(p.y for p in rear) - 0.004, 0.0))
else:
    S["Sight_Iron"] = Vector((S["Socket_Optic"].x - 0.06, vtop + 0.012, 0.0))
if cfg.get("iron_post"):
    # the sight line runs over the front sight post's tip (flip-up / A-frame posts in the body, or the front post part)
    front_lim = umax - cfg.get("iron_post_front", 10.0)
    cand = [p for p in BV + iv if abs(p.z) < 0.005 and max(S["Socket_Optic"].x + 0.10, front_lim) < p.x < umax - 0.03]
    if cand:
        S["Sight_Iron"] = Vector((S["Socket_Optic"].x - 0.05, max(p.y for p in cand) - 0.002, 0.0))
for k, v in cfg.get("sockets", {}).items():    # overrides (mm, gun space, relative to the ORIGIN)
    S[k] = origin + Vector(v) * 0.001
for k in cfg.get("remove_sockets", []):
    S.pop(k, None)

# ---- shift everything so the origin is the trigger
shift = b(-origin.x, -origin.y, -origin.z)
for ob in objs.values():
    ob.data.transform(Matrix.Translation(shift))
for k in S:
    S[k] = S[k] - origin

# ---- part origins (pivots): Mag at its socket, Bolt / Slide at their rear, IronSights at the gun origin
def set_origin(ob, p):
    pb = b(p.x, p.y, p.z)
    ob.data.transform(Matrix.Translation(-pb))
    ob.location = pb
if mag:
    set_origin(mag, S["Socket_Mag"])
for nm in ("Bolt", "Slide"):
    if nm in objs:
        lo2, hi2 = box(verts(objs[nm]))
        set_origin(objs[nm], Vector((lo2.x, (lo2.y + hi2.y) / 2, 0.0)))

# ---- materials: map every source material to a shared project material by its colour
MAT = {}
def shared(name, rgba, metal, smooth):
    m = bpy.data.materials.get(name)
    if not m:
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = rgba
        bsdf.inputs["Metallic"].default_value = metal
        bsdf.inputs["Roughness"].default_value = 1 - smooth
    return m
PALETTE = {
    "Metal_Black": ((0.045, 0.047, 0.05, 1), 0.7, 0.45), "Metal_Dark": ((0.1, 0.105, 0.11, 1), 0.85, 0.5),
    "Metal_Worn": ((0.34, 0.34, 0.35, 1), 0.9, 0.55), "Polymer_Black": ((0.04, 0.041, 0.045, 1), 0, 0.32),
    "Polymer_Tan": ((0.5, 0.42, 0.3, 1), 0, 0.3), "Polymer_OD": ((0.2, 0.22, 0.14, 1), 0, 0.3),
    "Wood": ((0.35, 0.18, 0.08, 1), 0, 0.35), "Brass": ((0.8, 0.6, 0.28, 1), 1, 0.72), "Rubber": ((0.03, 0.03, 0.03, 1), 0, 0.2),
}
def classify(mat, objname):
    rules = cfg.get("mat_by_object", {})
    for key, target in rules.items():
        if key in objname:
            return target
    if mat is None:
        return cfg.get("mat_default", "Metal_Black")
    rules = cfg.get("mat_by_name", {})
    if mat.name in rules:
        return rules[mat.name]
    col = (0.8, 0.8, 0.8); metal = 0.0
    if mat.use_nodes and mat.node_tree and mat.node_tree.nodes.get("Principled BSDF"):
        bs = mat.node_tree.nodes.get("Principled BSDF")
        c = bs.inputs["Base Color"].default_value; col = (c[0], c[1], c[2]); metal = bs.inputs["Metallic"].default_value
    r, gg, bb = col
    lum = 0.2126 * r + 0.7152 * gg + 0.0722 * bb
    if lum > 0.6:                                  # colour lost in the export: plain gunmetal
        return cfg.get("mat_default", "Metal_Black")
    if r > 0.12 and r > gg * 1.15 and gg > bb * 1.1:  # brown / tan
        return cfg.get("tan", "Polymer_Tan")
    if gg > r * 1.08 and gg > bb * 1.2:
        return "Polymer_OD"
    if r > 0.1 and r > gg * 2.5:                    # red bits
        return "Metal_Dark"
    if lum < 0.03:
        return "Metal_Black" if metal > 0.5 else "Polymer_Black"
    if lum < 0.09:
        return "Metal_Dark"
    return "Metal_Worn"
main = None
if cfg.get("mat_main"):     # palette-textured pack models keep their UVs: one "<Id>_Main" material (texture copied by the stager)
    main = bpy.data.materials.get(wid + "_Main") or bpy.data.materials.new(wid + "_Main")
for ob in objs.values():
    if not ob.data.materials:
        ob.data.materials.append(None)
    for i, slot in enumerate(ob.material_slots):
        if main:
            slot.material = main
            continue
        tgt = classify(slot.material, ob.name)
        rgba, met, sm = PALETTE[tgt]
        slot.material = shared(tgt, rgba, met, sm)
    # merge duplicate slots
    ob.select_set(False)

# ---- hierarchy + sockets
root = bpy.data.objects.new(wid, None)
bpy.context.scene.collection.objects.link(root)
for ob in objs.values():
    ob.parent = root
grip_rot = (math.radians(90.0 + rake), 0.0, 0.0)
for k, p in S.items():
    e = bpy.data.objects.new(k, None)
    e.empty_display_type = 'ARROWS'; e.empty_display_size = 0.012
    bpy.context.scene.collection.objects.link(e)
    e.parent = root
    e.location = b(p.x, p.y, p.z)
    if k == "Grip_R" or (k == "Grip_L" and kind in ("pistol", "revolver")):
        e.rotation_euler = grip_rot

# ---- export (project settings, wlib/export.py)
bpy.ops.object.select_all(action='DESELECT')
for ob in [root] + list(root.children):
    ob.select_set(True)
bpy.context.view_layer.objects.active = root
os.makedirs(os.path.dirname(out_fbx), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={'EMPTY', 'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
    axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True, mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False)

# ---- manifest-style entry (Unity space = (x, y, z) = (w, v, u))
def u3(p):
    return [round(p.z, 5) + 0.0, round(p.y, 5) + 0.0, round(p.x, 5) + 0.0]
deps = bpy.context.evaluated_depsgraph_get()
meshes = {}
tris_total = 0
for nm, ob in objs.items():
    me = ob.data
    tris = sum(len(pl.vertices) - 2 for pl in me.polygons)
    tris_total += tris
    vs = verts(ob)
    lo3, hi3 = box(vs)
    meshes[nm] = {"origin": u3(g(ob.matrix_world.translation)), "triangles": tris, "materials": [m.name for m in me.materials if m],
                  "boundsMin": u3(lo3), "boundsMax": u3(hi3)}
sockets = {}
for k, p in S.items():
    if k == "Grip_R" or (k == "Grip_L" and kind in ("pistol", "revolver")):
        rr = math.radians(rake)
        sockets[k] = {"position": u3(p), "forward": [0.0, -round(math.cos(rr), 5), -round(math.sin(rr), 5)], "up": [0.0, -round(math.sin(rr), 5), round(math.cos(rr), 5)]}
    else:
        sockets[k] = {"position": u3(p), "forward": [0.0, 0.0, 1.0], "up": [0.0, 1.0, 0.0]}
allv2 = [p for ob in objs.values() for p in verts(ob)]
lo4, hi4 = box(allv2)
entry = {"id": wid, "kind": "weapon", "slot": None, "file": "Assets/_Game/Art/Weapons/Models/%s.fbx" % wid, "triangles": tris_total,
         "dimensions": [round(hi4.z - lo4.z, 5), round(hi4.y - lo4.y, 5), round(hi4.x - lo4.x, 5)],
         "boundsMin": u3(lo4), "boundsMax": u3(hi4), "meshes": meshes, "sockets": sockets,
         "mag_insert": [0.0, 1.0, 0.0], "source": os.path.basename(cfg["src"])}
entry.update(cfg.get("meta", {}))
json.dump(entry, open(out_json, "w"), indent=1)
bpy.ops.wm.save_as_mainfile(filepath=out_fbx[:-4] + "_final.blend")

# ---- preview: right side view with socket markers
for k, p in S.items():
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.006, location=b(p.x, p.y, p.z))
    s = bpy.context.active_object
    m = bpy.data.materials.new("mk_" + k); m.diffuse_color = {"Socket_Muzzle": (1, 0, 0, 1), "Socket_Optic": (0, 1, 0, 1), "Grip_R": (0, 0, 1, 1), "Grip_L": (0, 1, 1, 1),
        "Socket_Mag": (1, 1, 0, 1), "Socket_Underbarrel": (1, 0, 1, 1), "Socket_Side": (1, 0.5, 0, 1), "Eject": (1, 1, 1, 1), "Sight_Iron": (0.5, 1, 0.5, 1)}.get(k, (1, 1, 1, 1))
    s.data.materials.append(m)
cam_d = bpy.data.cameras.new("c"); cam_d.type = 'ORTHO'
cam = bpy.data.objects.new("c", cam_d); bpy.context.scene.collection.objects.link(cam)
ctr = b((lo4.x + hi4.x) / 2, (lo4.y + hi4.y) / 2, 0.0)
cam.location = ctr + Vector((-1.0, 0, 0))          # from the gun's right (-X)
cam.rotation_euler = (math.radians(90), 0, math.radians(-90))
cam_d.ortho_scale = max(hi4.x - lo4.x, hi4.y - lo4.y) * 1.1
sc = bpy.context.scene; sc.camera = cam
sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.color_type = 'MATERIAL'
sc.render.resolution_x = 1100; sc.render.resolution_y = int(1100 * (hi4.y - lo4.y) / (hi4.x - lo4.x) * 1.0) + 60
sc.render.filepath = out_png
bpy.ops.render.render(write_still=True)
print("FINISH", wid, {k: [round(c * 1000) for c in u3(p)] for k, p in S.items()})
