import json
W = r"{WORK}/weapons_src"; K = r"{WORK}/work/kit_split.blend"
R1 = ["AKM","HK416","MK47","QBZ191","MK18","HoneyBadger","Vector","MPX","Scorpion","SR2","M249","M4A1","AK12","M14EBR","SVD","VSS"]
RIF = ["AKM","HK416","MK47","QBZ191","MK18","M4A1","AK12","M14EBR","M249"]
GRIPS = ["HK416","MK18","QBZ191","MK47","AKM","HoneyBadger","Vector","MPX","Scorpion","SR2","M249","M4A1","AK12","M14EBR"]
KM = {"Basic_mat": "Metal_Black", "Sec_mat": "Metal_Dark", "White_mat": "Metal_Worn", "red_mat": "Polymer_Red", "optic": "Glass_Lens",
      "Pixelcom": "Metal_Dark", "Pixelcom1": "Metal_Dark", "Pixelcom2": "Metal_Dark", "Pixelcom3": "Metal_Dark", "Pixelcom4": "Metal_Dark"}
c = {}
def kit(name, obj, kind, slot, **kw):
    d = {"src": K, "objects": [obj], "kind": kind, "slot": slot, "fwd": "+X", "up": "+Z", "scale": 0.147, "mat": KM, "mat_default": "Metal_Black"}
    d.update(kw); c[name] = d
kit("Optic_Scope6x", "kit_optic_3", "optic", "Optic", lens_material=["optic"], magnification=6.0, compatible=RIF + ["SVD","VSS","AWM","AXMC"])
kit("Optic_HoloRed", "kit_optic_5", "optic", "Optic", lens_material=["optic"], compatible=R1)
kit("Optic_MiniDot", "kit_optic_6", "optic", "Optic", lens_material=["optic"], compatible=R1)
kit("Muzzle_Comp", "kit_muzzle_2", "muzzle", "Muzzle", compatible=RIF)
kit("Muzzle_BrakeHex", "kit_muzzle_3", "muzzle", "Muzzle", compatible=RIF + ["SVD","AWM","AXMC"])
kit("Muzzle_Linear", "kit_muzzle_4", "muzzle", "Muzzle", compatible=RIF)
kit("Suppressor_Long", "kit_muzzle_5", "muzzle", "Muzzle", compatible=RIF + ["SVD"])
kit("Suppressor_Flow", "kit_muzzle_6", "muzzle", "Muzzle", compatible=RIF)
kit("Grip_Angled_Ergo", "kit_fgrip_2", "grip", "Underbarrel", rake=-20, grip_l=[0.0, -0.05], compatible=GRIPS)
kit("Grip_Angled_AFG", "kit_fgrip_4", "grip", "Underbarrel", rake=-35, grip_l=[0.0, -0.03], compatible=GRIPS)
kit("Grip_Angled_Stop", "kit_fgrip_5", "grip", "Underbarrel", rake=-35, grip_l=[0.03, -0.02], compatible=GRIPS)
kit("Grip_Vertical_Tactical", "kit_fgrip_6", "grip", "Underbarrel", grip_l=[0.0, -0.055], compatible=GRIPS)
kit("Mag_AR_Drum", "kit_mag_0", "mag", "Magazine", capacity=60, compatible=["M4A1","HK416","MK18"])
kit("Mag_AR_Coupled", "kit_mag_6", "mag", "Magazine", capacity=60, compatible=["M4A1","HK416","MK18"])
kit("Mag_AK_Ext", "kit_mag_5", "mag", "Magazine", capacity=45, compatible=["AKM","MK47"])
c["Optic_ACOG"] = {"src": W + "/low-poly-acog/low-poly-acog.blend", "objects": ["ACOG"], "kind": "optic", "slot": "Optic", "fwd": "+Y", "up": "+Z",
    "scale": 1.0, "lens_auto": {"from_rear": 0.004, "radius": 0.0115}, "magnification": 4.0, "mat": {"Gray": "Metal_Black", "Red": "Polymer_Red"},
    "compatible": RIF + ["SVD","VSS","AWM","AXMC","HoneyBadger","MPX","Scorpion"]}
c["Optic_HoloEXPS"] = {"src": W + "/low-poly-holographic-sight/low-poly-holographic-sight.blend", "objects": ["Sight.001", "Glass.001"],
    "lens_objects": ["Glass.001"], "kind": "optic", "slot": "Optic", "fwd": "+Y", "up": "+Z", "scale": 1.0, "compatible": R1,
    "mat_default": "Metal_Black"}
c["Optic_OKP7"] = {"src": W + "/low-poly-okp-7/low-poly-okp-7.blend", "objects": ["Optic", "Lens"], "lens_objects": ["Lens"], "kind": "optic",
    "slot": "Optic", "fwd": "+X", "up": "+Z", "scale": 0.3, "compatible": R1, "mat": {"Gray": "Metal_Black"}}
for rk, v in (("RK2", -0.05), ("RK0", -0.03)):
    c["Grip_Vertical_" + rk] = {"src": W + "/low-poly-ak-grips/low-poly-ak-grips.blend", "objects": [rk.replace("RK", "RK-")], "kind": "grip",
        "slot": "Underbarrel", "fwd": "+Y", "up": "+Z", "scale": 1.0, "grip_l": [0.0, v], "compatible": GRIPS,
        "mat": {"metal": "Polymer_Black", "metal_rough": "Polymer_Black"}}
MS = W + "/low-poly-muzzle-device-set/low-poly-muzzle-device-set.blend"
c["Muzzle_Brake762"] = {"src": MS, "objects": ["mb 7.62mm"], "kind": "muzzle", "slot": "Muzzle", "fwd": "+X", "up": "+Z", "scale": 0.08,
    "compatible": ["AKM","MK47","AK12","SVD","M14EBR","AWM","AXMC"]}
c["Suppressor_762"] = {"src": MS, "objects": ["sil 7.62mm"], "kind": "muzzle", "slot": "Muzzle", "fwd": "+X", "up": "+Z", "scale": 0.11,
    "compatible": ["AKM","MK47","SVD","M14EBR","AK12"]}
c["Suppressor_9mm"] = {"src": MS, "objects": ["sil 9mm"], "kind": "muzzle", "slot": "Muzzle", "fwd": "+X", "up": "+Z", "scale": 0.075,
    "compatible": ["MPX","Scorpion","Vector","SR2","Glock17"]}
json.dump(c, open(r"{WORK}/scripts/att_cfg.json", "w"), indent=1)
print("ATTCFG", len(c))
