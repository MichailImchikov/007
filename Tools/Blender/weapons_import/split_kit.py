# Blender (background): split the AR customization kit (one merged mesh laid out on a grid) into one object per module.
# usage: blender -b --factory-startup --disable-autoexec --python split_kit.py -- kit.fbx out.blend report.json
import bpy, bmesh, sys, json, mathutils

a = sys.argv[sys.argv.index("--") + 1:]
src, out_blend, report = a[0], a[1], a[2]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
for o in list(bpy.context.scene.objects):
    if o.type != 'MESH':
        bpy.data.objects.remove(o)
src_obj = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
# bake the import transform so world == local
bpy.context.view_layer.objects.active = src_obj
src_obj.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# grid of the kit (world X columns, Z rows), read off a front render
COLS = {"pgrip": -32.05, "fgrip": -28.07, "muzzle": -24.60, "mag": -20.01, "handguard": -16.19, "stock": -11.77, "optic": -7.53}
ROWS = [11.9, 9.1, 8.05, 6.06, 4.06, 2.07, 0.08]

bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.separate(type='LOOSE')
bpy.ops.object.mode_set(mode='OBJECT')
parts = [o for o in bpy.context.scene.objects if o.type == 'MESH']

cells = {}
skipped = 0
for p in parts:
    bb = [p.matrix_world @ mathutils.Vector(c) for c in p.bound_box]
    c = sum(bb, mathutils.Vector()) / 8
    if c.x > -4.0:  # the assembled example rifles on the right
        skipped += 1
        bpy.data.objects.remove(p)
        continue
    col = min(COLS, key=lambda k: abs(COLS[k] - c.x))
    row = min(range(len(ROWS)), key=lambda i: abs(ROWS[i] - c.z))
    cells.setdefault((col, row), []).append(p)

info = []
for (col, row), ps in sorted(cells.items()):
    bpy.ops.object.select_all(action='DESELECT')
    for p in ps:
        p.select_set(True)
    bpy.context.view_layer.objects.active = ps[0]
    if len(ps) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = "kit_%s_%d" % (col, row)
    bb = [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
    mn = [min(v[i] for v in bb) for i in range(3)]
    mx = [max(v[i] for v in bb) for i in range(3)]
    info.append({"name": o.name, "parts": len(ps), "verts": len(o.data.vertices),
                 "size": [round(mx[i] - mn[i], 3) for i in range(3)],
                 "center": [round((mx[i] + mn[i]) / 2, 3) for i in range(3)],
                 "mats": [m.name for m in o.data.materials if m]})

bpy.ops.wm.save_as_mainfile(filepath=out_blend)
json.dump({"skipped_assembled_parts": skipped, "modules": info}, open(report, "w"), indent=1)
print("SPLIT", len(info), "modules, skipped", skipped)
