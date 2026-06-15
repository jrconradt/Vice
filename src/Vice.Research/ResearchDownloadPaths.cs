namespace Vice.Research;

internal static class ResearchDownloadPaths
{
    public static string BuildUrlDestinationPath(string? toPath,
                                                 string fileName)
    {
        var safeName = Sanitize(fileName);
        if (string.IsNullOrWhiteSpace(toPath))
        {
            return Path.Combine(Environment.CurrentDirectory, safeName);
        }

        var full = Path.GetFullPath(toPath);
        if (Directory.Exists(full) || EndsWithSeparator(toPath))
        {
            return Path.Combine(full, safeName);
        }

        return full;
    }

    public static string BuildDestinationPath(string? toPath,
                                              string source,
                                              string id,
                                              string extension)
    {
        if (string.IsNullOrWhiteSpace(toPath))
        {
            return Path.Combine(Environment.CurrentDirectory, $"{Sanitize(id)}.{extension}");
        }

        var full = Path.GetFullPath(toPath);
        if (Directory.Exists(full) || EndsWithSeparator(toPath))
        {
            return Path.Combine(full, $"{Sanitize(id)}.{extension}");
        }

        return full;
    }

    private static bool EndsWithSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar);
    }

    private static string Sanitize(string id)
    {
        var chars = new List<char>(id.Length);
        foreach (var c in id)
        {
            if (char.IsLetterOrDigit(c) || c == '-'
                || c == '_'
                || c == '.')
            {
                chars.Add(c);
            }
            else
            {
                chars.Add('_');
            }
        }

        var start = 0;
        while (start < chars.Count && chars[start] == '.')
        {
            chars[start] = '_';
            start++;
        }

        var sanitized = new string(chars.ToArray());
        if (sanitized.Length == 0)
        {
            return "_";
        }

        return sanitized;
    }
}
