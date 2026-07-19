using System.Globalization;
using System.Text.RegularExpressions;

namespace PngSequenceAvi;

/*
 * 連番PNGのファイル名を解析し、変換順・開始番号・終了番号・欠番をまとめるクラスです。
 *
 * ユーザーが変更してよい箇所:
 * - エラーメッセージの文章
 * - 欠番表示の最大件数（現在は12件）
 * - 対応するファイル名規則。ただし変更後は必ず複数の命名例で確認してください。
 *
 * 変更不可の箇所:
 * - Filesを番号順に並べる処理
 * - 同じフォルダー、接頭辞、桁数を1つの連番としてまとめる処理
 * - 大文字・小文字を区別せずPNGを判定する処理
 *
 * Codex用覚書:
 * - HOW: ファイル名末尾の数字を取り出し、同じ命名規則の最大グループを連番として採用します。
 * - WHY NOT: OSのファイル列挙順をそのまま使いません。0002が0001より先に渡される場合があるためです。
 */
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
        // HOW: 実在するPNGだけを絶対パスへ統一し、同じファイルの重複指定を除きます。
        var pngFiles = paths
            .Where(path => File.Exists(path) && string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (pngFiles.Length == 0)
        {
            throw new InvalidOperationException("PNGファイルが見つかりません。連番PNGファイルをドロップしてください。");
        }

        // WHY NOT: 最初に見つかったPNGだけで連番を決めません。
        // 複数種類の連番が同時に選ばれた場合、最も枚数の多いまとまりを採用します。
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

        // HOW: 画面が欠番一覧で埋まらないよう、表示は先頭12件に制限します。
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
        // WHY NOT: ファイル名の途中にある数字は連番として扱いません。
        // 末尾の数字だけを対象にし、shot01_frame_0001.pngの0001を採用します。
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
