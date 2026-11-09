using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Norma;

// REQ-MANUAL-004: derived HTML evidence, explicitly NOT a capture of the native viewer.
internal static class ManualScreenshot
{
    internal const string EvidenceNote = "提取HTML渲染截图，非原版手册窗口；乱码、缺字和图片缺失可能来自提取过程，不能据此猜补数值。";

    internal static void Validate(int y, int width, int height)
    {
        if (y is < 0 or > 50000 || width is < 640 or > 2400 || height is < 300 or > 4000)
            throw new ArgumentException("y 应为0～50000像素，width为640～2400，height为300～4000");
    }

    internal static string Html(Manual.Page page, int y)
    {
        // CSP precedes untrusted manual content. No script, navigation, network or local image access.
        var body = Regex.Match(page.Html, @"<body\b[^>]*>([\s\S]*?)(?:</body\s*>|$)", RegexOptions.IgnoreCase).Groups[1].Value;
        if (body.Length == 0) body = page.Html;
        body = Regex.Replace(body, @"<(script|iframe|object|embed)\b[\s\S]*?</\1\s*>", "", RegexOptions.IgnoreCase);
        body = Regex.Replace(body, @"<meta\b[^>]*>", "", RegexOptions.IgnoreCase);
        return "<!doctype html><html><head><meta charset=utf-8>" +
            "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src data:; form-action 'none'; base-uri 'none'\">" +
            "<style>body{margin:0;background:white;color:black;font-family:SimSun,serif}header{height:58px;padding:6px;box-sizing:border-box;background:#fff3cd;font:14px sans-serif;border-bottom:1px solid #aaa;position:relative;z-index:10}main{position:absolute;top:64px;left:8px;right:8px;overflow:hidden;height:50000px}article{position:relative;top:-" + y + "px}table{border-collapse:collapse}td,th{border:1px solid #888;padding:3px}</style></head><body><header>" +
            WebUtility.HtmlEncode(page.Id + " · " + page.Title + " · y=" + y) + "<br>" + EvidenceNote +
            "</header><main><article>" + body + "</article></main></body></html>";
    }

    internal static object Capture(string root, string id, int y, int width, int height)
    {
        Validate(y, width, height);
        // Reuse read validation before selecting the page.
        _ = Manual.Read(root, id, "text", 0, 1);
        var page = Manual.Load(root, id[..id.LastIndexOf(':')]).Single(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        var edge = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) }
            .Select(d => Path.Combine(d, "Microsoft", "Edge", "Application", "msedge.exe")).FirstOrDefault(File.Exists)
            ?? throw new IOException("截图需要本机已安装 Microsoft Edge；未自动下载或安装");
        var folder = Path.Combine(Path.GetTempPath(), "Norma-manual-capture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var html = Path.Combine(folder, "page.html");
        var png = Path.Combine(folder, "page.png");
        File.WriteAllText(html, Html(page, y), new UTF8Encoding(false));
        var start = new ProcessStartInfo(edge) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--hide-scrollbars", "--run-all-compositor-stages-before-draw", "--virtual-time-budget=1500", "--user-data-dir=" + Path.Combine(folder, "profile"), "--window-size=" + width + "," + height, "--screenshot=" + png, new Uri(html).AbsoluteUri })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("无法启动截图渲染器");
        if (!process.WaitForExit(30000))
        {
            process.Kill(true);
            process.WaitForExit(5000);
            throw new IOException("手册截图渲染超时（30秒）");
        }
        if (!File.Exists(png) || new FileInfo(png).Length < 100) throw new IOException("渲染器未生成有效截图");
        try { Directory.Delete(Path.Combine(folder, "profile"), true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return new { id = page.Id, title = page.Title, source = page.File, byteOffset = page.ByteOffset,
            imagePath = png, htmlPath = html, y, width, height, evidence = "extracted-html", note = EvidenceNote };
    }
}
