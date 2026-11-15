"""
export.py - FBX export (project settings) and the JSON manifest.

Objects carry their final (export) name in obj['export_name']; in the .blend they are suffixed
with '__<AssetId>' so that every asset can keep children called Body, Mag, Socket_Muzzle ...
They are renamed to the exact export name only while their asset is being exported.
"""
import json
import math
import os

import bpy
from mathutils import Vector

from .core import b2unity


def _hierarchy(root):
    return [root] + list(root.children_recursive)


def export_fbx(root, filepath):
    objs = _hierarchy(root)
    os.makedirs(os.path.dirname(filepath), exist_ok=True)
    renamed = []
    try:
        for o in objs[1:]:
            en = o.get('export_name')
            if not en:
                continue
            renamed.append((o, o.name, o.data.name if o.data is not None else None))
        # free the target names first (two passes avoid collisions inside the asset)
        for o, old, oldd in renamed:
            o.name = '__tmp__' + old
        for o, old, oldd in renamed:
            o.name = o['export_name']
            if o.data is not None:
                o.data.name = o['export_name']
            if o.name != o['export_name']:
                raise RuntimeError(f"could not rename {old} -> {o['export_name']} (got {o.name})")
        for o in bpy.context.selected_objects:
            o.select_set(False)
        for o in objs:
            o.hide_set(False)
            o.select_set(True)
        bpy.context.view_layer.objects.active = root
        bpy.ops.export_scene.fbx(
            filepath=filepath, use_selection=True, object_types={'EMPTY', 'MESH'},
            apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
            axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True, mesh_smooth_type='FACE',
            add_leaf_bones=False, bake_anim=False)
    finally:
        for o, old, oldd in renamed:
            o.name = old
            if o.data is not None and oldd is not None:
                o.data.name = oldd
        for o in objs:
            o.select_set(False)


# ------------------------------------------------------------------------------ measurements
def r5(x):
    return round(float(x), 5)


def vec(v):
    return [r5(c) + 0.0 for c in v]


def mesh_stats(ob):
    """triangles, world-space vertex bounds (Blender), material names (evaluated mesh)."""
    deps = bpy.context.evaluated_depsgraph_get()
    oe = ob.evaluated_get(deps)
    me = oe.to_mesh()
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    mw = ob.matrix_world
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for v in me.vertices:
        p = mw @ v.co
        for i in range(3):
            lo[i] = min(lo[i], p[i])
            hi[i] = max(hi[i], p[i])
    mats = [m.name for m in ob.data.materials if m]
    oe.to_mesh_clear()
    return tris, lo, hi, mats


def unity_bounds(lo, hi):
    a = b2unity(lo)
    b = b2unity(hi)
    mn = [min(x, y) for x, y in zip(a, b)]
    mx = [max(x, y) for x, y in zip(a, b)]
    return mn, mx


def asset_entry(asset, rel_path):
    root = asset.root
    meshes = {}
    sockets = {}
    total = 0
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in root.children_recursive:
        name = o.get('export_name', o.name)
        if o.type == 'MESH':
            tris, l, h, mats = mesh_stats(o)
            total += tris
            for i in range(3):
                lo[i] = min(lo[i], l[i])
                hi[i] = max(hi[i], h[i])
            mn, mx = unity_bounds(l, h)
            meshes[name] = {'origin': vec(b2unity(o.matrix_world.translation)), 'triangles': tris,
                            'materials': mats, 'boundsMin': vec(mn), 'boundsMax': vec(mx)}
        elif o.type == 'EMPTY':
            m3 = o.matrix_world.to_3x3()
            fwd = (m3 @ Vector((0.0, -1.0, 0.0))).normalized()
            up = (m3 @ Vector((0.0, 0.0, 1.0))).normalized()
            sockets[name] = {'position': vec(b2unity(o.matrix_world.translation)),
                             'forward': vec(b2unity(fwd)), 'up': vec(b2unity(up))}
    mn, mx = unity_bounds(lo, hi)
    e = dict(asset.meta)
    e['file'] = rel_path
    e['triangles'] = total
    e['dimensions'] = vec([mx[i] - mn[i] for i in range(3)])
    e['boundsMin'] = vec(mn)
    e['boundsMax'] = vec(mx)
    e['meshes'] = meshes
    e['sockets'] = sockets
    return e


def write_manifest(entries, path):
    doc = {
        'generator': 'Tools/Blender/weapons/build_weapons.py',
        'units': 'meters',
        'coordinates': 'Unity local space of the imported prefab root: +X right, +Y up, +Z forward (muzzle). '
                       'Sockets: forward = transform.forward, up = transform.up.',
        'assets': entries,
    }
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(doc, f, indent=2)
