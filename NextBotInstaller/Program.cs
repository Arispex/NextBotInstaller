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
    private const string LatestReleaseMetadataUrl =
        "https://raw.githubusercontent.com/astral-sh/python-build-standalone/latest-release/latest-release.json";

    private static readonly HttpClient Http = CreateHttpClient();

    private static async Task Main()
    {
        AnsiConsole.Write(
            new FigletText("NextBot Installer")
                .Centered()
                .Color(Color.Cyan1));

        while (true)
        {
            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]请选择功能[/]")
                    .AddChoices("1. 一键安装", "2. 创建或修改配置文件", "0. 退出"));

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
                case "2. 创建或修改配置文件":
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
        var workingDirectory = Directory.GetCurrentDirectory();
        var cacheDirectory = Path.Combine(workingDirectory, ".installer-cache");
        var installDirectory = Path.Combine(workingDirectory, "python");

        Directory.CreateDirectory(cacheDirectory);

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

        AnsiConsole.MarkupLine("[blue]步骤 1/5:[/] 解析可用的 Python 压缩包...");
        var archivePlan = await ResolveArchivePlanAsync(PythonVersion);
        var archivePath = Path.Combine(cacheDirectory, archivePlan.FileName);

        AnsiConsole.MarkupLine($"[blue]步骤 2/5:[/] 下载 [grey]{Markup.Escape(archivePlan.FileName)}[/]...");
        await DownloadFileAsync(archivePlan.Url, archivePath);

        AnsiConsole.MarkupLine("[blue]步骤 3/5:[/] 解压 Python 到当前目录...");
        await ExtractArchiveAsync(archivePath, installDirectory);

        var pythonExecutable = FindPythonExecutable(installDirectory);
        AnsiConsole.MarkupLine($"[green]Python 可执行文件：[/]{Markup.Escape(Path.GetRelativePath(workingDirectory, pythonExecutable))}");

        AnsiConsole.MarkupLine("[blue]步骤 4/5:[/] 安装 uv 并执行 uv sync...");
        await RunProcessAsync(pythonExecutable, new[] { "-m", "ensurepip", "--upgrade" }, workingDirectory);
        await RunProcessAsync(pythonExecutable, new[] { "-m", "pip", "install", "--upgrade", "pip", "uv" },
            workingDirectory);
        await RunProcessAsync(pythonExecutable, new[] { "-m", "uv", "sync" }, workingDirectory);

        AnsiConsole.MarkupLine("[blue]步骤 5/5:[/] 生成运行脚本...");
        var scriptPath = CreateRunScript(workingDirectory, pythonExecutable);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold green]安装完成。[/]");
        AnsiConsole.MarkupLine($"运行脚本：[cyan]{Markup.Escape(Path.GetRelativePath(workingDirectory, scriptPath))}[/]");
    }

    private static async Task<ArchivePlan> ResolveArchivePlanAsync(string pythonVersion)
    {
        var customArchiveUrl = Environment.GetEnvironmentVariable("NEXTBOT_PYTHON_ARCHIVE_URL");
        if (!string.IsNullOrWhiteSpace(customArchiveUrl))
        {
            var customFileName = Path.GetFileName(new Uri(customArchiveUrl).AbsolutePath);
            return new ArchivePlan(customArchiveUrl, customFileName);
        }

        var releaseJson = await Http.GetStringAsync(LatestReleaseMetadataUrl);
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
            "未找到当前系统可用的 Python 3.14.3 压缩包。你可以设置环境变量 NEXTBOT_PYTHON_ARCHIVE_URL 指定下载地址。"
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
        try
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
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
            using var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
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
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
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

            File.WriteAllText(scriptPath, script, Encoding.UTF8);
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

        File.WriteAllText(unixScriptPath, unixScript, Encoding.UTF8);
        File.SetUnixFileMode(unixScriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        return unixScriptPath;
    }

    private static void RunConfigFileWizard()
    {
        var workingDirectory = Directory.GetCurrentDirectory();
        var envPath = Path.Combine(workingDirectory, ".env");

        if (!File.Exists(envPath))
        {
            AnsiConsole.MarkupLine("[blue]未检测到 .env，准备创建新配置文件。[/]");
            var inputs = PromptConfigInputs(null, null, null);
            WriteFullEnvFile(envPath, inputs);
            AnsiConsole.MarkupLine("[green].env 创建完成。[/]");
            return;
        }

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold].env 已存在，请选择操作[/]")
                .AddChoices("修改现有配置", "覆盖重建 .env", "取消"));

        switch (action)
        {
            case "修改现有配置":
            {
                var currentValues = ParseEnvValues(envPath);
                currentValues.TryGetValue("OWNER_ID", out var existingOwnerIds);
                currentValues.TryGetValue("GROUP_ID", out var existingGroupIds);
                currentValues.TryGetValue("RENDER_SERVER_PUBLIC_BASE_URL", out var existingPublicBaseUrl);

                var ownerDefaults = ParseEnvArray(existingOwnerIds);
                var groupDefaults = ParseEnvArray(existingGroupIds);
                var ipDefault = ParsePublicIpFromUrl(existingPublicBaseUrl);

                var inputs = PromptConfigInputs(ownerDefaults, groupDefaults, ipDefault);
                UpdateEnvValues(envPath, new Dictionary<string, string>
                {
                    ["OWNER_ID"] = ToEnvArrayLiteral(inputs.OwnerIds),
                    ["GROUP_ID"] = ToEnvArrayLiteral(inputs.GroupIds),
                    ["RENDER_SERVER_PUBLIC_BASE_URL"] = $"http://{inputs.PublicIp}:18081"
                });

                AnsiConsole.MarkupLine("[green].env 配置已更新。[/]");
                break;
            }
            case "覆盖重建 .env":
            {
                var inputs = PromptConfigInputs(null, null, null);
                WriteFullEnvFile(envPath, inputs);
                AnsiConsole.MarkupLine("[green].env 已按模板重建。[/]");
                break;
            }
            default:
                AnsiConsole.MarkupLine("[yellow]已取消。[/]");
                break;
        }
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
            new TextPrompt<string>("请输入 OWNER_ID（多个用英文逗号分隔）")
                .AllowEmpty()
                .DefaultValue(ownerDefaultText)
                .ShowDefaultValue(true));

        var groupRaw = AnsiConsole.Prompt(
            new TextPrompt<string>("请输入 GROUP_ID（多个用英文逗号分隔）")
                .AllowEmpty()
                .DefaultValue(groupDefaultText)
                .ShowDefaultValue(true));

        var publicIpRaw = AnsiConsole.Prompt(
            new TextPrompt<string>("请输入公网 IP（用于 RENDER_SERVER_PUBLIC_BASE_URL）")
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
            "ONEBOT_ACCESS_TOKEN=S~VPgQf9t0bhvf_u",
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

    private sealed record ConfigInputs(
        IReadOnlyList<string> OwnerIds,
        IReadOnlyList<string> GroupIds,
        string PublicIp);
}
