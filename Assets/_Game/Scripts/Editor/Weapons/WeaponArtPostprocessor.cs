using System;
using UnityEditor;

namespace FPS.EditorTools
{
    /// <summary>
    /// A new or re-exported weapon model, texture or manifest under Assets/_Game/Art/Weapons re-runs
    /// <see cref="WeaponAssetBuilder"/> (materials incl. textured "&lt;Id&gt;_Main", FBX remaps, texture color spaces, data
    /// assets) once the import is done. Deliberately no OnPreprocess* hooks: those would make every model / texture of
    /// the project re-import when this script is added.
    /// </summary>
    public class WeaponArtPostprocessor : AssetPostprocessor
    {
        const string Root = "Assets/_Game/Art/Weapons/";

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
            {
                if (!p.StartsWith(Root, StringComparison.Ordinal)) continue;
                if (p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    WeaponAssetBuilder.ScheduleAutoBuild();
                    return;
                }
            }
        }
    }
}
