using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;

internal static class Program
{
    private const string PythonVersion = "3.14.3";
    private const string DefaultGithubProxyBaseUrl = "https://ghfast.top/";
    private const string NextBotSourceZipUrl = "https://github.com/Arispex/next-bot/archive/refs/heads/main.zip";
    private const string NextBotExtractedFolderName = "next-bot-main";
    private const string LatestReleaseMetadataUrl =
        "https://raw.githubusercontent.com/astral-sh/python-build-standalone/latest-release/latest-release.json";

    private static readonly GithubProxySettings GithubProxy = ResolveGithubProxySettings();
    private static readonly HttpClient Http = CreateHttpClient();

    private static async Task Main()
    {
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
                    .AddChoices("1. 一键安装", "2. 配置管理", "0. 退出"));

            switch (selected)
            {
                case "1. 一键安装":
                    try
                    {
                        await RunOneClickInstallAsync();
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]安装失败：[/]{Markup.Escape(ex.Message)}");
                    }

                    break;
                case "2. 配置管理":
                    try
                    {
                        RunConfigFileWizard();
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]配置处理失败：[/]{Markup.Escape(ex.Message)}");
                    }

                    break;
                case "0. 退出":
                    return;
            }

            AnsiConsole.WriteLine();
        }
    }

    private static async Task RunOneClickInstallAsync()
    {
        ShowSectionTitle("一键安装", "自动下载 NextBot、安装环境并生成启动脚本");
        AnsiConsole.MarkupLine($"[grey]GitHub 代理：[/][white]{Markup.Escape(GetGithubProxyStatusText())}[/]");
        AnsiConsole.WriteLine();

        var workingDirectory = Directory.GetCurrentDirectory();
        var cacheDirectory = Path.Combine(workingDirectory, ".installer-cache");
        var installDirectory = Path.Combine(workingDirectory, "python");

        Directory.CreateDirectory(cacheDirectory);

        var sourceZipPath = Path.Combine(cacheDirectory, "next-bot-main.zip");
        await RunWithStatusAsync(
            "步骤 1/7 下载 NextBot...",
            () => DownloadFileAsync(NextBotSourceZipUrl, sourceZipPath));

        await RunWithStatusAsync(
            "步骤 2/7 解压并同步到当前目录...",
            () =>
            {
                DeployProjectSource(sourceZipPath, workingDirectory, cacheDirectory);
                return Task.CompletedTask;
            });

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
            "步骤 3/7 解析可用的 Python 压缩包...",
            () => ResolveArchivePlanAsync(PythonVersion));
        var archivePath = Path.Combine(cacheDirectory, archivePlan.FileName);

        await RunWithStatusAsync(
            $"步骤 4/7 下载 {archivePlan.FileName}...",
            () => DownloadFileAsync(archivePlan.Url, archivePath));

        await RunWithStatusAsync(
            "步骤 5/7 解压 Python 到当前目录...",
            () => ExtractArchiveAsync(archivePath, installDirectory));

        var pythonExecutable = FindPythonExecutable(installDirectory);
        AnsiConsole.MarkupLine(
            $"[grey]Python 可执行文件：[/][white]{Markup.Escape(Path.GetRelativePath(workingDirectory, pythonExecutable))}[/]");

        await RunWithStatusAsync(
            "步骤 6/7 安装 uv 并执行 uv sync...",
            async () =>
            {
                await RunProcessAsync(pythonExecutable, new[] { "-m", "ensurepip", "--upgrade" }, workingDirectory);
                await RunProcessAsync(pythonExecutable, new[] { "-m", "pip", "install", "--upgrade", "pip", "uv" },
                    workingDirectory);
                await RunProcessAsync(pythonExecutable, new[] { "-m", "uv", "sync" }, workingDirectory);
            });

        var scriptPath = await RunWithStatusAsync(
            "步骤 7/7 生成运行脚本...",
            () => Task.FromResult(CreateRunScript(workingDirectory, pythonExecutable)));

        AnsiConsole.WriteLine();
        var result = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey37)
            .AddColumn("[bold #7dd3fc]项目[/]")
            .AddColumn("[bold #7dd3fc]值[/]");
        result.AddRow("安装目录", Markup.Escape(installDirectory));
        result.AddRow("Python 版本", PythonVersion);
        result.AddRow("运行脚本", Markup.Escape(Path.GetRelativePath(workingDirectory, scriptPath)));

        AnsiConsole.Write(
            new Panel(result)
                .Header("[bold #4ade80]安装完成[/]")
                .Border(BoxBorder.Rounded)
                .BorderStyle(new Style(foreground: Color.Green)));
    }

    private static void DeployProjectSource(string sourceZipPath, string workingDirectory, string cacheDirectory)
    {
        var tempExtractRoot = Path.Combine(cacheDirectory, $"src-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempExtractRoot);

        try
        {
            ZipFile.ExtractToDirectory(sourceZipPath, tempExtractRoot, true);
            var extractedRoot = Path.Combine(tempExtractRoot, NextBotExtractedFolderName);

            if (!Directory.Exists(extractedRoot))
            {
                throw new DirectoryNotFoundException($"未找到解压后的源码目录：{NextBotExtractedFolderName}");
            }

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

        var infoTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey37)
            .AddColumn("[bold #7dd3fc]环境[/]")
            .AddColumn("[bold #7dd3fc]值[/]");
        infoTable.AddRow("系统", Markup.Escape(GetCurrentOsName()));
        infoTable.AddRow("架构", RuntimeInformation.ProcessArchitecture.ToString());
        infoTable.AddRow("GitHub 代理", Markup.Escape(GetGithubProxyStatusText()));
        infoTable.AddRow("运行目录", Markup.Escape(Directory.GetCurrentDirectory()));

        AnsiConsole.Write(
            new Panel(infoTable)
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

    private static bool IsGithubHost(string host)
    {
        var normalized = host.ToLowerInvariant();
        return normalized == "github.com" ||
               normalized.EndsWith(".github.com", StringComparison.Ordinal) ||
               normalized == "githubusercontent.com" ||
               normalized.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
    }

    private static GithubProxySettings ResolveGithubProxySettings()
    {
        var candidates = new[]
        {
            ("GITHUB_PROXY", Environment.GetEnvironmentVariable("GITHUB_PROXY"))
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Item2))
            {
                continue;
            }

            var configuredValue = candidate.Item2.Trim();
            if (IsProxyDisabledValue(configuredValue))
            {
                return new GithubProxySettings(false, string.Empty, $"环境变量 {candidate.Item1}");
            }

            if (TryNormalizeProxyBaseUrl(configuredValue, out var normalizedProxy))
            {
                return new GithubProxySettings(true, normalizedProxy, $"环境变量 {candidate.Item1}");
            }
        }

        return new GithubProxySettings(true, DefaultGithubProxyBaseUrl, "默认");
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

    private static bool IsProxyDisabledValue(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "0" or "off" or "false" or "disable" or "disabled" or "none";
    }

    private static string GetGithubProxyStatusText()
    {
        if (!GithubProxy.Enabled)
        {
            return $"已禁用（{GithubProxy.Source}）";
        }

        return $"{GithubProxy.BaseUrl}（{GithubProxy.Source}）";
    }

    private static async Task<ArchivePlan> ResolveArchivePlanAsync(string pythonVersion)
    {
        var releaseJson = await Http.GetStringAsync(ToProxiedGithubUrl(LatestReleaseMetadataUrl));
        var metadata = JsonSerializer.Deserialize<LatestReleaseMetadata>(releaseJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

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
        var requestUrl = ToProxiedGithubUrl(url);

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

    private static void RunConfigFileWizard()
    {
        ShowSectionTitle("配置管理", "创建、查看并按菜单逐项编辑 .env 配置");

        var workingDirectory = Directory.GetCurrentDirectory();
        var envPath = Path.Combine(workingDirectory, ".env");

        if (!File.Exists(envPath))
        {
            AnsiConsole.MarkupLine("[blue]未检测到 .env，已自动创建默认配置。[/]");
            WriteFullEnvFile(
                envPath,
                new ConfigInputs(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    "127.0.0.1"));
            RunDetailedConfigEditor(envPath);
            return;
        }

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold #7dd3fc].env 已存在，请选择操作[/]")
                .HighlightStyle(new Style(foreground: Color.Black, background: Color.Aquamarine1, decoration: Decoration.Bold))
                .AddChoices("修改现有配置", "覆盖重建 .env", "取消"));

        switch (action)
        {
            case "修改现有配置":
            {
                RunDetailedConfigEditor(envPath);
                break;
            }
            case "覆盖重建 .env":
            {
                WriteFullEnvFile(
                    envPath,
                    new ConfigInputs(
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        "127.0.0.1"));
                AnsiConsole.MarkupLine("[green].env 已按模板重建。[/]");
                break;
            }
            default:
                AnsiConsole.MarkupLine("[yellow]已取消。[/]");
                break;
        }
    }

    private static void RunDetailedConfigEditor(string envPath)
    {
        var config = LoadEditableConfig(envPath);

        while (true)
        {
            ShowCurrentConfig(config);
            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold #7dd3fc]请选择要修改的配置项[/]")
                    .HighlightStyle(new Style(foreground: Color.Black, background: Color.Aquamarine1, decoration: Decoration.Bold))
                    .AddChoices(
                        "DRIVER",
                        "LOCALSTORE_USE_CWD",
                        "COMMAND_START",
                        "ONEBOT_WS_URLS",
                        "ONEBOT_ACCESS_TOKEN",
                        "OWNER_ID",
                        "GROUP_ID",
                        "RENDER_SERVER_HOST",
                        "RENDER_SERVER_PORT",
                        "RENDER_SERVER_PUBLIC_BASE_URL",
                        "保存并返回",
                        "放弃并返回"));

            switch (selected)
            {
                case "DRIVER":
                {
                    var driver = AnsiConsole.Prompt(
                        new TextPrompt<string>("请输入 DRIVER")
                            .Validate(value => !string.IsNullOrWhiteSpace(value)
                                ? ValidationResult.Success()
                                : ValidationResult.Error("[red]DRIVER 不能为空[/]"))
                            .DefaultValue(config.Driver)
                            .ShowDefaultValue(true));
                    config.Driver = driver.Trim();
                    break;
                }
                case "LOCALSTORE_USE_CWD":
                {
                    var useCwd = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("请选择 LOCALSTORE_USE_CWD")
                            .AddChoices("true", "false")
                            .HighlightStyle(new Style(foreground: Color.Black, background: Color.Aquamarine1, decoration: Decoration.Bold))
                            .UseConverter(v => v == (config.LocalstoreUseCwd ? "true" : "false")
                                ? $"{v} (当前)"
                                : v));
                    config.LocalstoreUseCwd = string.Equals(useCwd, "true", StringComparison.OrdinalIgnoreCase);
                    break;
                }
                case "COMMAND_START":
                {
                    var commandRaw = AnsiConsole.Prompt(
                        new TextPrompt<string>("请输入 COMMAND_START（多个用英文逗号分隔）")
                            .AllowEmpty()
                            .DefaultValue(ToCommaSeparated(config.CommandStart))
                            .ShowDefaultValue(true));
                    config.CommandStart = ParseCommaSeparatedValuesAllowEmpty(commandRaw).ToList();
                    break;
                }
                case "ONEBOT_WS_URLS":
                {
                    var wsRaw = AnsiConsole.Prompt(
                        new TextPrompt<string>("请输入 ONEBOT_WS_URLS（多个用英文逗号分隔）")
                            .AllowEmpty()
                            .DefaultValue(ToCommaSeparated(config.OnebotWsUrls))
                            .ShowDefaultValue(true));
                    config.OnebotWsUrls = ParseCommaSeparatedValues(wsRaw).ToList();
                    break;
                }
                case "ONEBOT_ACCESS_TOKEN":
                {
                    var token = AnsiConsole.Prompt(
                        new TextPrompt<string>("请输入 ONEBOT_ACCESS_TOKEN")
                            .AllowEmpty()
                            .DefaultValue(config.OnebotAccessToken)
                            .ShowDefaultValue(true));
                    config.OnebotAccessToken = token.Trim();
                    break;
                }
                case "OWNER_ID":
                {
                    var ownerRaw = AnsiConsole.Prompt(
                        new TextPrompt<string>("请输入 OWNER_ID（多个用英文逗号分隔）")
                            .AllowEmpty()
                            .DefaultValue(ToCommaSeparated(config.OwnerIds))
                            .ShowDefaultValue(true));
                    config.OwnerIds = ParseCommaSeparatedValues(ownerRaw).ToList();
                    break;
                }
                case "GROUP_ID":
                {
                    var groupRaw = AnsiConsole.Prompt(
                        new TextPrompt<string>("请输入 GROUP_ID（多个用英文逗号分隔）")
                            .AllowEmpty()
                            .DefaultValue(ToCommaSeparated(config.GroupIds))
                            .ShowDefaultValue(true));
                    config.GroupIds = ParseCommaSeparatedValues(groupRaw).ToList();
                    break;
                }
                case "RENDER_SERVER_HOST":
                {
                    var host = AnsiConsole.Prompt(
                        new TextPrompt<string>("请输入 RENDER_SERVER_HOST")
                            .Validate(value => !string.IsNullOrWhiteSpace(value)
                                ? ValidationResult.Success()
                                : ValidationResult.Error("[red]RENDER_SERVER_HOST 不能为空[/]"))
                            .DefaultValue(config.RenderServerHost)
                            .ShowDefaultValue(true));
                    config.RenderServerHost = host.Trim();
                    break;
                }
                case "RENDER_SERVER_PORT":
                {
                    var port = AnsiConsole.Prompt(
                        new TextPrompt<int>("请输入 RENDER_SERVER_PORT")
                            .Validate(value => value is > 0 and <= 65535
                                ? ValidationResult.Success()
                                : ValidationResult.Error("[red]端口范围必须是 1-65535[/]"))
                            .DefaultValue(config.RenderServerPort)
                            .ShowDefaultValue(true));
                    config.RenderServerPort = port;
                    break;
                }
                case "RENDER_SERVER_PUBLIC_BASE_URL":
                {
                    var urlRaw = AnsiConsole.Prompt(
                        new TextPrompt<string>("请输入 RENDER_SERVER_PUBLIC_BASE_URL（可直接输入 IP）")
                            .Validate(value => !string.IsNullOrWhiteSpace(value)
                                ? ValidationResult.Success()
                                : ValidationResult.Error("[red]RENDER_SERVER_PUBLIC_BASE_URL 不能为空[/]"))
                            .DefaultValue(config.RenderServerPublicBaseUrl)
                            .ShowDefaultValue(true));
                    config.RenderServerPublicBaseUrl = NormalizePublicBaseUrl(urlRaw, config.RenderServerPort);
                    break;
                }
                case "保存并返回":
                {
                    UpdateEnvValues(envPath, new Dictionary<string, string>
                    {
                        ["DRIVER"] = config.Driver,
                        ["LOCALSTORE_USE_CWD"] = config.LocalstoreUseCwd ? "true" : "false",
                        ["COMMAND_START"] = ToEnvArrayLiteral(config.CommandStart),
                        ["ONEBOT_WS_URLS"] = ToEnvArrayLiteral(config.OnebotWsUrls),
                        ["ONEBOT_ACCESS_TOKEN"] = config.OnebotAccessToken,
                        ["OWNER_ID"] = ToEnvArrayLiteral(config.OwnerIds),
                        ["GROUP_ID"] = ToEnvArrayLiteral(config.GroupIds),
                        ["RENDER_SERVER_HOST"] = config.RenderServerHost,
                        ["RENDER_SERVER_PORT"] = config.RenderServerPort.ToString(),
                        ["RENDER_SERVER_PUBLIC_BASE_URL"] = config.RenderServerPublicBaseUrl
                    });

                    AnsiConsole.MarkupLine("[green].env 配置已更新。[/]");
                    return;
                }
                case "放弃并返回":
                    AnsiConsole.MarkupLine("[yellow]已放弃当前修改。[/]");
                    return;
            }

            AnsiConsole.WriteLine();
        }
    }

    private static void ShowCurrentConfig(EditableConfig config)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey37)
            .AddColumn("[bold #7dd3fc]配置项[/]")
            .AddColumn("[bold #7dd3fc]当前值[/]");

        table.AddRow("[#93c5fd]DRIVER[/]", Markup.Escape(config.Driver));
        table.AddRow("[#93c5fd]LOCALSTORE_USE_CWD[/]", config.LocalstoreUseCwd ? "true" : "false");
        table.AddRow("[#93c5fd]COMMAND_START[/]", Markup.Escape(ToEnvArrayLiteral(config.CommandStart)));
        table.AddRow("[#93c5fd]ONEBOT_WS_URLS[/]", Markup.Escape(ToEnvArrayLiteral(config.OnebotWsUrls)));
        table.AddRow("[#93c5fd]ONEBOT_ACCESS_TOKEN[/]", Markup.Escape(config.OnebotAccessToken));
        table.AddRow("[#93c5fd]OWNER_ID[/]", Markup.Escape(ToEnvArrayLiteral(config.OwnerIds)));
        table.AddRow("[#93c5fd]GROUP_ID[/]", Markup.Escape(ToEnvArrayLiteral(config.GroupIds)));
        table.AddRow("[#93c5fd]RENDER_SERVER_HOST[/]", Markup.Escape(config.RenderServerHost));
        table.AddRow("[#93c5fd]RENDER_SERVER_PORT[/]", config.RenderServerPort.ToString());
        table.AddRow("[#93c5fd]RENDER_SERVER_PUBLIC_BASE_URL[/]", Markup.Escape(config.RenderServerPublicBaseUrl));

        AnsiConsole.Write(
            new Panel(table)
                .Header("[bold #93c5fd]当前配置[/]")
                .Border(BoxBorder.Rounded)
                .BorderStyle(new Style(foreground: Color.Grey37))
                .Expand());
    }

    private static EditableConfig LoadEditableConfig(string envPath)
    {
        var values = ParseEnvValues(envPath);

        var driver = GetEnvValue(values, "DRIVER");
        if (string.IsNullOrWhiteSpace(driver))
        {
            driver = "~websockets";
        }

        var localstoreUseCwd = ParseBoolOrDefault(GetEnvValue(values, "LOCALSTORE_USE_CWD"), true);
        var commandStart = ParseEnvArrayAllowEmpty(GetEnvValue(values, "COMMAND_START"));
        var onebotWsUrls = ParseEnvArray(GetEnvValue(values, "ONEBOT_WS_URLS"));
        var ownerIds = ParseEnvArray(GetEnvValue(values, "OWNER_ID"));
        var groupIds = ParseEnvArray(GetEnvValue(values, "GROUP_ID"));

        var host = GetEnvValue(values, "RENDER_SERVER_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "0.0.0.0";
        }

        var port = ParsePortOrDefault(GetEnvValue(values, "RENDER_SERVER_PORT"), 18081);

        var publicBaseUrl = GetEnvValue(values, "RENDER_SERVER_PUBLIC_BASE_URL");
        if (string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            publicBaseUrl = $"http://127.0.0.1:{port}";
        }

        return new EditableConfig(
            driver,
            localstoreUseCwd,
            commandStart.Count > 0 ? commandStart.ToList() : new List<string> { "/", string.Empty },
            onebotWsUrls.Count > 0 ? onebotWsUrls.ToList() : new List<string> { "ws://127.0.0.1:3001" },
            GetEnvValue(values, "ONEBOT_ACCESS_TOKEN") ?? "S~VPgQf9t0bhvf_u",
            ownerIds.ToList(),
            groupIds.ToList(),
            host,
            port,
            publicBaseUrl);
    }

    private static string? GetEnvValue(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value : null;
    }

    private static int ParsePortOrDefault(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed is > 0 and <= 65535
            ? parsed
            : fallback;
    }

    private static string NormalizePublicBaseUrl(string rawInput, int defaultPort)
    {
        var trimmed = rawInput.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString().TrimEnd('/');
        }

        if (Uri.TryCreate($"http://{trimmed}", UriKind.Absolute, out var withSchemeUri))
        {
            var host = withSchemeUri.Host;
            var port = withSchemeUri.IsDefaultPort ? defaultPort : withSchemeUri.Port;
            return $"http://{host}:{port}";
        }

        return trimmed;
    }

    private static ConfigInputs PromptConfigInputs(
        IReadOnlyList<string>? ownerDefaults,
        IReadOnlyList<string>? groupDefaults,
        string? publicIpDefault)
    {
        var ownerDefaultText = ToCommaSeparated(ownerDefaults);
        var groupDefaultText = ToCommaSeparated(groupDefaults);
        var ipDefaultText = string.IsNullOrWhiteSpace(publicIpDefault) ? "127.0.0.1" : publicIpDefault;

        var ownerRaw = AnsiConsole.Prompt(
            new TextPrompt<string>("请输入机器人所有者的 QQ 号（多个用英文逗号分隔）")
                .AllowEmpty()
                .DefaultValue(ownerDefaultText)
                .ShowDefaultValue(true));

        var groupRaw = AnsiConsole.Prompt(
            new TextPrompt<string>("请输入监听的 QQ 群号（多个用英文逗号分隔）")
                .AllowEmpty()
                .DefaultValue(groupDefaultText)
                .ShowDefaultValue(true));

        var publicIpRaw = AnsiConsole.Prompt(
            new TextPrompt<string>("请输入公网 IP")
                .Validate(ip => !string.IsNullOrWhiteSpace(ip)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]公网 IP 不能为空[/]"))
                .DefaultValue(ipDefaultText)
                .ShowDefaultValue(true));

        return new ConfigInputs(
            ParseCommaSeparatedValues(ownerRaw),
            ParseCommaSeparatedValues(groupRaw),
            NormalizePublicIp(publicIpRaw));
    }

    private static void WriteFullEnvFile(string envPath, ConfigInputs inputs)
    {
        var content = BuildEnvTemplate(inputs);
        File.WriteAllText(envPath, content, new UTF8Encoding(false));
    }

    private static string BuildEnvTemplate(ConfigInputs inputs)
    {
        var ownerLiteral = ToEnvArrayLiteral(inputs.OwnerIds);
        var groupLiteral = ToEnvArrayLiteral(inputs.GroupIds);
        var publicBaseUrl = $"http://{inputs.PublicIp}:18081";

        var lines = new[]
        {
            "DRIVER=~websockets",
            "LOCALSTORE_USE_CWD=true",
            string.Empty,
            "COMMAND_START=[\"/\", \"\"]",
            string.Empty,
            "ONEBOT_WS_URLS=[\"ws://127.0.0.1:3001\"]",
            "ONEBOT_ACCESS_TOKEN=",
            string.Empty,
            $"OWNER_ID={ownerLiteral}",
            $"GROUP_ID={groupLiteral}",
            string.Empty,
            "RENDER_SERVER_HOST=0.0.0.0",
            "RENDER_SERVER_PORT=18081",
            $"RENDER_SERVER_PUBLIC_BASE_URL={publicBaseUrl}"
        };

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static Dictionary<string, string> ParseEnvValues(string envPath)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var index = line.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }

            var key = line[..index].Trim();
            var value = line[(index + 1)..].Trim();
            if (key.Length > 0)
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static void UpdateEnvValues(string envPath, IReadOnlyDictionary<string, string> updates)
    {
        var lines = File.ReadAllLines(envPath).ToList();
        var touched = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var index = line.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }

            var key = line[..index].Trim();
            if (!updates.TryGetValue(key, out var newValue))
            {
                continue;
            }

            lines[i] = $"{key}={newValue}";
            touched.Add(key);
        }

        foreach (var pair in updates)
        {
            if (touched.Contains(pair.Key))
            {
                continue;
            }

            lines.Add($"{pair.Key}={pair.Value}");
        }

        var output = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        File.WriteAllText(envPath, output, new UTF8Encoding(false));
    }

    private static string ToEnvArrayLiteral(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "[\"\"]";
        }

        var escaped = values
            .Select(v => v.Replace("\\", "\\\\").Replace("\"", "\\\""))
            .Select(v => $"\"{v}\"");

        return $"[{string.Join(",", escaped)}]";
    }

    private static IReadOnlyList<string> ParseCommaSeparatedValues(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split(',', StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ParseCommaSeparatedValuesAllowEmpty(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new[] { string.Empty };
        }

        return raw
            .Split(',', StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private static IReadOnlyList<string> ParseEnvArray(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(value);
            if (parsed is null)
            {
                return Array.Empty<string>();
            }

            return parsed
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> ParseEnvArrayAllowEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(value) ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool ParseBoolOrDefault(string? value, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return bool.TryParse(value.Trim(), out var parsed) ? parsed : fallback;
    }

    private static string ParsePublicIpFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host;
        }

        return string.Empty;
    }

    private static string NormalizePublicIp(string rawInput)
    {
        var trimmed = rawInput.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri) &&
            !string.IsNullOrWhiteSpace(absoluteUri.Host))
        {
            return absoluteUri.Host;
        }

        if (Uri.TryCreate($"http://{trimmed}", UriKind.Absolute, out var withSchemeUri) &&
            !string.IsNullOrWhiteSpace(withSchemeUri.Host))
        {
            return withSchemeUri.Host;
        }

        return trimmed;
    }

    private static string ToCommaSeparated(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(",", values);
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

    private sealed record LatestReleaseMetadata(
        [property: JsonPropertyName("tag")] string Tag,
        [property: JsonPropertyName("asset_url_prefix")]
        string AssetUrlPrefix);

    private sealed record GithubProxySettings(bool Enabled, string BaseUrl, string Source);

    private sealed class EditableConfig
    {
        public EditableConfig(
            string driver,
            bool localstoreUseCwd,
            List<string> commandStart,
            List<string> onebotWsUrls,
            string onebotAccessToken,
            List<string> ownerIds,
            List<string> groupIds,
            string renderServerHost,
            int renderServerPort,
            string renderServerPublicBaseUrl)
        {
            Driver = driver;
            LocalstoreUseCwd = localstoreUseCwd;
            CommandStart = commandStart;
            OnebotWsUrls = onebotWsUrls;
            OnebotAccessToken = onebotAccessToken;
            OwnerIds = ownerIds;
            GroupIds = groupIds;
            RenderServerHost = renderServerHost;
            RenderServerPort = renderServerPort;
            RenderServerPublicBaseUrl = renderServerPublicBaseUrl;
        }

        public string Driver { get; set; }
        public bool LocalstoreUseCwd { get; set; }
        public List<string> CommandStart { get; set; }
        public List<string> OnebotWsUrls { get; set; }
        public string OnebotAccessToken { get; set; }
        public List<string> OwnerIds { get; set; }
        public List<string> GroupIds { get; set; }
        public string RenderServerHost { get; set; }
        public int RenderServerPort { get; set; }
        public string RenderServerPublicBaseUrl { get; set; }
    }

    private sealed record ConfigInputs(
        IReadOnlyList<string> OwnerIds,
        IReadOnlyList<string> GroupIds,
        string PublicIp);
}
