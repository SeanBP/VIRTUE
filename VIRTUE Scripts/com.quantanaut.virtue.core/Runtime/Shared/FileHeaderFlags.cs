using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace VirtueCore.Shared
{
    // Reads flags from a model/event file's "header" object without parsing
    // the whole file -- event files in particular can be very large, and the
    // file lists are rebuilt every time the menu opens.
    //
    // Currently just "hidden": a file whose header has "hidden": true is left
    // out of the dropdown lists, but can still be loaded by reference (a
    // model's header.event_file, an event's header.model_file, or a tour).
    public static class FileHeaderFlags
    {
        // The header is conventionally the first thing in the file; only
        // this much of the file is read to find it. A header that starts or
        // ends beyond this counts as "not hidden".
        private const int MaxHeaderBytes = 64 * 1024;

        [Serializable]
        private class Flags
        {
            public bool hidden = false;
        }

        private struct CacheEntry
        {
            public DateTime lastWriteUtc;
            public long length;
            public bool hidden;
        }

        // Keyed by full path; reused until the file's size or timestamp
        // changes, so refreshing the lists doesn't re-read unchanged files.
        private static readonly Dictionary<string, CacheEntry> cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        // Whether the JSON file at `path` is marked hidden. Never throws:
        // unreadable or unrecognisable files count as not hidden, so they
        // still appear in the list (and report their own error on load).
        public static bool IsHidden(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists) return false;

                if (cache.TryGetValue(info.FullName, out CacheEntry entry) &&
                    entry.lastWriteUtc == info.LastWriteTimeUtc && entry.length == info.Length)
                    return entry.hidden;

                string start;
                using (var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] buffer = new byte[(int)Math.Min(MaxHeaderBytes, info.Length)];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int n = stream.Read(buffer, read, buffer.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    start = Encoding.UTF8.GetString(buffer, 0, read);
                }

                bool hidden = IsHiddenJson(start);
                cache[info.FullName] = new CacheEntry
                {
                    lastWriteUtc = info.LastWriteTimeUtc,
                    length = info.Length,
                    hidden = hidden
                };
                return hidden;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Same check on JSON text already in memory (e.g. a Mobile TextAsset);
        // only the header object is parsed.
        public static bool IsHiddenJson(string json)
        {
            string header = ExtractHeaderObject(json);
            if (header == null) return false;

            try
            {
                Flags flags = JsonUtility.FromJson<Flags>(header);
                return flags != null && flags.hidden;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Returns the text of the top-level "header": { ... } object, or
        // null if it isn't found complete in `json`.
        private static string ExtractHeaderObject(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            int key = json.IndexOf("\"header\"", StringComparison.Ordinal);
            if (key < 0) return null;

            int start = json.IndexOf('{', key + 8);
            if (start < 0) return null;

            // Only whitespace and the ':' may sit between the key and '{'.
            for (int i = key + 8; i < start; i++)
            {
                char c = json[i];
                if (c != ':' && !char.IsWhiteSpace(c)) return null;
            }

            int depth = 0;
            bool inString = false;
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return json.Substring(start, i - start + 1);
                }
            }
            return null;
        }
    }
}
