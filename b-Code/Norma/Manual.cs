using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Norma;

// REQ-MANUAL-001/002: only read supplied local .ysc files; never execute the manual viewer.
internal static class Manual
{
    private static readonly Regex Pages = new(@"<html\b[^>]*>.*?</html\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(10));
    private static readonly Encoding Gb = CreateEncoding();
    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936);
    }

    internal sealed record Page(string Id, string Title, string File, int Entry, long ByteOffset, string Html)
    {
        public string Text => Plain(Html);
    }

    private static string Chapter(string directory, string file)
    {
        var relative = Path.ChangeExtension(Path.GetRelativePath(directory, file), null).Replace('\\', '/');
        var segments = relative.Split('/');
        return segments.Length > 1 && segments[^1].Equals(segments[^2], StringComparison.OrdinalIgnoreCase)
            ? string.Join("/", segments[..^1]) : relative;
    }

    internal static IEnumerable<Page> Load(string root, string? chapter = null)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new ArgumentException("root 应为已下载手册目录（也接受 base 或 jxgcsdzsc 目录）");
        var directory = Path.GetFullPath(root);
        foreach (var suffix in new[] { Path.Combine("base", "jxgcsdzsc"), "jxgcsdzsc" })
            if (Directory.Exists(Path.Combine(directory, suffix))) { directory = Path.Combine(directory, suffix); break; }
        if (chapter is not null && !Regex.IsMatch(chapter, @"^[a-zA-Z0-9_-]+(?:/[a-zA-Z0-9_-]+)*$"))
            throw new ArgumentException("chapter 应为搜索返回的章节标识，如 sjzl、13-01、25-04/24-04");
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint };
        var files = Directory.EnumerateFiles(directory, "*.ysc", options)
            .Where(f => chapter is null || Chapter(directory, f).Equals(chapter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) throw new ArgumentException("未找到对应 .ysc 章节；检查 root 和 chapter");
        if (files.Select(f => Chapter(directory, f)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length)
            throw new ArgumentException("章节文件名重复，无法生成唯一页 ID；请缩小 root 到单本手册");
        foreach (var file in files)
        {
            // Latin1 is a byte-preserving carrier, so Match.Index is an exact source byte offset.
            var bytes = File.ReadAllBytes(file);
            if (!bytes.AsSpan().StartsWith("yingkeyushouceku_m1_2005"u8))
                throw new ArgumentException($"不支持的手册文件格式：{Path.GetFileName(file)}");
            var carrier = Encoding.Latin1.GetString(bytes);
            var matches = Pages.Matches(carrier);
            if (matches.Count == 0) throw new ArgumentException($"章节没有可读取的 HTML：{Path.GetFileName(file)}");
            var entry = 0;
            foreach (Match match in matches)
            {
                entry++;
                var html = Gb.GetString(bytes, match.Index, match.Length);
                var title = Regex.Match(html, @"<title\b[^>]*>(.*?)</title\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Groups[1].Value;
                // Some shipped pages have no <title>. Their 80-byte GBK index label is authoritative.
                var indexEnd = 25 + 88 * matches.Count;
                if (string.IsNullOrWhiteSpace(title) && (matches[0].Index == indexEnd || matches[0].Index == indexEnd + 100))
                {
                    var label = bytes.AsSpan(25 + (entry - 1) * 88, 80);
                    var terminator = label.IndexOf((byte)0);
                    title = Gb.GetString(terminator >= 0 ? label[..terminator] : label);
                }
                if (string.IsNullOrWhiteSpace(title)) title = $"{Chapter(directory, file)} 条目 {entry}（源页无标题）";
                yield return new Page($"{Chapter(directory, file)}:{entry}", Plain(title),
                    Path.GetRelativePath(directory, file).Replace('\\', '/'), entry, match.Index, html);
            }
        }
    }

    internal static string Plain(string html)
    {
        var text = Regex.Replace(html, @"<(script|style|head)\b[^>]*>.*?</\1\s*>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<!--[\s\S]*?-->", "");
        // Keep table rows and cells visible; merged-cell geometry requires the original HTML.
        text = Regex.Replace(text, @"</t[dh]\s*>\s*<t[dh]\b[^>]*>", "\t", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(?:br\b[^>]*|/?(?:p|div|tr|table|h[1-6])\b[^>]*)>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<img\b[^>]*>", m => "[图片 " + Attribute(m.Value, "src") + "]", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(Regex.Replace(text, @"<[^>]*>", ""));
        return string.Join("\n", text.Replace("\r", "").Split('\n').Select(line => Regex.Replace(line, @"[^\S\t\n]+", " ").Trim()).Where(line => line.Length > 0));
    }

    private static string Attribute(string tag, string name) => WebUtility.HtmlDecode(
        Regex.Match(tag, name + "\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.IgnoreCase)
            .Groups.Cast<Group>().Skip(1).FirstOrDefault(g => g.Success)?.Value ?? "");

    internal static object Search(string root, string query, string scope, string? chapter, int offset, int limit)
    {
        if (scope is not ("title" or "text")) throw new ArgumentException("scope 应为 title 或 text");
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentException("offset ≥ 0，limit 应为 1～100");
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var total = 0;
        var items = new List<object>();
        foreach (var page in Load(root, chapter))
        {
            var target = scope == "text" ? page.Text : page.Title;
            if (!terms.All(term => target.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
            if (total++ < offset || items.Count >= limit) continue;
            var at = terms.Length == 0 ? 0 : Math.Max(0, target.IndexOf(terms[0], StringComparison.OrdinalIgnoreCase) - 50);
            items.Add(new { id = page.Id, title = page.Title, source = page.File, entry = page.Entry,
                snippet = target.Substring(at, Math.Min(240, target.Length - at)) });
        }
        return new { query, scope, total, offset, returned = items.Count, nextOffset = offset + items.Count < total ? (int?)(offset + items.Count) : null, items };
    }

    internal static object Read(string root, string id, string format, int offset, int length)
    {
        var match = Regex.Match(id, @"^([a-zA-Z0-9_-]+(?:/[a-zA-Z0-9_-]+)*):([1-9][0-9]*)$");
        if (!match.Success) throw new ArgumentException("id 应为搜索返回的 章节:条目序号，如 sjzl:17；序号不是印刷页码");
        if (format is not ("text" or "html")) throw new ArgumentException("format 应为 text 或 html");
        if (offset < 0 || length is < 1 or > 20000) throw new ArgumentException("offset ≥ 0，length 应为 1～20000");
        var page = Load(root, match.Groups[1].Value).FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("没有该页；请先搜索取得 id");
        var content = format == "html" ? page.Html : page.Text;
        if (offset > content.Length) throw new ArgumentException($"offset 超过总长度 {content.Length}");
        var part = content.Substring(offset, Math.Min(length, content.Length - offset));
        var images = Regex.Matches(page.Html, @"<img\b[^>]*>", RegexOptions.IgnoreCase)
            .Select(m => Attribute(m.Value, "src")).Distinct().ToArray();
        return new { id = page.Id, title = page.Title, source = page.File, entry = page.Entry, byteOffset = page.ByteOffset,
            format, offset, totalLength = content.Length, returned = part.Length,
            nextOffset = offset + part.Length < content.Length ? (int?)(offset + part.Length) : null, content = part, images,
            note = "entry 是文件内 HTML 条目序号，不是印刷页码。文本保留表格行/制表符；合并单元格请核对 HTML。图片仅列原始引用，未解析封装图片。封装原文可能有错字、乱码或残缺标签，不自动修正；关键数据须核对原页。" };
    }
}
