# Blender (background): right-side ortho render of a prepared weapon with a 10 mm grid in gun space
# (u = forward from the rearmost point, v = up from the lowest point), labels every 50 mm.
# usage: blender -b --factory-startup --disable-autoexec --python grid_render.py -- prep.blend out.png
import bpy, sys, math
from mathutils import Vector

a = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0], load_ui=False, use_scripts=False)
out = a[1]
objs = [o for o in bpy.data.objects if o.type == 'MESH']
pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
umin = min(-p.y for p in pts); umax = max(-p.y for p in pts)
vmin = min(p.z for p in pts); vmax = max(p.z for p in pts)
# colour parts
cols = {"Body": (0.75, 0.75, 0.78, 1), "Mag": (0.9, 0.75, 0.2, 1), "Bolt": (0.3, 0.8, 0.3, 1), "Slide": (0.3, 0.8, 0.3, 1), "IronSights": (0.3, 0.5, 1, 1)}
for o in objs:
    m = bpy.data.materials.new("pc_" + o.name); m.diffuse_color = cols.get(o.name, (0.8, 0.4, 0.8, 1))
    o.data.materials.clear(); o.data.materials.append(m)
# grid lines as thin boxes on the far side (+X is the gun's left; camera on -X looks at the right side)
def line(p0, p1, w, col):
    me = bpy.data.meshes.new("l"); ob = bpy.data.objects.new("l", me); bpy.context.scene.collection.objects.link(ob)
    import bmesh
    bm = bmesh.new()
    d = (p1 - p0); n = Vector((0, -d.z, d.y)).normalized() * w
    vs = [bm.verts.new(p0 - n), bm.verts.new(p1 - n), bm.verts.new(p1 + n), bm.verts.new(p0 + n)]
    bm.faces.new(vs); bm.to_mesh(me); bm.free()
    m = bpy.data.materials.new("g"); m.diffuse_color = col; me.materials.append(m)
X = 0.2
u0 = math.floor(umin * 100) / 100; u1 = math.ceil(umax * 100) / 100
v0 = math.floor(vmin * 100) / 100; v1 = math.ceil(vmax * 100) / 100
k = 0
u = u0
while u <= u1 + 1e-6:
    mm = round((u - umin) * 1000)
    major = abs(round(u * 1000) % 50) < 1
    line(Vector((X, -u, v0)), Vector((X, -u, v1)), 0.0006 if major else 0.00025, (0.9, 0.2, 0.2, 1) if major else (0.35, 0.35, 0.35, 1))
    u += 0.01
v = v0
while v <= v1 + 1e-6:
    major = abs(round(v * 1000) % 50) < 1
    line(Vector((X, -u0, v)), Vector((X, -u1, v)), 0.0006 if major else 0.00025, (0.2, 0.4, 0.95, 1) if major else (0.35, 0.35, 0.35, 1))
    v += 0.01
SC = {"Socket_Muzzle": (1, 0, 0, 1), "Socket_Optic": (0, 1, 0, 1), "Grip_R": (0, 0, 1, 1), "Grip_L": (0, 1, 1, 1),
      "Socket_Mag": (1, 1, 0, 1), "Socket_Underbarrel": (1, 0, 1, 1), "Socket_Side": (1, 0.5, 0, 1), "Eject": (1, 1, 1, 1), "Sight_Iron": (0.2, 0.6, 0.2, 1)}
for e in [o for o in bpy.data.objects if o.type == 'EMPTY' and o.parent]:
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.0045, location=e.matrix_world.translation + Vector((-0.06, 0, 0)))
    s = bpy.context.active_object
    m = bpy.data.materials.new("mk"); m.diffuse_color = SC.get(e.name, (1, 1, 1, 1)); s.data.materials.append(m)
cam_d = bpy.data.cameras.new("c"); cam_d.type = 'ORTHO'
cam = bpy.data.objects.new("c", cam_d); bpy.context.scene.collection.objects.link(cam)
cam.location = Vector((-1.0, -(u0 + u1) / 2, (v0 + v1) / 2))
cam.rotation_euler = (math.radians(90), 0, math.radians(-90))
cam_d.ortho_scale = max(u1 - u0, v1 - v0) * 1.02
sc = bpy.context.scene; sc.camera = cam
sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.color_type = 'MATERIAL'; sc.display.shading.light = 'FLAT'
sc.render.resolution_x = 1600; sc.render.resolution_y = int(1600 * (v1 - v0) / (u1 - u0)) + 2
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("GRID", out, "u0(m) %.3f u1 %.3f v0 %.3f v1 %.3f  (red lines every 50 mm of u, blue every 50 mm of v; absolute Blender metres)" % (u0, u1, v0, v1))
