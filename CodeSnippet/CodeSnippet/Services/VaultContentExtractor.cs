namespace CodeSnippet.Services;

internal static class VaultContentExtractor
{
    // Obsidian export shape:
    //   #title
    //   ```lang
    //   actual content
    //   ```
    // Strip title + fence ONLY when the whole file matches this exact shape end-to-end;
    // anything else (no title, no fence, multiple blocks, trailing text) passes through raw.
    public static string ExtractContent(string raw)
    {
        var lines = raw.Replace("\r\n", "\n").Split('\n');

        var start = 0;
        while (start < lines.Length && lines[start].Trim().Length == 0) start++;
        if (start >= lines.Length || !lines[start].TrimStart().StartsWith('#'))
            return raw;

        var fenceOpen = start + 1;
        while (fenceOpen < lines.Length && lines[fenceOpen].Trim().Length == 0) fenceOpen++;
        if (fenceOpen >= lines.Length || !lines[fenceOpen].TrimStart().StartsWith("```"))
            return raw;

        var end = lines.Length - 1;
        while (end > fenceOpen && lines[end].Trim().Length == 0) end--;
        if (end <= fenceOpen || lines[end].Trim() != "```")
            return raw;

        return string.Join('\n', lines[(fenceOpen + 1)..end]).Trim('\n');
    }
}
