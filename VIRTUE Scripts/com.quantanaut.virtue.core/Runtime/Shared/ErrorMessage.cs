using UnityEngine.UI;

namespace VirtueCore.Shared
{
    // Lets the user clear the shared on-screen error/status text (the single
    // Text every loader, the tour maker and the pause menu write to).
    //
    // Progress messages ("Loading model: 40%", "Reading event file",
    // "Loading events: ...") are not dismissible: they clear themselves when
    // the load finishes, and treating them as dismissible would make Escape
    // swallow the menu toggle for the whole duration of every load.
    public static class ErrorMessage
    {
        public static bool IsDismissible(string message)
        {
            return !string.IsNullOrEmpty(message)
                && !message.StartsWith("Loading")
                && !message.StartsWith("Reading");
        }

        // Clears the message if one is showing. Returns whether anything was
        // cleared, so a caller (e.g. the Escape key handler) can treat the
        // input as consumed.
        public static bool TryDismiss(Text text)
        {
            if (text == null || !IsDismissible(text.text)) return false;
            text.text = "";
            return true;
        }

        // Makes clicking/tapping the message clear it.
        public static void EnableClickToDismiss(Text text)
        {
            if (text == null) return;
            if (text.GetComponent<DismissOnClick>() == null)
                text.gameObject.AddComponent<DismissOnClick>();
        }
    }
}
