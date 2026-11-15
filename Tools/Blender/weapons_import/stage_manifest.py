# Builds Assets/_Game/Art/Weapons/weapons_manifest_imported.json from the per-asset entries (run with Blender's python).
import json, os, sys
a = sys.argv[sys.argv.index("--") + 1:]
work, out = a[0], a[1]
W = ["AKM", "HK416", "MK47", "QBZ191", "MK18", "HoneyBadger", "Vector", "MPX", "Scorpion", "SR2", "M249", "Deagle", "K6S", "R870",
     "SVD", "AWM", "VSS", "AXMC", "RPG7"]
MOVING = {"AKM": {"Bolt": {"travel": 0.07}}, "HK416": {"Bolt": {"travel": 0.07}}, "MK47": {"Bolt": {"travel": 0.075}},
          "QBZ191": {"Bolt": {"travel": 0.07}}, "MK18": {"Bolt": {"travel": 0.065}}, "HoneyBadger": {"Bolt": {"travel": 0.065}},
          "MPX": {"Bolt": {"travel": 0.06}}, "Scorpion": {"Bolt": {"travel": 0.06}}, "SR2": {"Bolt": {"travel": 0.05}},
          "M249": {"Bolt": {"travel": 0.09}}, "Deagle": {"Slide": {"travel": 0.045}}, "R870": {"Pump": {"travel": 0.09}},
          "AWM": {"Bolt": {"travel": 0.08}}}
CAP = {"AKM": 30, "HK416": 30, "MK47": 30, "QBZ191": 30, "MK18": 30, "HoneyBadger": 30, "Vector": 25, "MPX": 30, "Scorpion": 30, "SR2": 30,
       "M249": 100, "Deagle": 7, "K6S": 6, "R870": 6, "SVD": 10, "AWM": 5, "VSS": 20, "AXMC": 5, "RPG7": 1}
assets = []
for w in W:
    e = json.load(open(os.path.join(work, "entries", w + ".json")))
    e["file"] = "Assets/_Game/Art/Weapons/Models/%s.fbx" % w
    e["capacity"] = CAP[w]
    if w in MOVING: e["moving"] = MOVING[w]
    if w == "Deagle": e["socket_parent_hint"] = {"Socket_Optic": "Slide"}
    if w in ("SVD", "AWM", "VSS", "AXMC", "RPG7"):
        for m in e["meshes"].values(): m["materials"] = [w + "_Main"]
    assets.append(e)
for f in sorted(os.listdir(os.path.join(work, "att"))):
    if f.endswith(".json"):
        assets.append(json.load(open(os.path.join(work, "att", f))))
NEW_R1 = ["AKM", "HK416", "MK47", "QBZ191", "MK18", "HoneyBadger", "Vector", "MPX", "Scorpion", "SR2", "M249", "SVD", "VSS"]
GRIPS = ["HK416", "MK18", "QBZ191", "MK47", "AKM", "HoneyBadger", "Vector", "MPX", "Scorpion", "SR2", "M249"]
SIDE = ["AKM", "HK416", "MK47", "QBZ191", "MK18", "HoneyBadger", "Vector", "MPX", "Scorpion", "M249", "SVD", "AWM", "VSS", "AXMC"]
compat = {
    "Optic_RedDot": NEW_R1, "Optic_Holo": NEW_R1,
    "Optic_Prism4x": ["AKM", "HK416", "MK47", "QBZ191", "MK18", "M249", "SVD", "VSS", "HoneyBadger"],
    "Optic_Scope8x": ["AWM", "AXMC", "SVD", "VSS", "HK416", "MK47", "QBZ191", "AKM"],
    "Optic_PistolDot": ["Deagle"],
    "Muzzle_FlashHider": ["HK416", "MK18", "QBZ191", "M249", "MK47", "AKM"],
    "Muzzle_Brake": ["HK416", "MK18", "QBZ191", "MK47", "AKM", "AWM", "AXMC", "SVD"],
    "Muzzle_PistolComp": ["Deagle", "MPX", "Scorpion"],
    "Suppressor_Rifle": ["HK416", "MK18", "QBZ191", "MK47", "AKM", "M249"],
    "Suppressor_Pistol": ["MPX", "Scorpion", "Vector", "SR2"],
    "Suppressor_Sniper": ["AWM", "AXMC", "SVD"],
    "Grip_Vertical": GRIPS, "Grip_Angled": GRIPS,
    "Bipod": ["AWM", "AXMC", "SVD", "M249", "MK47"],
    "Laser_Module": SIDE, "Flashlight": SIDE,
    "Pistol_LaserLight": ["Deagle"],
}
doc = {"generator": "imported packs (Blender conversion, see Animation/README)", "units": "meters",
       "coordinates": "Unity local space of the imported prefab root: +X right, +Y up, +Z forward (muzzle).",
       "assets": assets, "compat_add": compat}
json.dump(doc, open(out, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
print("MANIFEST", len(assets), "assets")
