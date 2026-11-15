using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Superseded: the first-person arms used to be a separate copy of the character on the KINEMATION FPS Animation
    /// Framework. The player's own body is now animated by FPS.Animation (true first person, see
    /// SkinnedBody.EnableFirstPerson / PlayerBody). This empty component is kept only so old references don't break;
    /// the file can be deleted.
    /// </summary>
    [System.Obsolete("Use SkinnedBody.EnableFirstPerson (FPS.Animation framework)")]
    public class FPArms : MonoBehaviour
    {
    }
}
