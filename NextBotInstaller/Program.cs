using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Spectre.Console;

internal static class Program
{
    private const string PythonVersion = "3.14.3";
    private const string NextBotSourceZipUrl = "https://github.com/Arispex/next-bot/archive/refs/heads/main.zip";
    private const string NextBotExtractedFolderName = "next-bot-main";
    private const string NapCatShellZipUrl =
        "https://github.com/NapNeko/NapCatQQ/releases/latest/download/NapCat.Shell.zip";
    private const string NapCatShellWindowsOneKeyZipUrl =
        "https://github.com/NapNeko/NapCatQQ/releases/latest/download/NapCat.Shell.Windows.Node.zip";
    private const string NapCatLinuxInstallScriptUrl =
        "https://raw.githubusercontent.com/NapNeko/napcat-linux-installer/refs/heads/main/install.sh";
    private const string LatestReleaseMetadataUrl =
        "https://raw.githubusercontent.com/astral-sh/python-build-standalone/latest-release/latest-release.json";
    private static readonly string[] BuiltinGithubProxySites =
    {
        "https://ghfast.top/",
        "https://gh-proxy.org/",
        "https://hk.gh-proxy.org/",
        "https://cdn.gh-proxy.org/",
        "https://edgeone.gh-proxy.org/"
    };
    private static readonly HashSet<string> NextBotUpdateProtectedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "python",
        "napcat"
    };
    private static readonly HashSet<string> NextBotUpdateProtectedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env",
        ".webui_auth.json",
        "app.db",
        "run_bot.bat",
        "run_bot.sh",
        "run_napcat.bat",
        "run_napcat.sh"
    };

    private static readonly GithubProxyState GithubProxy = CreateDefaultGithubProxyState();
    private static readonly HttpClient Http = CreateHttpClient();

    private static async Task Main()
    {
        await AutoSelectAvailableProxyAsync();
        ShowWelcomeScreen();

        while (true)
        {
            ShowMenuHeader();
            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold #7dd3fc]请选择功能[/]")
                    .HighlightStyle(new Style(foreground: Color.Black, background: Color.Aquamarine1, decoration: Decoration.Bold))
                    .PageSize(10)
                    .MoreChoicesText("[grey](上下方向键选择，回车确认)[/]")
                    .AddChoices("1. 安装 NextBot", "2. 更新 NextBot", "3. 安装 NapCat", "4. 代理站管理", "0. 退出"));

            switch (selected)
            {
                case "1. 安装 NextBot":
                    try
                    {
                        await RunOneClickInstallAsync();
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]安装失败：[/]{Markup.Escape(ex.Message)}");
                    }

                    break;
                case "2. 更新 NextBot":
                    try
                    {
                        await RunNextBotUpdateAsync();
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]更新失败：[/]{Markup.Escape(ex.Message)}");
                    }

                    break;
                case "3. 安装 NapCat":
                    try
                    {
                        await RunNapCatInstallerAsync();
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]NapCat 安装失败：[/]{Markup.Escape(ex.Message)}");
                    }

                    break;
                case "4. 代理站管理":
                    RunProxyManager();
                    break;
                case "0. 退出":
                    return;
            }

            AnsiConsole.WriteLine();
        }
    }

    private static async Task AutoSelectAvailableProxyAsync()
    {
        await RunWithStatusAsync(
            "测试代理站可用性...",
            async () =>
            {
                for (var i = 0; i < BuiltinGithubProxySites.Length; i++)
                {
                    var rawSite = BuiltinGithubProxySites[i];
                    if (!TryNormalizeProxyBaseUrl(rawSite, out var normalized))
                    {
                        continue;
                    }

                    if (!await IsProxyAvailableAsync(normalized))
                    {
                        continue;
                    }

                    ApplyProxyState(true, normalized, i == 0 ? "自动检测（默认）" : "自动检测");
                    return;
                }

                ApplyProxyState(false, string.Empty, "自动检测");
            });
    }

    private static async Task RunOneClickInstallAsync()
    {
        ShowSectionTitle("安装 NextBot", "下载项目源码、安装 Python 与依赖，并生成启动脚本");
        AnsiConsole.MarkupLine($"[grey]GitHub 代理：[/][white]{Markup.Escape(GetGithubProxyStatusText())}[/]");
        AnsiConsole.WriteLine();

        var workingDirectory = Directory.GetCurrentDirectory();
        var cacheDirectory = Path.Combine(workingDirectory, ".installer-cache");
        var installDirectory = Path.Combine(workingDirectory, "python");

        Directory.CreateDirectory(cacheDirectory);

        var sourceZipPath = Path.Combine(cacheDirectory, "next-bot-main.zip");
        await RunWithStatusAsync(
            "步骤 1/8 下载 NextBot...",
            () => DownloadFileAsync(NextBotSourceZipUrl, sourceZipPath));

        await RunWithStatusAsync(
            "步骤 2/8 解压并同步到当前目录...",
            () =>
            {
                DeployProjectSource(sourceZipPath, workingDirectory, cacheDirectory);
                return Task.CompletedTask;
            });
        DeleteFileIfExists(sourceZipPath);

        if (Directory.Exists(installDirectory))
        {
            var overwrite = AnsiConsole.Confirm("检测到当前目录已存在 [green]python[/] 文件夹，是否覆盖？", false);
            if (!overwrite)
            {
                AnsiConsole.MarkupLine("[yellow]已取消安装。[/]");
                return;
            }

            Directory.Delete(installDirectory, true);
        }

        Directory.CreateDirectory(installDirectory);

        var archivePlan = await RunWithStatusAsync(
            "步骤 3/8 解析可用的 Python 压缩包...",
            () => ResolveArchivePlanAsync(PythonVersion));
        var archivePath = Path.Combine(cacheDirectory, archivePlan.FileName);

        await RunWithStatusAsync(
            $"步骤 4/8 下载 {archivePlan.FileName}...",
            () => DownloadFileAsync(archivePlan.Url, archivePath));

        await RunWithStatusAsync(
            "步骤 5/8 解压 Python 到当前目录...",
            () => ExtractArchiveAsync(archivePath, installDirectory));
        DeleteFileIfExists(archivePath);

        var pythonExecutable = FindPythonExecutable(installDirectory);
        AnsiConsole.MarkupLine(
            $"[grey]Python 可执行文件：[/][white]{Markup.Escape(Path.GetRelativePath(workingDirectory, pythonExecutable))}[/]");

        await RunWithStatusAsync(
            "步骤 6/8 安装 uv 并执行 uv sync...",
            async () =>
            {
                await RunProcessAsync(pythonExecutable, new[] { "-m", "ensurepip", "--upgrade" }, workingDirectory);
                await RunProcessAsync(pythonExecutable, new[] { "-m", "pip", "install", "--upgrade", "pip", "uv" },
                    workingDirectory);
                await RunProcessAsync(pythonExecutable, new[] { "-m", "uv", "sync" }, workingDirectory);
            });

        await RunWithStatusAsync(
            "步骤 7/8 安装 Playwright Chromium...",
            () => RunProcessAsync(
                pythonExecutable,
                new[] { "-m", "uv", "run", "python", "-m", "playwright", "install", "chromium" },
                workingDirectory));

        var scriptPath = await RunWithStatusAsync(
            "步骤 8/8 生成启动脚本...",
            () => Task.FromResult(CreateRunScript(workingDirectory, pythonExecutable)));

        AnsiConsole.WriteLine();
        var summaryGrid = new Grid();
        summaryGrid.AddColumn(new GridColumn().NoWrap());
        summaryGrid.AddColumn();
        summaryGrid.AddRow(new Markup("[grey]安装目录[/]"), new Markup($"[white]{Markup.Escape(workingDirectory)}[/]"));
        summaryGrid.AddRow(new Markup("[grey]Python 安装目录[/]"), new Markup($"[white]{Markup.Escape(installDirectory)}[/]"));
        summaryGrid.AddRow(new Markup("[grey]Python 版本[/]"), new Markup($"[white]{Markup.Escape(PythonVersion)}[/]"));
        summaryGrid.AddRow(new Markup("[grey]启动脚本[/]"),
            new Markup($"[white]{Markup.Escape(Path.GetRelativePath(workingDirectory, scriptPath))}[/]"));

        AnsiConsole.Write(
            new Panel(summaryGrid)
                .Header("[bold #4ade80]安装完成[/]")
                .Border(BoxBorder.Rounded)
                .BorderStyle(new Style(foreground: Color.Green))
                .Padding(1, 0, 1, 0));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[green]运行 {Markup.Escape(Path.GetFileName(scriptPath))} 即可运行 NextBot！[/]");
    }

    private static async Task RunNextBotUpdateAsync()
    {
        ShowSectionTitle("更新 NextBot", "清理非保留内容、同步最新源码并执行 uv sync");
        AnsiConsole.MarkupLine($"[grey]GitHub 代理：[/][white]{Markup.Escape(GetGithubProxyStatusText())}[/]");
        AnsiConsole.WriteLine();

        var workingDirectory = Directory.GetCurrentDirectory();
        var cacheDirectory = Path.Combine(workingDirectory, ".installer-cache");
        var installDirectory = Path.Combine(workingDirectory, "python");
        Directory.CreateDirectory(cacheDirectory);

        if (!Directory.Exists(installDirectory))
        {
            throw new InvalidOperationException("未找到 python 环境，请先执行“安装 NextBot”。");
        }

        var pythonExecutable = FindPythonExecutable(installDirectory);
        var sourceZipPath = Path.Combine(cacheDirectory, "next-bot-main.zip");

        await RunWithStatusAsync(
            "步骤 1/5 下载 NextBot...",
            () => DownloadFileAsync(NextBotSourceZipUrl, sourceZipPath));

        await RunWithStatusAsync(
            "步骤 2/5 清理旧文件并同步源码...",
            () =>
            {
                UpdateProjectSource(sourceZipPath, workingDirectory, cacheDirectory);
                return Task.CompletedTask;
            });
        DeleteFileIfExists(sourceZipPath);

        await RunWithStatusAsync(
            "步骤 3/5 校验 Python 环境...",
            () =>
            {
                if (!File.Exists(pythonExecutable))
                {
                    throw new FileNotFoundException("未找到可用的 Python 可执行文件，请先执行“安装 NextBot”。");
                }

                return Task.CompletedTask;
            });

        await RunWithStatusAsync(
            "步骤 4/5 安装 uv 并执行 uv sync...",
            async () =>
            {
                await RunProcessAsync(pythonExecutable, new[] { "-m", "ensurepip", "--upgrade" }, workingDirectory);
                await RunProcessAsync(pythonExecutable, new[] { "-m", "pip", "install", "--upgrade", "pip", "uv" },
                    workingDirectory);
                await RunProcessAsync(pythonExecutable, new[] { "-m", "uv", "sync" }, workingDirectory);
            });

        await RunWithStatusAsync(
            "步骤 5/5 完成更新...",
            () => Task.CompletedTask);

        var summaryGrid = new Grid();
        summaryGrid.AddColumn(new GridColumn().NoWrap());
        summaryGrid.AddColumn();
        summaryGrid.AddRow(new Markup("[grey]更新目录[/]"), new Markup($"[white]{Markup.Escape(workingDirectory)}[/]"));
        summaryGrid.AddRow(new Markup("[grey]Python 目录[/]"), new Markup($"[white]{Markup.Escape(installDirectory)}[/]"));

        AnsiConsole.Write(
            new Panel(summaryGrid)
                .Header("[bold #4ade80]更新完成[/]")
                .Border(BoxBorder.Rounded)
                .BorderStyle(new Style(foreground: Color.Green))
                .Padding(1, 0, 1, 0));
    }

    private static async Task RunNapCatInstallerAsync()
    {
        ShowSectionTitle("安装 NapCat", "下载 NapCat 并解压到根目录 napcat 文件夹");
        AnsiConsole.MarkupLine($"[grey]GitHub 代理：[/][white]{Markup.Escape(GetGithubProxyStatusText())}[/]");
        AnsiConsole.WriteLine();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await RunNapCatLinuxInstallerAsync();
            return;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            AnsiConsole.MarkupLine("[yellow]当前仅实现 Windows 和 Linux 安装流程。[/]");
            return;
        }

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold #7dd3fc]请选择 NapCat 安装方式[/]")
                .HighlightStyle(new Style(foreground: Color.Black, background: Color.Aquamarine1, decoration: Decoration.Bold))
                .AddChoices("1. NapCat.Shell", "2. NapCat.Shell.Windows.OneKey", "0. 返回"));

        string? packageUrl = selected switch
        {
            "1. NapCat.Shell" => NapCatShellZipUrl,
            "2. NapCat.Shell.Windows.OneKey" => NapCatShellWindowsOneKeyZipUrl,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(packageUrl))
        {
            AnsiConsole.MarkupLine("[yellow]已返回主菜单。[/]");
            return;
        }

        var workingDirectory = Directory.GetCurrentDirectory();
        var cacheDirectory = Path.Combine(workingDirectory, ".installer-cache");
        var targetDirectory = Path.Combine(workingDirectory, "napcat");
        Directory.CreateDirectory(cacheDirectory);

        if (Directory.Exists(targetDirectory))
        {
            var overwrite = AnsiConsole.Confirm("检测到 [green]napcat[/] 文件夹已存在，是否覆盖？", false);
            if (!overwrite)
            {
                AnsiConsole.MarkupLine("[yellow]已取消安装。[/]");
                return;
            }

            Directory.Delete(targetDirectory, true);
        }

        Directory.CreateDirectory(targetDirectory);

        var archiveFileName = Path.GetFileName(new Uri(packageUrl).AbsolutePath);
        var archivePath = Path.Combine(cacheDirectory, archiveFileName);

        await RunWithStatusAsync(
            $"步骤 1/3 下载 {archiveFileName}...",
            () => DownloadFileAsync(packageUrl, archivePath));

        await RunWithStatusAsync(
            "步骤 2/3 解压 NapCat 到 napcat 目录...",
            () => ExtractArchiveAsync(archivePath, targetDirectory));
        DeleteFileIfExists(archivePath);

        var scriptPath = await RunWithStatusAsync(
            "步骤 3/3 生成 NapCat 启动脚本...",
            () => Task.FromResult(CreateNapCatRunScript(workingDirectory, selected)));

        var summaryGrid = new Grid();
        summaryGrid.AddColumn(new GridColumn().NoWrap());
        summaryGrid.AddColumn();
        summaryGrid.AddRow(new Markup("[grey]安装目录[/]"), new Markup($"[white]{Markup.Escape(targetDirectory)}[/]"));
        summaryGrid.AddRow(new Markup("[grey]安装方式[/]"), new Markup($"[white]{Markup.Escape(selected)}[/]"));
        summaryGrid.AddRow(new Markup("[grey]启动脚本[/]"),
            new Markup($"[white]{Markup.Escape(Path.GetRelativePath(workingDirectory, scriptPath))}[/]"));

        AnsiConsole.Write(
            new Panel(summaryGrid)
                .Header("[bold #4ade80]NapCat 安装完成[/]")
                .Border(BoxBorder.Rounded)
                .BorderStyle(new Style(foreground: Color.Green))
                .Padding(1, 0, 1, 0));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[green]运行 {Markup.Escape(Path.GetFileName(scriptPath))} 即可启动 NapCat！[/]");
    }

    [SupportedOSPlatform("linux")]
    private static async Task RunNapCatLinuxInstallerAsync()
    {
        if (!IsRunningAsRoot())
        {
            AnsiConsole.MarkupLine("[yellow]Linux 安装 NapCat 需要 root 权限。[/]");
            AnsiConsole.MarkupLine("[yellow]请使用 sudo 运行此程序，或切换到 root 用户后再执行。[/]");
            return;
        }

        var workingDirectory = Directory.GetCurrentDirectory();
        var targetDirectory = Path.Combine(workingDirectory, "napcat");

        if (Directory.Exists(targetDirectory))
        {
            var overwrite = AnsiConsole.Confirm("检测到 [green]napcat[/] 文件夹已存在，是否覆盖？", false);
            if (!overwrite)
            {
                AnsiConsole.MarkupLine("[yellow]已取消安装。[/]");
                return;
            }

            Directory.Delete(targetDirectory, true);
        }

        Directory.CreateDirectory(targetDirectory);

        var installScriptPath = Path.Combine(targetDirectory, "install.sh");
        await RunWithStatusAsync(
            "步骤 1/3 下载 Linux 安装脚本...",
            () => DownloadFileAsync(NapCatLinuxInstallScriptUrl, installScriptPath));

        File.SetUnixFileMode(installScriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]步骤 2/3 执行 Linux 安装脚本...[/]");
        AnsiConsole.Write(
            new Rule("[grey]NapCat 安装日志[/]")
                .RuleStyle("grey")
                .LeftJustified());

        await RunProcessWithLiveOutputAsync("bash", new[] { "install.sh" }, targetDirectory);

        var scriptPath = await RunWithStatusAsync(
            "步骤 3/3 生成 NapCat 启动脚本...",
            () => Task.FromResult(CreateNapCatRunScript(workingDirectory, "linux")));

        var summaryGrid = new Grid();
        summaryGrid.AddColumn(new GridColumn().NoWrap());
        summaryGrid.AddColumn();
        summaryGrid.AddRow(new Markup("[grey]安装目录[/]"), new Markup($"[white]{Markup.Escape(targetDirectory)}[/]"));
        summaryGrid.AddRow(new Markup("[grey]安装方式[/]"), new Markup("[white]NapCat.Linux.Launcher[/]"));
        summaryGrid.AddRow(new Markup("[grey]启动脚本[/]"),
            new Markup($"[white]{Markup.Escape(Path.GetRelativePath(workingDirectory, scriptPath))}[/]"));

        AnsiConsole.WriteLine();
        AnsiConsole.Write(
            new Panel(summaryGrid)
                .Header("[bold #4ade80]NapCat 安装完成[/]")
                .Border(BoxBorder.Rounded)
                .BorderStyle(new Style(foreground: Color.Green))
                .Padding(1, 0, 1, 0));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[green]运行 {Markup.Escape(Path.GetFileName(scriptPath))} 即可启动 NapCat！[/]");
    }

    private static void DeployProjectSource(string sourceZipPath, string workingDirectory, string cacheDirectory)
    {
        var tempExtractRoot = Path.Combine(cacheDirectory, $"src-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempExtractRoot);

        try
        {
            ZipFile.ExtractToDirectory(sourceZipPath, tempExtractRoot, true);
            var extractedRoot = ResolveExtractedSourceRoot(tempExtractRoot);

            MergeDirectoryIntoTarget(extractedRoot, workingDirectory);
        }
        finally
        {
            if (Directory.Exists(tempExtractRoot))
            {
                Directory.Delete(tempExtractRoot, true);
            }
        }
    }

    private static void UpdateProjectSource(string sourceZipPath, string workingDirectory, string cacheDirectory)
    {
        var tempExtractRoot = Path.Combine(cacheDirectory, $"src-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempExtractRoot);

        try
        {
            ZipFile.ExtractToDirectory(sourceZipPath, tempExtractRoot, true);
            var extractedRoot = ResolveExtractedSourceRoot(tempExtractRoot);

            CleanupWorkingDirectoryForUpdate(workingDirectory, cacheDirectory);
            MergeDirectoryIntoTarget(extractedRoot, workingDirectory);
        }
        finally
        {
            if (Directory.Exists(tempExtractRoot))
            {
                Directory.Delete(tempExtractRoot, true);
            }
        }
    }

    private static void CleanupWorkingDirectoryForUpdate(string workingDirectory, string cacheDirectory)
    {
        var currentProcessPath = Environment.ProcessPath;
        var protectedProcessFileName =
            currentProcessPath is not null &&
            string.Equals(Path.GetDirectoryName(currentProcessPath), workingDirectory, StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileName(currentProcessPath)
                : null;

        foreach (var directoryPath in Directory.EnumerateDirectories(workingDirectory))
        {
            var name = Path.GetFileName(directoryPath);
            if (string.Equals(directoryPath, cacheDirectory, StringComparison.OrdinalIgnoreCase) ||
                NextBotUpdateProtectedDirectories.Contains(name))
            {
                continue;
            }

            Directory.Delete(directoryPath, true);
        }

        foreach (var filePath in Directory.EnumerateFiles(workingDirectory))
        {
            var name = Path.GetFileName(filePath);
            if (NextBotUpdateProtectedFiles.Contains(name) ||
                string.Equals(name, protectedProcessFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Delete(filePath);
        }
    }

    private static string ResolveExtractedSourceRoot(string tempExtractRoot)
    {
        var preferred = Path.Combine(tempExtractRoot, NextBotExtractedFolderName);
        if (Directory.Exists(preferred))
        {
            return preferred;
        }

        var subdirectories = Directory.GetDirectories(tempExtractRoot);
        var hasFiles = Directory.GetFiles(tempExtractRoot).Length > 0;

        if (subdirectories.Length == 1 && !hasFiles)
        {
            return subdirectories[0];
        }

        if (subdirectories.Length == 0 && !hasFiles)
        {
            throw new DirectoryNotFoundException("未找到解压后的源码目录：压缩包内容为空");
        }

        return tempExtractRoot;
    }

    private static void MergeDirectoryIntoTarget(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory))
        {
            var fileName = Path.GetFileName(filePath);
            var targetPath = Path.Combine(targetDirectory, fileName);
            File.Move(filePath, targetPath, true);
        }

        foreach (var subDirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var name = Path.GetFileName(subDirectory);
            var targetSubDirectory = Path.Combine(targetDirectory, name);
            MergeDirectoryIntoTarget(subDirectory, targetSubDirectory);
            Directory.Delete(subDirectory, true);
        }
    }

    private static void ShowWelcomeScreen()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(
            new FigletText("NextBot")
                .Centered()
                .Color(Color.Aquamarine1));
        AnsiConsole.Write(new Rule("[bold #7dd3fc]Installer Console[/]").RuleStyle("grey").Centered());

        var infoGrid = new Grid();
        infoGrid.AddColumn(new GridColumn().NoWrap());
        infoGrid.AddColumn();
        infoGrid.AddRow(new Markup("[grey]系统[/]"), new Markup($"[white]{Markup.Escape(GetCurrentOsName())}[/]"));
        infoGrid.AddRow(new Markup("[grey]架构[/]"), new Markup($"[white]{Markup.Escape(RuntimeInformation.ProcessArchitecture.ToString())}[/]"));
        infoGrid.AddRow(new Markup("[grey]GitHub 代理[/]"), new Markup($"[white]{Markup.Escape(GetGithubProxyStatusText())}[/]"));
        infoGrid.AddRow(new Markup("[grey]运行目录[/]"), new Markup($"[white]{Markup.Escape(Directory.GetCurrentDirectory())}[/]"));

        AnsiConsole.Write(
            new Panel(infoGrid)
                .Header("[bold #93c5fd]欢迎使用[/]")
                .Border(BoxBorder.Rounded)
                .Padding(1, 0, 1, 0));
        AnsiConsole.WriteLine();
    }

    private static void ShowMenuHeader()
    {
        AnsiConsole.Write(new Rule("[grey]Main Menu[/]").RuleStyle("grey").LeftJustified());
    }

    private static void ShowSectionTitle(string title, string subtitle)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(
            new Rule($"[bold #7dd3fc]{Markup.Escape(title)}[/]")
                .RuleStyle("grey")
                .LeftJustified());
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(subtitle)}[/]");
        AnsiConsole.WriteLine();
    }

    private static async Task RunWithStatusAsync(string message, Func<Task> action)
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(new Style(foreground: Color.Aquamarine1))
            .StartAsync($"[bold #7dd3fc]{Markup.Escape(message)}[/]", async _ => await action());
    }

    private static async Task<T> RunWithStatusAsync<T>(string message, Func<Task<T>> action)
    {
        return await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(new Style(foreground: Color.Aquamarine1))
            .StartAsync($"[bold #7dd3fc]{Markup.Escape(message)}[/]", async _ => await action());
    }

    private static string GetCurrentOsName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "Windows";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "macOS";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "Linux";
        }

        return RuntimeInformation.OSDescription;
    }

    private static string ToProxiedGithubUrl(string url)
    {
        if (!GithubProxy.Enabled || string.IsNullOrWhiteSpace(GithubProxy.BaseUrl))
        {
            return url;
        }

        if (url.StartsWith(GithubProxy.BaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        if (!IsGithubHost(uri.Host))
        {
            return url;
        }

        return $"{GithubProxy.BaseUrl}{url}";
    }

    private static GithubProxyState CreateDefaultGithubProxyState()
    {
        var defaultUrl = BuiltinGithubProxySites.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(defaultUrl) &&
            TryNormalizeProxyBaseUrl(defaultUrl, out var normalized))
        {
            return new GithubProxyState
            {
                Enabled = true,
                BaseUrl = normalized,
                Source = "默认"
            };
        }

        return new GithubProxyState
        {
            Enabled = false,
            BaseUrl = string.Empty,
            Source = "默认"
        };
    }

    private static void RunProxyManager()
    {
        ShowSectionTitle("代理站管理", "可关闭代理、切换预设代理，或临时添加代理站");

        while (true)
        {
            var proxyGrid = new Grid();
            proxyGrid.AddColumn(new GridColumn().NoWrap());
            proxyGrid.AddColumn();
            proxyGrid.AddRow(new Markup("[grey]当前状态[/]"), new Markup($"[white]{Markup.Escape(GetGithubProxyStatusText())}[/]"));
            proxyGrid.AddRow(new Markup("[grey]默认代理站[/]"), new Markup($"[white]{Markup.Escape(BuiltinGithubProxySites[0])}[/]"));
            proxyGrid.AddRow(new Markup("[grey]预设数量[/]"), new Markup($"[white]{BuiltinGithubProxySites.Length}[/]"));

            AnsiConsole.Write(
                new Panel(proxyGrid)
                    .Header("[bold #93c5fd]代理配置[/]")
                    .Border(BoxBorder.Rounded)
                    .Padding(1, 0, 1, 0));

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold #7dd3fc]请选择代理操作[/]")
                    .HighlightStyle(new Style(foreground: Color.Black, background: Color.Aquamarine1, decoration: Decoration.Bold))
                    .AddChoices(
                        "使用默认代理站",
                        "选择预设代理站",
                        "临时添加并使用代理站",
                        "关闭代理站",
                        "返回"));

            switch (selected)
            {
                case "使用默认代理站":
                {
                    if (!TryNormalizeProxyBaseUrl(BuiltinGithubProxySites[0], out var normalized))
                    {
                        AnsiConsole.MarkupLine("[red]默认代理站格式无效。[/]");
                        break;
                    }

                    ApplyProxyState(true, normalized, "默认");
                    AnsiConsole.MarkupLine($"[green]已切换到默认代理：[/]{Markup.Escape(normalized)}");
                    break;
                }
                case "选择预设代理站":
                {
                    var proxy = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[bold #7dd3fc]请选择一个预设代理站[/]")
                            .HighlightStyle(new Style(foreground: Color.Black, background: Color.Aquamarine1, decoration: Decoration.Bold))
                            .AddChoices(BuiltinGithubProxySites));

                    if (!TryNormalizeProxyBaseUrl(proxy, out var normalized))
                    {
                        AnsiConsole.MarkupLine("[red]代理地址格式无效。[/]");
                        break;
                    }

                    ApplyProxyState(true, normalized, "预设");
                    AnsiConsole.MarkupLine($"[green]已切换代理：[/]{Markup.Escape(normalized)}");
                    break;
                }
                case "临时添加并使用代理站":
                {
                    var input = AnsiConsole.Prompt(
                        new TextPrompt<string>("请输入代理站地址（例如 https://gh-proxy.org/）")
                            .Validate(value =>
                                TryNormalizeProxyBaseUrl(value, out _)
                                    ? ValidationResult.Success()
                                    : ValidationResult.Error("[red]请输入有效的 http/https 代理地址[/]")));

                    TryNormalizeProxyBaseUrl(input, out var normalized);
                    ApplyProxyState(true, normalized, "临时");
                    AnsiConsole.MarkupLine($"[green]已使用临时代理：[/]{Markup.Escape(normalized)}");
                    break;
                }
                case "关闭代理站":
                    ApplyProxyState(false, string.Empty, "手动关闭");
                    AnsiConsole.MarkupLine("[yellow]代理站已关闭。[/]");
                    break;
                case "返回":
                    return;
            }

            AnsiConsole.WriteLine();
        }
    }

    private static bool IsGithubHost(string host)
    {
        var normalized = host.ToLowerInvariant();
        return normalized == "github.com" ||
               normalized.EndsWith(".github.com", StringComparison.Ordinal) ||
               normalized == "githubusercontent.com" ||
               normalized.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
    }

    private static bool TryNormalizeProxyBaseUrl(string raw, out string normalized)
    {
        var candidate = raw.Trim();
        if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            candidate = $"https://{candidate}";
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            normalized = string.Empty;
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            normalized = string.Empty;
            return false;
        }

        var rebuilt = uri.ToString();
        normalized = rebuilt.EndsWith('/') ? rebuilt : $"{rebuilt}/";
        return true;
    }

    private static string GetGithubProxyStatusText()
    {
        if (!GithubProxy.Enabled)
        {
            return $"已禁用（{GithubProxy.Source}）";
        }

        return $"{GithubProxy.BaseUrl}（{GithubProxy.Source}）";
    }

    private static void ApplyProxyState(bool enabled, string baseUrl, string source)
    {
        GithubProxy.Enabled = enabled;
        GithubProxy.BaseUrl = baseUrl;
        GithubProxy.Source = source;
    }

    private static async Task<ArchivePlan> ResolveArchivePlanAsync(string pythonVersion)
    {
        var releaseJson = await Http.GetStringAsync(ToProxiedGithubUrl(LatestReleaseMetadataUrl));
        var metadata = ParseLatestReleaseMetadata(releaseJson);

        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Tag) || string.IsNullOrWhiteSpace(metadata.AssetUrlPrefix))
        {
            throw new InvalidOperationException("无法解析 python-build-standalone 发布信息。");
        }

        var fileCandidates = BuildArchiveCandidates(pythonVersion, metadata.Tag).ToList();
        foreach (var fileName in fileCandidates)
        {
            var url = $"{metadata.AssetUrlPrefix}/{fileName}";
            if (await UrlExistsAsync(url))
            {
                return new ArchivePlan(url, fileName);
            }
        }

        throw new InvalidOperationException(
            "未找到当前系统可用的 Python 3.14.3 压缩包。请检查网络连接和代理设置后重试。"
        );
    }

    private static IEnumerable<string> BuildArchiveCandidates(string pythonVersion, string tag)
    {
        var candidates = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var windowsArch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "aarch64-pc-windows-msvc",
                _ => "x86_64-pc-windows-msvc"
            };

            candidates.Add($"cpython-{pythonVersion}+{tag}-{windowsArch}-shared-install_only.zip");
            candidates.Add($"cpython-{pythonVersion}+{tag}-{windowsArch}-shared-install_only_stripped.zip");
            candidates.Add($"cpython-{pythonVersion}+{tag}-{windowsArch}-shared-install_only.tar.gz");
            candidates.Add($"cpython-{pythonVersion}+{tag}-{windowsArch}-install_only.zip");
            candidates.Add($"cpython-{pythonVersion}+{tag}-{windowsArch}-install_only.tar.gz");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var macArch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x86_64-apple-darwin",
                _ => "aarch64-apple-darwin"
            };

            candidates.Add($"cpython-{pythonVersion}+{tag}-{macArch}-install_only_stripped.tar.gz");
            candidates.Add($"cpython-{pythonVersion}+{tag}-{macArch}-install_only.tar.gz");
            candidates.Add($"cpython-{pythonVersion}+{tag}-{macArch}-install_only.zip");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var linuxArchCandidates = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => new[]
                {
                    "aarch64-unknown-linux-gnu",
                    "aarch64-unknown-linux-musl"
                },
                _ => new[]
                {
                    "x86_64_v3-unknown-linux-gnu",
                    "x86_64-unknown-linux-gnu",
                    "x86_64_v2-unknown-linux-gnu",
                    "x86_64-unknown-linux-musl"
                }
            };

            foreach (var arch in linuxArchCandidates)
            {
                candidates.Add($"cpython-{pythonVersion}+{tag}-{arch}-install_only_stripped.tar.gz");
                candidates.Add($"cpython-{pythonVersion}+{tag}-{arch}-install_only.tar.gz");
                candidates.Add($"cpython-{pythonVersion}+{tag}-{arch}-install_only.zip");
            }
        }

        return candidates;
    }

    private static async Task<bool> UrlExistsAsync(string url)
    {
        return await IsUrlReachableAsync(ToProxiedGithubUrl(url));
    }

    private static async Task<bool> IsProxyAvailableAsync(string proxyBaseUrl)
    {
        var probeUrl = $"{proxyBaseUrl}{LatestReleaseMetadataUrl}";
        return await IsUrlReachableAsync(probeUrl);
    }

    private static async Task<bool> IsUrlReachableAsync(string requestUrl)
    {
        try
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, requestUrl);
            using var headResponse = await Http.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead);
            if (headResponse.IsSuccessStatusCode)
            {
                return true;
            }

            if (headResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }
        catch
        {
            // ignore and fallback to ranged GET
        }

        try
        {
            using var getRequest = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            getRequest.Headers.Range = new RangeHeaderValue(0, 0);
            using var getResponse = await Http.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead);
            return getResponse.IsSuccessStatusCode || getResponse.StatusCode == HttpStatusCode.PartialContent;
        }
        catch
        {
            return false;
        }
    }

    private static async Task DownloadFileAsync(string url, string outputPath)
    {
        var requestUrl = ToProxiedGithubUrl(url);
        using var response = await Http.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var outputStream = File.Create(outputPath);
        await using var networkStream = await response.Content.ReadAsStreamAsync();
        await networkStream.CopyToAsync(outputStream);
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static async Task ExtractArchiveAsync(string archivePath, string targetDirectory)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, targetDirectory, true);
            return;
        }

        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            if (await TryExtractTarWithCommandAsync(archivePath, targetDirectory))
            {
                return;
            }

            using var fileStream = File.OpenRead(archivePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzipStream, targetDirectory, true);
            return;
        }

        throw new NotSupportedException($"不支持的压缩包格式: {archivePath}");
    }

    private static async Task<bool> TryExtractTarWithCommandAsync(string archivePath, string targetDirectory)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "tar",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            info.ArgumentList.Add("-xzf");
            info.ArgumentList.Add(archivePath);
            info.ArgumentList.Add("-C");
            info.ArgumentList.Add(targetDirectory);

            using var process = Process.Start(info);
            if (process is null)
            {
                return false;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync());

            if (process.ExitCode == 0)
            {
                return true;
            }

            var details = (errorTask.Result + Environment.NewLine + outputTask.Result).Trim();
            if (!string.IsNullOrWhiteSpace(details))
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]系统 tar 解压失败，将回退到 .NET 解压：[/]{Markup.Escape(details)}");
            }
        }
        catch
        {
            // Fall back to managed extraction below.
        }

        return false;
    }

    private static string FindPythonExecutable(string installDirectory)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var candidateFiles = Directory
            .EnumerateFiles(installDirectory, isWindows ? "python.exe" : "python*", SearchOption.AllDirectories)
            .Where(path =>
            {
                if (isWindows)
                {
                    return true;
                }

                var fileName = Path.GetFileName(path);
                return fileName is "python" or "python3";
            })
            .ToList();

        if (candidateFiles.Count == 0)
        {
            throw new FileNotFoundException("解压后未找到 Python 可执行文件。");
        }

        candidateFiles.Sort((a, b) => ScorePythonPath(a).CompareTo(ScorePythonPath(b)));
        return candidateFiles[0];
    }

    private static int ScorePythonPath(string path)
    {
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        var score = normalized.Count(c => c == '/');

        if (normalized.Contains("/python/bin/", StringComparison.Ordinal))
        {
            score -= 40;
        }

        if (normalized.Contains("/bin/", StringComparison.Ordinal))
        {
            score -= 15;
        }

        if (normalized.EndsWith("/python.exe", StringComparison.Ordinal) ||
            normalized.EndsWith("/python3", StringComparison.Ordinal) ||
            normalized.EndsWith("/python", StringComparison.Ordinal))
        {
            score -= 10;
        }

        if (normalized.Contains("test", StringComparison.Ordinal))
        {
            score += 100;
        }

        return score;
    }

    private static async Task RunProcessAsync(string fileName, IReadOnlyList<string> args, string workingDirectory)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info);
        if (process is null)
        {
            throw new InvalidOperationException("进程启动失败。");
        }

        var outputBuffer = new StringBuilder();

        var stdOutTask = ReadAndBufferAsync(process.StandardOutput, outputBuffer);
        var stdErrTask = ReadAndBufferAsync(process.StandardError, outputBuffer);

        await Task.WhenAll(stdOutTask, stdErrTask, process.WaitForExitAsync());

        if (process.ExitCode != 0)
        {
            var commandPreview = $"{fileName} {string.Join(' ', args)}";
            throw new InvalidOperationException(
                $"命令执行失败（exit code: {process.ExitCode}）: {commandPreview}\n{outputBuffer}");
        }
    }

    private static async Task RunProcessWithLiveOutputAsync(string fileName, IReadOnlyList<string> args, string workingDirectory)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info);
        if (process is null)
        {
            throw new InvalidOperationException("进程启动失败。");
        }

        var outputBuffer = new StringBuilder();
        var stdOutTask = ReadBufferAndForwardAsync(process.StandardOutput, outputBuffer);
        var stdErrTask = ReadBufferAndForwardAsync(process.StandardError, outputBuffer);

        await Task.WhenAll(stdOutTask, stdErrTask, process.WaitForExitAsync());

        if (process.ExitCode != 0)
        {
            var commandPreview = $"{fileName} {string.Join(' ', args)}";
            throw new InvalidOperationException(
                $"命令执行失败（exit code: {process.ExitCode}）: {commandPreview}\n{outputBuffer}");
        }
    }

    private static async Task ReadAndBufferAsync(StreamReader reader, StringBuilder outputBuffer)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            lock (outputBuffer)
            {
                outputBuffer.AppendLine(line);
            }
        }
    }

    private static async Task ReadBufferAndForwardAsync(StreamReader reader, StringBuilder outputBuffer)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            lock (outputBuffer)
            {
                outputBuffer.AppendLine(line);
            }

            AnsiConsole.WriteLine(line);
        }
    }

    private static string CreateRunScript(string workingDirectory, string pythonExecutable)
    {
        var relativePythonDirectory = Path.GetDirectoryName(Path.GetRelativePath(workingDirectory, pythonExecutable)) ?? ".";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            relativePythonDirectory = relativePythonDirectory.Replace('/', '\\');
            var relativeScriptsDirectory = Path.Combine(relativePythonDirectory, "Scripts");
            var scriptPath = Path.Combine(workingDirectory, "run_bot.bat");
            var script =
                "@echo off\r\n" +
                "setlocal\r\n" +
                "cd /d \"%~dp0\"\r\n" +
                $"set \"PATH=%~dp0{relativePythonDirectory};%~dp0{relativeScriptsDirectory};%PATH%\"\r\n" +
                "uv run python bot.py\r\n";

            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
            return scriptPath;
        }

        relativePythonDirectory = relativePythonDirectory.Replace('\\', '/');
        var unixScriptPath = Path.Combine(workingDirectory, "run_bot.sh");
        var unixScript =
            "#!/usr/bin/env bash\n" +
            "set -euo pipefail\n" +
            "cd \"$(dirname \"$0\")\"\n" +
            $"export PATH=\"$PWD/{relativePythonDirectory}:$PATH\"\n" +
            "uv run python bot.py\n";

        File.WriteAllText(unixScriptPath, unixScript, new UTF8Encoding(false));
        File.SetUnixFileMode(unixScriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        return unixScriptPath;
    }

    private static string CreateNapCatRunScript(string workingDirectory, string installMode)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (!string.Equals(installMode, "linux", StringComparison.Ordinal))
            {
                throw new NotSupportedException($"未知的 NapCat 安装方式：{installMode}");
            }

            var unixScriptPath = Path.Combine(workingDirectory, "run_napcat.sh");
            var unixScript =
                "#!/usr/bin/env bash\n" +
                "set -euo pipefail\n" +
                "cd \"$(dirname \"$0\")/napcat\"\n" +
                "bash \"./launcher.sh\"\n";

            File.WriteAllText(unixScriptPath, unixScript, new UTF8Encoding(false));
            File.SetUnixFileMode(unixScriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            return unixScriptPath;
        }

        var scriptPath = Path.Combine(workingDirectory, "run_napcat.bat");
        var launcherRelativePath = installMode switch
        {
            "1. NapCat.Shell" => IsWindows11() ? "napcat\\launcher.bat" : "napcat\\launcher-win10.bat",
            "2. NapCat.Shell.Windows.OneKey" => "napcat\\napcat.bat",
            _ => throw new NotSupportedException($"未知的 NapCat 安装方式：{installMode}")
        };

        var script =
            "@echo off\r\n" +
            "setlocal\r\n" +
            "cd /d \"%~dp0\"\r\n" +
            $"call \"{launcherRelativePath}\"\r\n";

        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
        return scriptPath;
    }

    private static bool IsWindows11()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
               Environment.OSVersion.Version.Build >= 22000;
    }

    [SupportedOSPlatform("linux")]
    private static bool IsRunningAsRoot()
    {
        return geteuid() == 0;
    }

    [DllImport("libc")]
    private static extern uint geteuid();

    private static LatestReleaseMetadata? ParseLatestReleaseMetadata(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty("tag", out var tagElement) ||
                tagElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty("asset_url_prefix", out var prefixElement) ||
                prefixElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var tag = tagElement.GetString();
            var assetUrlPrefix = prefixElement.GetString();
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(assetUrlPrefix))
            {
                return null;
            }

            return new LatestReleaseMetadata(tag, assetUrlPrefix);
        }
        catch
        {
            return null;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NextBotInstaller", "1.0"));
        return client;
    }

    private sealed record ArchivePlan(string Url, string FileName);

    private sealed record LatestReleaseMetadata(string Tag, string AssetUrlPrefix);

    private sealed class GithubProxyState
    {
        public bool Enabled { get; set; }
        public string BaseUrl { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

}
