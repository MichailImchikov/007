"""
pipeline.py - per-asset finalisation after modelling:
    bevel modifier (angle limited, harden normals) applied -> weighted normals + triangulate (kept as
    modifiers, applied by the FBX exporter) -> one shared UV atlas for all meshes of the asset.
"""
import math

import bpy


def asset_meshes(asset):
    return [o for o in asset.root.children_recursive if o.type == 'MESH']


class Isolate:
    """Exclude every other asset collection from the view layer (bakes/AO must not see them)."""

    def __init__(self, *assets):
        self.colls = {a.coll.name for a in assets}

    def __enter__(self):
        self.saved = {}
        for lc in bpy.context.view_layer.layer_collection.children:
            self.saved[lc.name] = (lc.exclude, lc.hide_viewport)
            lc.exclude = lc.collection.name not in self.colls
            if not lc.exclude:
                lc.hide_viewport = False
        return self

    def __exit__(self, *exc):
        for lc in bpy.context.view_layer.layer_collection.children:
            if lc.name in self.saved:
                lc.exclude, lc.hide_viewport = self.saved[lc.name]


def activate(objs):
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    for o in list(bpy.context.selected_objects):
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def apply_bevel(ob, width_m, segments=1, angle=50.0):
    """Chamfer the remaining hard edges (> angle). Edges that already carry a modelled chamfer (45 deg) are
    left alone; rails, knurls, screws, pins and magazine ribs are excluded via the 'NoBevel' group."""
    mod = ob.modifiers.new('Bevel', 'BEVEL')
    mod.width = width_m
    mod.segments = segments
    mod.limit_method = 'ANGLE'
    mod.angle_limit = math.radians(angle)
    mod.harden_normals = True
    mod.use_clamp_overlap = True
    mod.profile = 0.5
    mod.miter_outer = 'MITER_ARC'
    if 'NoBevel' in ob.vertex_groups:
        mod.vertex_group = 'NoBevel'
        mod.invert_vertex_group = True
    activate([ob])
    bpy.ops.object.modifier_apply(modifier=mod.name)
    if 'NoBevel' in ob.vertex_groups:
        ob.vertex_groups.remove(ob.vertex_groups['NoBevel'])


def cleanup(ob):
    """Remove collinear vertices (they triangulate into zero-area slivers) and degenerate geometry.
    Custom normals from the bevel are rebuilt afterwards by the WeightedNormal modifier."""
    import bmesh
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(0.25), verts=list(bm.verts), edges=[],
                             delimit={'MATERIAL', 'SHARP'})
    bmesh.ops.dissolve_degenerate(bm, dist=1e-5, edges=list(bm.edges))
    bm.to_mesh(me)
    bm.free()
    me.update()


def add_export_modifiers(ob):
    wn = ob.modifiers.new('WeightedNormal', 'WEIGHTED_NORMAL')
    wn.mode = 'FACE_AREA'
    wn.weight = 50
    wn.keep_sharp = True
    tri = ob.modifiers.new('Triangulate', 'TRIANGULATE')
    tri.quad_method = 'BEAUTY'
    tri.ngon_method = 'BEAUTY'
    tri.keep_custom_normals = True


def unwrap_atlas(objs, res, margin_px=6):
    """Smart-project all meshes of the asset together and pack them into ONE 0-1 atlas."""
    activate(objs)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    m = margin_px / float(res)
    bpy.ops.uv.smart_project(angle_limit=math.radians(60.0), margin_method='FRACTION', island_margin=m,
                             area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(rotate=True, margin_method='FRACTION', margin=m, shape_method='CONCAVE')
    bpy.ops.object.mode_set(mode='OBJECT')


def finalize(asset, res, segments=1, margin_px=8):
    import time
    objs = asset_meshes(asset)
    t0 = time.time()
    with Isolate(asset):
        for ob in objs:
            w = float(ob.get('bevel_mm', asset.bevel_mm)) * 0.001
            if w > 0:
                apply_bevel(ob, w, int(ob.get('bevel_seg', segments)))
            cleanup(ob)
            add_export_modifiers(ob)
        t1 = time.time()
        unwrap_atlas(objs, res, margin_px)
    print(f"[finalize] {asset.id:20s} bevel {t1 - t0:5.1f}s  uv {time.time() - t1:5.1f}s")
