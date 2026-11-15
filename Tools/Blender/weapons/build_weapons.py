"""
build_weapons.py - rebuilds every weapon + attachment from an empty scene, bakes one PBR texture atlas per
asset (Cycles, GPU), exports one FBX per asset into the Unity project, writes weapons_manifest.json and
saves weapons.blend (one collection per asset).

Run (Git Bash):
  "/e/SteamLibrary/steamapps/common/Blender/blender.exe" --background --factory-startup \
      --python "C:/Project/007/Tools/Blender/weapons/build_weapons.py" -- [options]

Options (after the "--"):
  --only ID,ID,...     build only these asset ids (no .blend/manifest is saved unless --force-save)
  --no-export          do not write FBX files / manifest
  --export-dir DIR     write FBX/textures/manifest under DIR instead of Assets/_Game/Art/Weapons (testing)
  --no-bake            skip texture baking (fast geometry iteration; FBX then keep the source materials)
  --samples N          bake samples (default 64; AO uses 2N)
  --res-scale F        multiply texture resolutions (e.g. 0.25 for quick tests)
  --preview DIR        textured Cycles review renders (catalog left/right side views, 3/4 views, kits,
                       attachment sheet) into DIR
  --quick-preview DIR  Workbench review renders with socket markers (no textures needed)
  --no-blend           do not save weapons.blend
  --force-save         save .blend/manifest even with --only
  --from-blend         do not rebuild: open weapons.blend (e.g. after manual edits) and export it as is
  --rebake             with --from-blend: bake the textures again

Pipeline per asset: model (wlib/w_*.py, attachments.py; "gun space" mm, see core.py) -> pipeline.finalize
(bevel modifier with harden normals, weighted normals, triangulate, ONE shared UV atlas) -> texturing.bake
(BaseColor / Normal (OpenGL) / MetallicSmoothness / Occlusion) -> export (all baked surfaces use the single
material '<Id>_Main'; Glass_Lens / Reticle_Red / Emitter_* stay separate).
In the .blend the children are suffixed "__<AssetId>" (Body__M4A1 ...) and the faces keep their procedural
source materials; exact export names and the <Id>_Main material are swapped in only while exporting.
"""
import json
import os
import sys
import time
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
sys.dont_write_bytecode = True        # keep the tool folder free of __pycache__
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import bpy  # noqa: E402

from wlib import core, export, preview, attachments, pipeline, texturing  # noqa: E402
from wlib import w_m4a1, w_ak12, w_m14ebr, w_m700, w_glock17  # noqa: E402

PROJECT = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
ART_REL = 'Assets/_Game/Art/Weapons'
BLEND_PATH = os.path.join(HERE, 'weapons.blend')

core.SEG_SCALE = 1.25
core.MATERIAL_FACTORY = texturing.material_factory

WEAPONS = [
    ('M4A1', w_m4a1.build),
    ('AK12', w_ak12.build),
    ('M14EBR', w_m14ebr.build),
    ('M700', w_m700.build),
    ('Glock17', w_glock17.build),
]
ATTACHMENTS = attachments.BUILDERS

# review configurations: weapon -> attachments mounted at sockets (+ weapon children hidden)
PREVIEW_SETS = [
    ('M4A1', 'kit', {'Socket_Optic': 'Optic_RedDot', 'Socket_Underbarrel': 'Grip_Vertical',
                     'Socket_Side': 'Laser_Module', 'Socket_Muzzle': 'Suppressor_Rifle'}, ['IronSights']),
    ('AK12', 'kit', {'Socket_Optic': 'Optic_Holo', 'Socket_Underbarrel': 'Grip_Angled',
                     'Socket_Muzzle': 'Muzzle_Brake', 'Socket_Mag': 'Mag_AK12_Ext'}, ['IronSights', 'Mag']),
    ('M14EBR', 'kit', {'Socket_Optic': 'Optic_Prism4x', 'Socket_Underbarrel': 'Bipod',
                       'Socket_Side': 'Flashlight'}, ['IronSights']),
    ('M700', 'kit', {'Socket_Optic': 'Optic_Scope8x', 'Socket_Muzzle': 'Suppressor_Sniper',
                     'Socket_Underbarrel': 'Bipod'}, []),
    ('Glock17', 'kit', {'Socket_Optic': 'Optic_PistolDot', 'Socket_Underbarrel': 'Pistol_LaserLight',
                        'Socket_Muzzle': 'Muzzle_PistolComp'}, []),
    ('M4A1', 'kit2', {'Socket_Optic': 'Optic_Prism4x', 'Socket_Muzzle': 'Suppressor_Rifle',
                      'Socket_Side': 'Flashlight', 'Socket_Mag': 'Mag_M4A1_Ext',
                      'Socket_Underbarrel': 'Grip_Angled'}, ['IronSights', 'Mag']),
    ('AK12', 'kit2', {'Socket_Optic': 'Optic_RedDot', 'Socket_Side': 'Laser_Module',
                      'Socket_Muzzle': 'Suppressor_Rifle', 'Socket_Underbarrel': 'Grip_Vertical'}, ['IronSights']),
    ('Glock17', 'kit2', {'Socket_Muzzle': 'Suppressor_Pistol', 'Socket_Mag': 'Mag_Glock17_Ext'}, ['Mag']),
]
# how each weapon is shown in the "catalog" renders (its default configuration)
CATALOG = {
    'M4A1': ({'Socket_Muzzle': 'Muzzle_FlashHider'}, []),
    'AK12': ({}, []),
    'M14EBR': ({}, []),
    'M700': ({'Socket_Optic': 'Optic_Scope8x', 'Socket_Underbarrel': 'Bipod'}, []),
    'Glock17': ({}, []),
}


