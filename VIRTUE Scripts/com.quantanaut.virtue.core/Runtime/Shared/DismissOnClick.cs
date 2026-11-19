using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VirtueCore.Shared
{
    // Clears the attached message Text when it's clicked or tapped. Added at
    // runtime by ErrorMessage.EnableClickToDismiss.
    //
    // A UI Text blocks clicks across its whole rectangle whenever
    // raycastTarget is on, even with no text in it. So raycastTarget is only
    // turned on while a dismissible message is actually showing; the rest of
    // the time the (possibly large) error text area never gets in the way
    // of the buttons underneath it.
    [RequireComponent(typeof(Text))]
    public class DismissOnClick : MonoBehaviour, IPointerClickHandler
    {
        private Text text;

        void Awake()
        {
            text = GetComponent<Text>();
        }

        void LateUpdate()
        {
            bool showing = ErrorMessage.IsDismissible(text.text);
            if (text.raycastTarget != showing)
                text.raycastTarget = showing;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            ErrorMessage.TryDismiss(text);
        }
    }
}
