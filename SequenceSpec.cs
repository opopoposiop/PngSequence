using System.Globalization;
using System.Text.RegularExpressions;

namespace PngSequenceAvi;

internal sealed record SequenceSpec(
    string DirectoryPath,
    string Prefix,
    string Suffix,
    int Digits,
    int StartNumber,
    int EndNumber,
    int Count,
    string PatternPath,
    IReadOnlyList<string> Files)
{
    public string DisplayName => $"{Prefix}%0{Digits}d{Suffix}";

    public static SequenceSpec FromFiles(IEnumerable<string> paths)
    {
        var pngFiles = paths
            .Where(path => File.Exists(path) && string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (pngFiles.Length == 0)
        {
            throw new InvalidOperationException("PNGファイルが見つかりません。連番PNGファイルをドロップしてください。");
        }

        var candidates = pngFiles
            .Select(TryParse)
            .Where(item => item is not null)
            .Cast<ParsedFile>()
            .GroupBy(item => new
            {
                Directory = item.DirectoryPath.ToUpperInvariant(),
                Prefix = item.Prefix.ToUpperInvariant(),
                Suffix = item.Suffix.ToUpperInvariant(),
                item.Digits
            })
            .Select(group => group.OrderBy(item => item.Number).ToArray())
            .OrderByDescending(group => group.Length)
            .ThenBy(group => group.First().Number)
            .FirstOrDefault();

        if (candidates is null || candidates.Length == 0)
        {
            throw new InvalidOperationException("ファイル名の末尾に連番があるPNGをドロップしてください。例: frame_0001.png");
        }

        var first = candidates.First();
        var numbers = candidates.Select(item => item.Number).Order().ToArray();
        var start = numbers.First();
        var end = numbers.Last();
        var pattern = Path.Combine(first.DirectoryPath, $"{first.Prefix}%0{first.Digits}d{first.Suffix}");
        var orderedFiles = candidates.OrderBy(item => item.Number).Select(item => item.Path).ToArray();

        return new SequenceSpec(
            first.DirectoryPath,
            first.Prefix,
            first.Suffix,
            first.Digits,
            start,
            end,
            candidates.Length,
            pattern,
            orderedFiles);
    }

    public string GetMissingNumberSummary()
    {
        var existing = Files
            .Select(TryParse)
            .Where(item => item is not null)
            .Cast<ParsedFile>()
            .Select(item => item.Number)
            .ToHashSet();

        var missing = Enumerable.Range(StartNumber, EndNumber - StartNumber + 1)
            .Where(number => !existing.Contains(number))
            .Take(12)
            .Select(number => number.ToString($"D{Digits}", CultureInfo.InvariantCulture))
            .ToArray();

        if (missing.Length == 0)
        {
            return "欠番なし";
        }

        var suffix = EndNumber - StartNumber + 1 - Count > missing.Length ? " ..." : string.Empty;
        return $"欠番: {string.Join(", ", missing)}{suffix}";
    }

    private static ParsedFile? TryParse(string path)
    {
        var fileName = Path.GetFileName(path);
        var match = Regex.Match(fileName, @"^(?<prefix>.*?)(?<number>\d+)(?<suffix>\.png)$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        return new ParsedFile(
            path,
            Path.GetDirectoryName(path) ?? string.Empty,
            match.Groups["prefix"].Value,
            match.Groups["suffix"].Value,
            match.Groups["number"].Value.Length,
            int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture));
    }

    private sealed record ParsedFile(string Path, string DirectoryPath, string Prefix, string Suffix, int Digits, int Number);
}
