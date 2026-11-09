using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Norma;

// REQ-MANUAL-005: system capture in a disposable process; no COM in the unloadable module.
internal static class ManualWindowScreenshot
{
    internal static void Validate(int x, int y, int width, int height)
    {
        if (x is < 0 or > 12000 || y is < 0 or > 12000 || width is < 0 or > 12000 || height is < 0 or > 12000)
            throw new ArgumentException("窗口裁切x/y/width/height应为0～12000像素；width/height=0截到窗口边缘");
    }

    internal static object Capture(string root, string? id, int x, int y, int width, int height)
    {
        return Run(root, "capture", null, null, null, 0, 30, id, x, y, width, height);
    }

    internal static object Open(string root, string name, string? path) => Run(root, "open", name, path, null, 0, 30, null, 0, 0, 0, 0);
    internal static object DirectoryView(string root, string? query, int offset, int limit)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentException("offset≥0，limit为1～100");
        return Run(root, "directory", null, null, query, offset, limit, null, 0, 0, 0, 0);
    }

    private static object Run(string root, string action, string? name, string? path, string? query, int offset, int limit, string? id, int x, int y, int width, int height)
    {
        Validate(x, y, width, height);
        if (action == "open" && string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name必填，应为完整页名称");
        if (!string.IsNullOrWhiteSpace(id)) throw new ArgumentException("window模式截当前原手册窗口，不按id翻页；请在原手册打开所需页后省略id截图。提取页请显式mode=html");
        var directory = new DirectoryInfo(Path.GetFullPath(root));
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ykyManual.exe"))) directory = directory.Parent;
        if (directory is null) throw new ArgumentException("root未找到原手册ykyManual.exe");
        var executable = Path.Combine(directory.FullName, "ykyManual.exe");
        var matches = new List<(int Id, long Handle, string Title)>();
        foreach (var process in Process.GetProcessesByName("ykyManual"))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase) && process.MainWindowHandle != IntPtr.Zero)
                        matches.Add((process.Id, process.MainWindowHandle.ToInt64(), process.MainWindowTitle));
                }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        if (matches.Count != 1) throw new IOException($"匹配的原手册窗口为{matches.Count}个；请在同一交互桌面会话打开且只保留一个指定目录的手册窗口");
        var target = matches[0];
        var folder = Path.Combine(Path.GetTempPath(), "Norma-manual-capture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var script = Path.Combine(folder, "capture.ps1");
        using (var resource = typeof(ManualWindowScreenshot).Assembly.GetManifestResourceStream("Norma.ManualWindowCapture.ps1")!)
        using (var output = File.Create(script)) resource.CopyTo(output);
        var image = Path.Combine(folder, "window.png");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
            "-WindowHandle", target.Handle.ToString(CultureInfo.InvariantCulture), "-ExpectedProcessId", target.Id.ToString(CultureInfo.InvariantCulture),
            "-X", x.ToString(CultureInfo.InvariantCulture), "-Y", y.ToString(CultureInfo.InvariantCulture), "-Width", width.ToString(CultureInfo.InvariantCulture), "-Height", height.ToString(CultureInfo.InvariantCulture), "-OutputPath", image }) start.ArgumentList.Add(arg);
        foreach (var pair in new[] { ("-Action", action), ("-PageName", name), ("-PagePath", path), ("-Query", query), ("-Offset", offset.ToString(CultureInfo.InvariantCulture)), ("-Limit", limit.ToString(CultureInfo.InvariantCulture)) })
            if (pair.Item2 is not null) { start.ArgumentList.Add(pair.Item1); start.ArgumentList.Add(pair.Item2); }
        using var child = Process.Start(start) ?? throw new IOException("无法启动Windows系统截图辅助进程");
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(30000))
        {
            child.Kill(true);
            child.WaitForExit(5000);
            throw new IOException("系统窗口截图超时（30秒），已终止辅助进程");
        }
        if (child.ExitCode != 0 || (action != "open" && !File.Exists(image))) throw new IOException("系统截图失败：" + stderr.GetAwaiter().GetResult());
        var data = JsonSerializer.Deserialize<JsonElement>(stdout.GetAwaiter().GetResult());
        return new { title = target.Title, capture = data };
    }
}
