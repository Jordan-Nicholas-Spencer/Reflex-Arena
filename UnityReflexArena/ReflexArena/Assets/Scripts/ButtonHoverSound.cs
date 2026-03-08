// =============================================================================
// Attached to: Every button that needs hover sounds
// Purpose: Plays sounds on pointer enter (hover).
// =============================================================================

using UnityEngine;
using UnityEngine.EventSystems;

public class ButtonSounds : MonoBehaviour, IPointerEnterHandler
{
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonHover();
    }
}