def parse_args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    opts = {'only': None, 'export': True, 'export_dir': None, 'preview': None, 'quick_preview': None,
            'blend': True, 'force_save': False, 'from_blend': False, 'bake': True, 'rebake': False,
            'samples': 64, 'res_scale': 1.0}
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == '--from-blend':
            opts['from_blend'] = True
        elif a == '--rebake':
            opts['rebake'] = True
        elif a == '--only':
            opts['only'] = set(argv[i + 1].split(','))
            i += 1
        elif a == '--no-export':
            opts['export'] = False
        elif a == '--export-dir':
            opts['export_dir'] = argv[i + 1]
            i += 1
        elif a == '--no-bake':
            opts['bake'] = False
        elif a == '--samples':
            opts['samples'] = int(argv[i + 1])
            i += 1
        elif a == '--res-scale':
            opts['res_scale'] = float(argv[i + 1])
            i += 1
        elif a == '--preview':
            opts['preview'] = argv[i + 1]
            i += 1
        elif a == '--quick-preview':
            opts['quick_preview'] = argv[i + 1]
            i += 1
        elif a == '--no-blend':
            opts['blend'] = False
        elif a == '--force-save':
            opts['force_save'] = True
        i += 1
    return opts


class LoadedAsset:
    """An asset found in an opened weapons.blend (for --from-blend)."""

    def __init__(self, root):
        self.root = root
        self.id = root.name
        self.kind = root.get('asset_kind', 'attachment')
        self.meta = json.loads(root.get('meta_json', '{}')) or {'id': self.id, 'kind': self.kind}
        self.coll = root.users_collection[0]
        self.bevel_mm = 0.0


def texture_res(a, scale):
    base = 2048 if (a.kind == 'weapon' and a.id != 'Glock17') else 1024
    return max(128, int(base * scale))


def build_all(opts):
    core.reset_scene()
    assets, order = {}, []
    for kind, table in (('weapon', WEAPONS), ('attachment', ATTACHMENTS)):
        for aid, fn in table:
            if opts['only'] and aid not in opts['only']:
                continue
            try:
                a = fn()
            except Exception:
                print(f"[build] FAILED {aid}")
                traceback.print_exc()
                raise
            assets[aid] = a
            order.append(aid)
    return assets, order


def load_blend(opts):
    bpy.ops.wm.open_mainfile(filepath=BLEND_PATH)
    for lc in bpy.context.view_layer.layer_collection.children:
        lc.exclude = False
        lc.hide_viewport = False
    roots = {o.name: o for o in bpy.data.objects if o.parent is None and 'asset_kind' in o.keys()}
    known = [aid for aid, _ in WEAPONS + ATTACHMENTS]
    order = [a for a in known if a in roots] + sorted(a for a in roots if a not in known)
    if opts['only']:
        order = [a for a in order if a in opts['only']]
    return {a: LoadedAsset(roots[a]) for a in order}, order


