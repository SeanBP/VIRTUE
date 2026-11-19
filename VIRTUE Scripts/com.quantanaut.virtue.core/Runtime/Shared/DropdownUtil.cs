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
        // Only expanded dropdowns are touched, because TMP_Dropdown.Hide()
        // also selects the dropdown it closes; that selection is then
        // cleared so nothing is left highlighted in the hidden menu.
        public static void CloseOpenDropdowns()
        {
            TMP_Dropdown[] dropdowns = Object.FindObjectsByType<TMP_Dropdown>(FindObjectsSortMode.None);
            foreach (TMP_Dropdown dropdown in dropdowns)
            {
                if (!dropdown.IsExpanded) continue;

                dropdown.Hide();

                EventSystem eventSystem = EventSystem.current;
                if (eventSystem != null && eventSystem.currentSelectedGameObject == dropdown.gameObject)
                    eventSystem.SetSelectedGameObject(null);
            }
        }
    }
}
