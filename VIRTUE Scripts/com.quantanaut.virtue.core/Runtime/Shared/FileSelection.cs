using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

namespace VirtueCore.Shared
{
    // Helpers for the model/event file dropdowns, used when one file
    // auto-loads its companion (an event's header.model_file or a model's
    // header.event_file) so the dropdown reflects what is actually loaded.
    public static class FileSelection
    {
        // Finds the dropdown entry a header's file reference points to.
        // Header references may or may not include an extension, and the
        // dropdown lists may or may not store one (Desktop/Lite/VR store
        // "name.json", Mobile stores Resources names without extensions,
        // scene models are bare GameObject names), so this tries, in order:
        // exact match, entry == reference minus extension, entry minus
        // extension == reference, then both minus extension. All
        // case-insensitive. Returns -1 if nothing matches.
        //
        // Entries can be in subfolders ("LHC/ATLAS.json", see FileListing).
        // A reference that includes a folder is first matched against the
        // whole relative path (the same four ways); then -- and for a
        // reference that is just a file name -- against entries' file names
        // alone, so "ATLAS.json" still finds "LHC/ATLAS.json". Lists put
        // top-level files first, so a top-level file wins a name clash.
        public static int FindIndex(IList<string> names, string requested)
        {
            if (names == null || string.IsNullOrWhiteSpace(requested))
                return -1;

            string reqPath = PathKey(requested);
            int index = -1;

            if (reqPath.Contains("/"))
                index = FindByName(names, reqPath, PathKey);

            if (index < 0)
                index = FindByName(names, Path.GetFileName(reqPath), n => Path.GetFileName(PathKey(n)));

            return index;
        }

        // Exact (case-insensitive) match on the whole relative path. Used to
        // re-find the current file after the list is refreshed, where
        // FindIndex's extension-insensitive fallbacks could wrongly pick e.g.
        // foo.json after foo.fbx was deleted (or a same-named file in
        // another folder). An absolute path is matched on its file name.
        public static int FindExact(IList<string> names, string requested)
        {
            if (names == null || string.IsNullOrWhiteSpace(requested))
                return -1;

            if (Path.IsPathRooted(requested.Trim()))
            {
                string name = Path.GetFileName(PathKey(requested));
                return Find(names, n => Same(PathKey(n), name));
            }

            string req = PathKey(requested);
            return Find(names, n => Same(PathKey(n), req));
        }

        // The four-way extension-tolerant match described above, comparing
        // `req` with key(entry).
        private static int FindByName(IList<string> names, string req, Func<string, string> key)
        {
            string reqStem = FileListing.StripExtension(req);

            int index = Find(names, n => Same(key(n), req));
            if (index < 0) index = Find(names, n => Same(key(n), reqStem));
            if (index < 0) index = Find(names, n => Same(FileListing.StripExtension(key(n)), req));
            if (index < 0) index = Find(names, n => Same(FileListing.StripExtension(key(n)), reqStem));
            return index;
        }

        // '/' separators, no leading "./" or '/'.
        private static string PathKey(string path)
        {
            return FileListing.Normalize(path).TrimStart('/');
        }

        // Shows the given entry as selected without firing onValueChanged
        // (which in these scenes is wired to OnFileSelected + LoadFile, so
        // setting .value directly would trigger a second, companion-chasing
        // load). index -1 shows an empty selection.
        public static void ShowSelection(TMP_Dropdown dropdown, int index)
        {
            if (dropdown == null) return;

            RestorePlaceholderText(dropdown);

            // TMP_Dropdown only allows value -1 when a placeholder graphic is
            // assigned; otherwise it clamps -1 back to entry 0.
            if (index < 0) EnsurePlaceholder(dropdown);

            dropdown.SetValueWithoutNotify(index);
            dropdown.RefreshShownValue();
        }

        // Shows `label` as the current selection without it being one of the
        // dropdown's entries -- for a loaded file that is hidden from the
        // list. Shown through the placeholder, whose own text is put back the
        // next time a listed entry (or nothing) is shown.
        public static void ShowUnlisted(TMP_Dropdown dropdown, string label)
        {
            if (dropdown == null) return;

            EnsurePlaceholder(dropdown);
            if (dropdown.placeholder is TMP_Text text)
            {
                if (!originalPlaceholderText.ContainsKey(dropdown))
                    originalPlaceholderText[dropdown] = text.text;
                text.text = label;
            }

            dropdown.SetValueWithoutNotify(-1);
            dropdown.RefreshShownValue();
        }

        // Placeholder text as it was before ShowUnlisted replaced it, per
        // dropdown. Entries for dropdowns destroyed by a scene reload are
        // dropped the next time any entry is restored.
        private static readonly Dictionary<TMP_Dropdown, string> originalPlaceholderText = new Dictionary<TMP_Dropdown, string>();

        private static void RestorePlaceholderText(TMP_Dropdown dropdown)
        {
            if (!originalPlaceholderText.TryGetValue(dropdown, out string original)) return;

            if (dropdown.placeholder is TMP_Text text)
                text.text = original;
            originalPlaceholderText.Remove(dropdown);

            // Unity's == null is true for destroyed objects.
            List<TMP_Dropdown> destroyed = new List<TMP_Dropdown>();
            foreach (TMP_Dropdown key in originalPlaceholderText.Keys)
                if (key == null) destroyed.Add(key);
            foreach (TMP_Dropdown key in destroyed)
                originalPlaceholderText.Remove(key);
        }

        private static void EnsurePlaceholder(TMP_Dropdown dropdown)
        {
            if (dropdown.placeholder != null || dropdown.captionText == null) return;

            // A blank copy of the caption text, so the empty state uses the
            // same layout and nothing extra has to be set up in the scene.
            GameObject copy = UnityEngine.Object.Instantiate(
                dropdown.captionText.gameObject,
                dropdown.captionText.transform.parent);
            copy.name = "Placeholder (empty selection)";
            TMP_Text text = copy.GetComponent<TMP_Text>();
            text.text = "";
            dropdown.placeholder = text;
        }

        private static int Find(IList<string> names, Func<string, bool> match)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] != null && match(names[i])) return i;
            }
            return -1;
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
