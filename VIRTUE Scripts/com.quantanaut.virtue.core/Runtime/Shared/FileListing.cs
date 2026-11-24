using System;
using System.Collections.Generic;
using System.IO;

namespace VirtueCore.Shared
{
    // Lists the files in a StreamingAssets folder (Models, Events, Tours)
    // including any subfolders the user has made to organise them.
    //
    // Files are identified by their path relative to that folder, always
    // with '/' separators (e.g. "LHC/ATLAS.json"), so the same name means
    // the same file on every platform. Files directly in the folder keep
    // their plain file name, exactly as before subfolders were supported.
    public static class FileListing
    {
        // Relative paths of every file under `root` (and its subfolders) that
        // `accept` (given the full path) lets through. Files directly in
        // root come first, then each subfolder's, folders and files sorted
        // alphabetically. Names starting with '.' (hidden files/folders, e.g.
        // macOS "._" files) are skipped, as are folders that can't be read.
        public static List<string> ListFiles(string root, Func<string, bool> accept)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return result;

            Collect(root, "", accept, result, 0);
            return result;
        }

        // Full path of a relative file name under root.
        public static string FullPath(string root, string relativePath)
        {
            return Path.Combine(root, Normalize(relativePath).Replace('/', Path.DirectorySeparatorChar));
        }

        // Dropdown label: the relative path without its extension, so files
        // in subfolders show which folder they're in ("LHC/ATLAS").
        public static string DisplayName(string relativePath)
        {
            return StripExtension(Normalize(relativePath));
        }

        // Turns `path` into a relative name under root if it's an absolute
        // path inside root; otherwise returns it normalised.
        public static string ToRelative(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            if (Path.IsPathRooted(path) && !string.IsNullOrEmpty(root))
            {
                string full = Normalize(Path.GetFullPath(path));
                string rootFull = Normalize(Path.GetFullPath(root)).TrimEnd('/') + "/";
                if (full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    return full.Substring(rootFull.Length);
            }

            return Normalize(path);
        }

        // '/' separators, no leading "./", trimmed.
        public static string Normalize(string path)
        {
            if (path == null) return null;
            string p = path.Trim().Replace('\\', '/');
            while (p.StartsWith("./")) p = p.Substring(2);
            return p;
        }

        // Path without its extension, keeping any folder ("LHC/ATLAS.json"
        // -> "LHC/ATLAS").
        public static string StripExtension(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            int slash = path.LastIndexOf('/');
            int dot = path.LastIndexOf('.');
            // A leading dot (".hidden") isn't an extension.
            return dot > slash + 1 ? path.Substring(0, dot) : path;
        }

        // Guards against a folder linking back to itself.
        private const int MaxDepth = 32;

        private static void Collect(string directory, string prefix, Func<string, bool> accept,
                                    List<string> result, int depth)
        {
            string[] files;
            string[] folders;
            try
            {
                files = Directory.GetFiles(directory);
                folders = depth < MaxDepth ? Directory.GetDirectories(directory) : new string[0];
            }
            catch (Exception)
            {
                return;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            Array.Sort(folders, StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith(".")) continue;
                if (accept != null && !accept(file)) continue;
                result.Add(prefix + name);
            }

            foreach (string folder in folders)
            {
                string name = Path.GetFileName(folder);
                if (name.StartsWith(".")) continue;
                Collect(folder, prefix + name + "/", accept, result, depth + 1);
            }
        }
    }
}
