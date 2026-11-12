using UnityEngine;
using UnityEngine.EventSystems;

namespace FPS
{
    /// <summary>Shows a map description in the main menu when a button is hovered.</summary>
    public class HoverInfo : MonoBehaviour, IPointerEnterHandler
    {
        public MainMenu menu;
        [TextArea] public string text;

        public void OnPointerEnter(PointerEventData e)
        {
            if (menu) menu.ShowDesc(text);
        }
    }
}
