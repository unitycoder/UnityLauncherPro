namespace UnityLauncherPro
{
    internal static class JsonParser
    {
        // finds the index of the closing } matching the opening { at startIndex
        internal static int FindMatchingBrace(string json, int startIndex)
        {
            int depth = 0;
            for (int i = startIndex; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        // extracts a JSON string value for the given key
        internal static string GetStringValue(string json, string key)
        {
            int keyIndex = json.IndexOf("\"" + key + "\":");
            if (keyIndex == -1) return null;
            int valueStart = json.IndexOf('"', keyIndex + key.Length + 2) + 1;
            if (valueStart == 0) return null;
            int valueEnd = json.IndexOf('"', valueStart);
            if (valueEnd == -1) return null;
            return json.Substring(valueStart, valueEnd - valueStart);
        }

        // extracts a JSON number value for the given key (returned as string for flexibility)
        internal static string GetNumberValue(string json, string key)
        {
            int keyIndex = json.IndexOf("\"" + key + "\":");
            if (keyIndex == -1) return null;
            int valueStart = keyIndex + key.Length + 3; // skip past "key":
            while (valueStart < json.Length && json[valueStart] == ' ') valueStart++;
            int valueEnd = valueStart;
            while (valueEnd < json.Length && (char.IsDigit(json[valueEnd]) || json[valueEnd] == '-')) valueEnd++;
            return valueEnd > valueStart ? json.Substring(valueStart, valueEnd - valueStart) : null;
        }

        internal static string ExtractJsonString(string json, string key)
        {
            int keyIndex = json.IndexOf(key + ":");
            if (keyIndex == -1) return null;

            int valueStart = json.IndexOf("\"", keyIndex + key.Length + 1);
            if (valueStart == -1) return null;

            int valueEnd = json.IndexOf("\"", valueStart + 1);
            if (valueEnd == -1) return null;

            return json.Substring(valueStart + 1, valueEnd - valueStart - 1);
        }

        internal static string ExtractNestedJsonString(string json, string parentKey, string childKey)
        {
            int parentIndex = json.IndexOf(parentKey + ":");
            if (parentIndex == -1) return null;

            // Find the object after parentKey
            int objectStart = json.IndexOf("{", parentIndex);
            if (objectStart == -1) return null;

            int objectEnd = JsonParser.FindMatchingBrace(json, objectStart);
            if (objectEnd == -1) return null;

            string nestedJson = json.Substring(objectStart, objectEnd - objectStart + 1);
            return ExtractJsonString(nestedJson, childKey);
        }

    } // class
} // namespace