def main():
    opts = parse_args()
    T = {}
    t_all = time.time()
    out_root = opts['export_dir'] or os.path.join(PROJECT, ART_REL)

    t = time.time()
    if opts['from_blend']:
        assets, order = load_blend(opts)
        opts['blend'] = False                      # never overwrite the hand-edited file
        do_bake = opts['rebake']
    else:
        assets, order = build_all(opts)
        T['model'] = time.time() - t
        t = time.time()
        for aid in order:
            pipeline.finalize(assets[aid], texture_res(assets[aid], opts['res_scale']))
        T['bevel+uv'] = time.time() - t
        do_bake = opts['bake']
    for aid in order:
        a = assets[aid]
        a.root['meta_json'] = json.dumps(a.meta)

    if do_bake:
        t = time.time()
        dev = texturing.setup_cycles(opts['samples'])
        print(f"[bake] device: {dev}")
        for aid in order:
            a = assets[aid]
            t1 = time.time()
            sub = f"Textures/{aid}"
            res = texture_res(a, opts['res_scale'])
            paths = texturing.bake_asset(a, os.path.join(out_root, *sub.split('/')), res,
                                         samples=opts['samples'], ao_samples=opts['samples'] * 2)
            texturing.make_main_material(a, paths)
            a.meta['material'] = f'{aid}_Main'
            a.meta['textures'] = {k: f"{ART_REL}/{sub}/{os.path.basename(p)}" for k, p in paths.items()}
            a.meta['textureResolution'] = res
            a.root['meta_json'] = json.dumps(a.meta)
            print(f"[bake] {aid:20s} {res}px {time.time() - t1:5.1f}s")
        T['bake'] = time.time() - t

    full = opts['only'] is None or opts['force_save']
    if opts['export']:
        t = time.time()
        entries = []
        with texturing.FinalMaterials([assets[a] for a in order]):
            for aid in order:
                a = assets[aid]
                sub = 'Models' if a.kind == 'weapon' else 'Attachments'
                rel = f"{ART_REL}/{sub}/{aid}.fbx"
                path = os.path.join(out_root, sub, f"{aid}.fbx")
                export.export_fbx(a.root, path)
                e = export.asset_entry(a, rel)
                entries.append(e)
                print(f"[export] {aid:20s} tris={e['triangles']:6d} dims(m)={e['dimensions']}")
        if full:
            export.write_manifest(entries, os.path.join(out_root, 'weapons_manifest.json'))
            print('[export] manifest written')
        T['export'] = time.time() - t

    if opts['quick_preview']:
        pdir = opts['quick_preview']
        os.makedirs(pdir, exist_ok=True)
        preview.setup_render()
        with preview.ReviewPalette():
            for aid in order:
                if assets[aid].kind == 'weapon':
                    preview.render_config(assets, aid, os.path.join(pdir, f"{aid}_q"), views=('right', 'left', '34'))
            att = [assets[a] for a in order if assets[a].kind == 'attachment']
            if att:
                preview.render_contact_sheet(att, os.path.join(pdir, 'attachments_q_sheet.png'))
        preview.show_only([lc.collection.name for lc in bpy.context.view_layer.layer_collection.children])

    if opts['preview']:
        t = time.time()
        pdir = opts['preview']
        os.makedirs(pdir, exist_ok=True)
        preview.studio_setup()
        with texturing.FinalMaterials([assets[a] for a in order]):
            for aid in order:
                if assets[aid].kind != 'weapon':
                    continue
                mounts, hide = CATALOG.get(aid, ({}, []))
                mounts = {s: m for s, m in mounts.items() if m in assets}
                preview.render_config(assets, aid, os.path.join(pdir, f"{aid}_v3"), mounts=mounts, hide=hide,
                                      views=('left', 'right', '34'), markers=False)
            for wid, tag, mounts, hide in PREVIEW_SETS:
                if wid in assets and all(m in assets for m in mounts.values()):
                    preview.render_config(assets, wid, os.path.join(pdir, f"{wid}_v3_{tag}"), mounts=mounts,
                                          hide=hide, views=('right', '34'), markers=False)
            att = [assets[a] for a in order if assets[a].kind == 'attachment']
            if att:
                preview.render_contact_sheet(att, os.path.join(pdir, 'attachments_v3_sheet.png'), markers=False)
        preview.studio_cleanup()
        preview.show_only([lc.collection.name for lc in bpy.context.view_layer.layer_collection.children])
        T['preview'] = time.time() - t

    if opts['blend'] and full:
        for i, lc in enumerate(bpy.context.view_layer.layer_collection.children):
            lc.exclude = False
            lc.hide_viewport = i != 0
        bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH, compress=True)
        print(f"[blend] saved {BLEND_PATH}")
    T['total'] = time.time() - t_all
    print('[time] ' + '  '.join(f"{k}={v:.1f}s" for k, v in T.items()))
    print(f"[done] {len(order)} assets")


if __name__ == '__main__':
    try:
        main()
    except Exception:
        traceback.print_exc()
        sys.exit(1)
