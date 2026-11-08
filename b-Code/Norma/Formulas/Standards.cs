using System.Text;

namespace Norma.Formulas;

/// <summary>一条标准代号：代号、名称、国内/国外，以及手册原注或收录说明。</summary>
public sealed record StandardCode(string Code, string Name, string Scope, string? Note);

/// <summary>一次查表：识别出的是代号还是名称，命中的总条数与前若干条。</summary>
public sealed record StandardLookup(string Query, bool ByCode, int Total, IReadOnlyList<StandardCode> Items);

/// <summary>
/// 「国内外部分标准代号」两页的查表：机械工程师手册 sjzl 章第 16、17 页。
/// 只收代号与名称，不复制手册表格；数据在嵌入资源 <c>standards.tsv</c>。
/// </summary>
public static class Standards
{
    private const string ResourceName = "Norma.Formulas.standards.tsv";

    private static readonly Lazy<IReadOnlyList<StandardCode>> Cached = new(Load);

    /// <summary>全部标准代号，按国内、国外与代号排序。</summary>
    public static IReadOnlyList<StandardCode> All => Cached.Value;

    /// <summary>查询内容看起来像代号（以字母开头，如 GB、JB/ZQ、ISO）；否则按中文名称查。</summary>
    public static bool LooksLikeCode(string query) =>
        query.Trim().Length > 0 && char.IsAsciiLetter(query.Trim()[0]);

    /// <summary>代号比较形式：大写，只留字母和数字（GB/T、GB T、GB-T 都变成 GBT）。</summary>
    internal static string NormalizeCode(string code)
    {
        var builder = new StringBuilder(code.Length);
        foreach (var ch in code.ToUpperInvariant())
            if (char.IsAsciiLetterOrDigit(ch))
                builder.Append(ch);
        return builder.ToString();
    }

    /// <summary>
    /// 双向查表。代号先完全一致，再按前缀；名称按完全一致、开头、包含。
    /// 0 条也是正常结果。
    /// </summary>
    public static StandardLookup Lookup(string query, int limit)
    {
        var text = query.Trim();
        if (text.Length == 0)
            return new StandardLookup(text, false, 0, []);

        var byCode = LooksLikeCode(text);
        var scored = new List<(StandardCode Entry, int Rank)>();
        if (byCode)
        {
            var want = NormalizeCode(text);
            // GB/T5782 这种完整标准号：取数字前的字母部分（GBT），再让 GB 这类短代号前缀命中。
            var digit = text.IndexOfAny("0123456789".ToCharArray());
            var prefix = NormalizeCode(digit >= 0 ? text[..digit] : text);
            foreach (var entry in All)
            {
                var key = NormalizeCode(entry.Code);
                int? rank = key == want ? 0
                    : key == prefix ? 1
                    : key.StartsWith(want, StringComparison.Ordinal) ? 2
                    : prefix.StartsWith(key, StringComparison.Ordinal) ? 3
                    : null;
                if (rank is { } value)
                    scored.Add((entry, value));
            }
        }
        else
        {
            var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in All)
            {
                var rank = NameRank(entry.Name, text, tokens);
                if (rank < int.MaxValue)
                    scored.Add((entry, rank));
            }
        }

        var items = scored
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.Entry.Scope == "国内" ? 0 : 1)
            .ThenBy(item => item.Entry.Code, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(item => item.Entry)
            .ToList();
        return new StandardLookup(text, byCode, scored.Count, items);
    }

    private static int NameRank(string name, string query, string[] tokens)
    {
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 1;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 2;
        return tokens.All(token => name.Contains(token, StringComparison.OrdinalIgnoreCase))
            ? 3
            : int.MaxValue;
    }

    private static IReadOnlyList<StandardCode> Load()
    {
        using var stream = typeof(Standards).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"缺少嵌入资源 {ResourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var entries = new List<StandardCode>();
        var first = true;
        while (reader.ReadLine() is { } line)
        {
            if (first)
            {
                first = false;
                if (line.StartsWith("code\t", StringComparison.Ordinal))
                    continue;
            }

            var fields = line.Split('\t');
            if (fields.Length < 3)
                continue;
            var code = fields[0].Trim();
            var name = fields[1].Trim();
            var scope = fields[2].Trim();
            if (code.Length == 0 || name.Length == 0 || scope.Length == 0)
                continue;
            var note = fields.Length > 3 && fields[3].Trim().Length > 0 ? fields[3].Trim() : null;
            entries.Add(new StandardCode(code, name, scope, note));
        }

        return entries
            .OrderBy(entry => entry.Scope == "国内" ? 0 : 1)
            .ThenBy(entry => entry.Code, StringComparer.Ordinal)
            .ToList();
    }
}
