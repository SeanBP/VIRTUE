using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VirtueCore.Shared
{
    // Minimal JSON reader for the few spots JsonUtility can't handle --
    // chiefly nested arrays such as a hexahedron's "vertices": [[x,y,z], ...].
    // Parses into plain objects:
    //   object -> Dictionary<string, object>
    //   array  -> List<object>
    //   number -> double
    //   string -> string
    //   true/false -> bool, null -> null
    // Throws FormatException on malformed input.
    public static class MiniJson
    {
        // [[x, y, z], ...] (as parsed) -> float[][]; anything that isn't a
        // list of numeric lists becomes null so the caller can report it.
        public static float[][] ReadPoints(object value)
        {
            if (!(value is List<object> list)) return null;

            var points = new float[list.Count][];
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is List<object> point)) return null;
                points[i] = new float[point.Count];
                for (int j = 0; j < point.Count; j++)
                {
                    if (!(point[j] is double number)) return null;
                    points[i][j] = (float)number;
                }
            }
            return points;
        }

        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var reader = new Reader(json);
            object value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd)
                throw reader.Error("Unexpected trailing content");
            return value;
        }

        private class Reader
        {
            private readonly string text;
            private int pos;

            public Reader(string text)
            {
                this.text = text;
                // Skip a UTF-8 byte order mark if the file had one.
                if (text.Length > 0 && text[0] == '﻿') pos = 1;
            }

            public bool AtEnd => pos >= text.Length;

            public FormatException Error(string message)
            {
                return new FormatException(message + " at character " + pos + ".");
            }

            public void SkipWhitespace()
            {
                while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;
            }

            public object ReadValue()
            {
                SkipWhitespace();
                if (AtEnd) throw Error("Unexpected end of JSON");

                char c = text[pos];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': ExpectWord("true"); return true;
                    case 'f': ExpectWord("false"); return false;
                    case 'n': ExpectWord("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            private void ExpectWord(string word)
            {
                if (string.CompareOrdinal(text, pos, word, 0, word.Length) != 0)
                    throw Error("Expected '" + word + "'");
                pos += word.Length;
            }

            private Dictionary<string, object> ReadObject()
            {
                var result = new Dictionary<string, object>();
                pos++; // {
                SkipWhitespace();
                if (!AtEnd && text[pos] == '}') { pos++; return result; }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || text[pos] != '"') throw Error("Expected property name");
                    string key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || text[pos] != ':') throw Error("Expected ':'");
                    pos++;
                    result[key] = ReadValue();
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unterminated object");
                    if (text[pos] == ',') { pos++; continue; }
                    if (text[pos] == '}') { pos++; return result; }
                    throw Error("Expected ',' or '}'");
                }
            }

            private List<object> ReadArray()
            {
                var result = new List<object>();
                pos++; // [
                SkipWhitespace();
                if (!AtEnd && text[pos] == ']') { pos++; return result; }

                while (true)
                {
                    result.Add(ReadValue());
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unterminated array");
                    if (text[pos] == ',') { pos++; continue; }
                    if (text[pos] == ']') { pos++; return result; }
                    throw Error("Expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                var sb = new StringBuilder();
                pos++; // opening quote
                while (true)
                {
                    if (AtEnd) throw Error("Unterminated string");
                    char c = text[pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }

                    if (AtEnd) throw Error("Unterminated escape");
                    char e = text[pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 > text.Length) throw Error("Bad unicode escape");
                            sb.Append((char)int.Parse(text.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            pos += 4;
                            break;
                        default: throw Error("Bad escape '\\" + e + "'");
                    }
                }
            }

            private double ReadNumber()
            {
                int start = pos;
                if (text[pos] == '-') pos++;
                while (pos < text.Length && "0123456789+-.eE".IndexOf(text[pos]) >= 0) pos++;
                string number = text.Substring(start, pos - start);
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    throw Error("Bad number '" + number + "'");
                return value;
            }
        }
    }
}
