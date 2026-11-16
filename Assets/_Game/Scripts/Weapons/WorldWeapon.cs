using UnityEngine;

namespace FPS
{
    /// <summary>
    /// On a world (third-person / pickup) copy of a first-person weapon (Loadout.MakeWorldModel): what a body needs to
    /// hold it the way the local player's first-person body holds the original (SkinnedBody) — its hip hold and sprint
    /// pose, pistol or long gun, the sight line ("AimPoint", where the original's sight point is) and the muzzle.
    /// </summary>
    public class WorldWeapon : MonoBehaviour
    {
        public string weaponName;
        public bool pistol;
        [Tooltip("The original's hip hold (camera / eye space)")] public Vector3 hipPosition = new Vector3(0.155f, -0.175f, 0.33f);
        [Tooltip("The original's sprint pose offsets (eye space, degrees)")] public Vector3 sprintPos, sprintRot;
        [Tooltip("Sight line point (forward = the line of sight)")] public Transform aimPoint;
        public Transform muzzle;

        /// <summary>Fills the hold data from the first-person original; <paramref name="copy"/> is the clone of its model.</summary>
        public static WorldWeapon Attach(GameObject copy, WeaponController w)
        {
            var ww = copy.AddComponent<WorldWeapon>();
            ww.weaponName = w.weaponName;
            ww.pistol = w.slot == WeaponSlot.Secondary;
            ww.hipPosition = w.hipPosition;
            ww.sprintPos = w.sprintPos;
            ww.sprintRot = w.sprintRot;
            Transform model = w.model, root = copy.transform;
            // the copy's root is the original's model root (same local space): the points keep their local poses
            if (w.sightPoint && model)
                ww.aimPoint = Point(root, "AimPoint", model.InverseTransformPoint(w.sightPoint.position), Quaternion.Inverse(model.rotation) * w.sightPoint.rotation);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "Muzzle") { ww.muzzle = t; break; }
            // a muzzle that hung on the holder rather than the model did not come along: one at the same place
            if (!ww.muzzle && w.muzzle && model)
                ww.muzzle = Point(root, "Muzzle", model.InverseTransformPoint(w.muzzle.position), Quaternion.Inverse(model.rotation) * w.muzzle.rotation);
            return ww;
        }

        static Transform Point(Transform parent, string name, Vector3 localPos, Quaternion localRot)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = localRot;
            t.gameObject.layer = parent.gameObject.layer;
            return t;
        }
    }
}
