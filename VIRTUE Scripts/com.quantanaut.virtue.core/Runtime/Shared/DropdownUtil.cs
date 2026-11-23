using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VirtueCore.Shared
{
    public static class DropdownUtil
    {
        // Closes every dropdown whose list is currently open. Called when a
        // menu slides away: an open list is parented under its dropdown, so
        // it travels with the menu and the part that extends below the menu
        // stays visible ("peeks up") after the menu has slid off-screen.
        //
        // Afterwards the EventSystem's selection is cleared entirely, not
        // just for dropdowns that were open: a dropdown keeps keyboard focus
        // after the user picks an option, and Space/Enter is UI "Submit", so
        // a focused dropdown (or button) in a menu that has slid off-screen
        // would still open (or click) on Space -- which is also the fly-up
        // key, so the user sees the camera rise and a list pop up from below.
        public static void CloseOpenDropdowns()
        {
            TMP_Dropdown[] dropdowns = Object.FindObjectsByType<TMP_Dropdown>();
            foreach (TMP_Dropdown dropdown in dropdowns)
            {
                if (dropdown.IsExpanded)
                    dropdown.Hide();
            }

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem != null)
                eventSystem.SetSelectedGameObject(null);
        }
    }
}
