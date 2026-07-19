using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace PngSequenceAvi;

internal enum VideoOutputKind
{
    ProRes4444,
    UncompressedAvi,
    UtVideoRgb,
    UtVideoRgba,
    UtVideoYuv420Bt601,
    UtVideoYuv422Bt601,
    UtVideoYuv420Bt709,
    UtVideoYuv422Bt709
}

internal sealed record VideoOutputChoice(
    string DisplayName,
    string FileSuffix,
    string Extension,
    VideoOutputKind Kind)
{
    public override string ToString() => $"{DisplayName} / 同梱FFmpeg";
}

internal sealed class VideoWriter
{
    // FFmpegの目安は出力1GiBあたり約16バイト。64KiBで数TiB分を確保します。
    private const int AviIndexReserveBytes = 64 * 1024;

    public static IReadOnlyList<VideoOutputChoice> GetOutputChoices()
    {
        return
        [
            new("Apple ProRes 4444 MOV", "prores4444", ".mov", VideoOutputKind.ProRes4444),
            new("無圧縮 AVI (OpenDML)", "uncompressed", ".avi", VideoOutputKind.UncompressedAvi),
            new("UtVideo RGB AVI (OpenDML)", "utvideo_rgb", ".avi", VideoOutputKind.UtVideoRgb),
            new("UtVideo RGBA AVI (OpenDML)", "utvideo_rgba", ".avi", VideoOutputKind.UtVideoRgba),
            new("UtVideo YUV420 BT.601 AVI (OpenDML)", "utvideo_yuv420_bt601", ".avi", VideoOutputKind.UtVideoYuv420Bt601),
            new("UtVideo YUV422 BT.601 AVI (OpenDML)", "utvideo_yuv422_bt601", ".avi", VideoOutputKind.UtVideoYuv422Bt601),
            new("UtVideo YUV420 BT.709 AVI (OpenDML)", "utvideo_yuv420_bt709", ".avi", VideoOutputKind.UtVideoYuv420Bt709),
            new("UtVideo YUV422 BT.709 AVI (OpenDML)", "utvideo_yuv422_bt709", ".avi", VideoOutputKind.UtVideoYuv422Bt709)
        ];
    }

    public async Task<string> WriteAsync(
        IReadOnlyList<string> files,
        string outputPath,
        int fps,
        VideoOutputChoice outputChoice,
        Action<string> log,
        Action<int> progress,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            throw new InvalidOperationException("PNGファイルがありません。");
        }

        var outputDirectory = Path.GetDirectoryName(outputPath) ?? ".";
        Directory.CreateDirectory(outputDirectory);

        log("同梱FFmpegを準備しています。");
        var ffmpegPath = await EmbeddedFfmpeg.GetExecutablePathAsync(cancellationToken);
        var partialPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileNameWithoutExtension(outputPath)}.{Guid.NewGuid():N}.partial{outputChoice.Extension}");

        using var process = new Process
        {
            StartInfo = CreateStartInfo(ffmpegPath, partialPath, fps, files.Count, outputChoice),
            EnableRaisingEvents = true
        };

        var stderr = new StringBuilder();
        var completed = false;

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("FFmpegを起動できませんでした。");
            }

            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // 終了処理中の競合は、後続の終了コード確認に任せます。
                }
            });

            var progressTask = ReadProgressAsync(
                process.StandardOutput,
                files.Count,
                progress,
                cancellationToken);
            var errorTask = ReadErrorsAsync(process.StandardError, stderr, log);

            try
            {
                await WritePngStreamAsync(
                    process.StandardInput.BaseStream,
                    files,
                    cancellationToken);
                process.StandardInput.Close();
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                await process.WaitForExitAsync(CancellationToken.None);
                throw;
            }
            catch
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                await process.WaitForExitAsync(CancellationToken.None);
                throw;
            }
            finally
            {
                await Task.WhenAll(progressTask, errorTask);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (process.ExitCode != 0)
            {
                var details = GetErrorSummary(stderr);
                throw new InvalidOperationException(
                    $"FFmpegによる変換に失敗しました。終了コード: {process.ExitCode}{details}");
            }

            if (!File.Exists(partialPath) || new FileInfo(partialPath).Length == 0)
            {
                throw new InvalidOperationException("FFmpegの出力ファイルが作成されませんでした。");
            }

            File.Move(partialPath, outputPath, overwrite: true);
            completed = true;
            progress(files.Count);
            return outputPath;
        }
        finally
        {
            if (!completed && File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        string ffmpegPath,
        string outputPath,
        int fps,
        int frameCount,
        VideoOutputChoice outputChoice)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        AddArguments(
            startInfo,
            "-hide_banner",
            "-loglevel", "warning",
            "-y",
            "-f", "image2pipe",
            "-framerate", fps.ToString(CultureInfo.InvariantCulture),
            "-vcodec", "png",
            "-i", "pipe:0",
            "-map", "0:v:0",
            "-an",
            "-frames:v", frameCount.ToString(CultureInfo.InvariantCulture),
            "-progress", "pipe:1",
            "-nostats");

        switch (outputChoice.Kind)
        {
            case VideoOutputKind.ProRes4444:
                AddArguments(
                    startInfo,
                    "-codec:v", "prores_ks",
                    "-profile:v", "4",
                    "-pix_fmt", "yuva444p10le",
                    "-alpha_bits", "16",
                    "-vendor", "apl0",
                    "-color_primaries", "bt709",
                    "-color_trc", "bt709",
                    "-colorspace", "bt709",
                    "-f", "mov");
                break;

            case VideoOutputKind.UncompressedAvi:
                AddAviArguments(startInfo, "rawvideo", "bgr24");
                break;

            case VideoOutputKind.UtVideoRgb:
                AddAviArguments(startInfo, "utvideo", "gbrp");
                break;

            case VideoOutputKind.UtVideoRgba:
                AddAviArguments(startInfo, "utvideo", "gbrap");
                break;

            case VideoOutputKind.UtVideoYuv420Bt601:
                AddAviArguments(startInfo, "utvideo", "yuv420p", "smpte170m");
                break;

            case VideoOutputKind.UtVideoYuv422Bt601:
                AddAviArguments(startInfo, "utvideo", "yuv422p", "smpte170m");
                break;

            case VideoOutputKind.UtVideoYuv420Bt709:
                AddAviArguments(startInfo, "utvideo", "yuv420p", "bt709");
                break;

            case VideoOutputKind.UtVideoYuv422Bt709:
                AddAviArguments(startInfo, "utvideo", "yuv422p", "bt709");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(outputChoice));
        }

        startInfo.ArgumentList.Add(outputPath);
        return startInfo;
    }

    private static void AddAviArguments(
        ProcessStartInfo startInfo,
        string codec,
        string pixelFormat,
        string? colorSpace = null)
    {
        AddArguments(
            startInfo,
            "-codec:v", codec,
            "-pix_fmt", pixelFormat);

        if (colorSpace is not null)
        {
            AddArguments(
                startInfo,
                "-color_primaries", colorSpace,
                "-color_trc", colorSpace,
                "-colorspace", colorSpace);
        }

        AddArguments(
            startInfo,
            "-f", "avi",
            "-reserve_index_space", AviIndexReserveBytes.ToString(CultureInfo.InvariantCulture));
    }

    private static void AddArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    private static async Task WritePngStreamAsync(
        Stream destination,
        IReadOnlyList<string> files,
        CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var source = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                useAsync: true);
            await source.CopyToAsync(destination, 1024 * 1024, cancellationToken);
        }

        await destination.FlushAsync(cancellationToken);
    }

    private static async Task ReadProgressAsync(
        StreamReader reader,
        int totalFrames,
        Action<int> progress,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("frame=", StringComparison.Ordinal)
                || !int.TryParse(line.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame))
            {
                continue;
            }

            progress(Math.Clamp(frame, 0, totalFrames));
        }
    }

    private static async Task ReadErrorsAsync(
        StreamReader reader,
        StringBuilder errors,
        Action<string> log)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            errors.AppendLine(line);
            log($"FFmpeg: {line}");
        }
    }

    private static string GetErrorSummary(StringBuilder stderr)
    {
        var lines = stderr
            .ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(8)
            .ToArray();

        return lines.Length == 0
            ? string.Empty
            : $"{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }
}

internal static class EmbeddedFfmpeg
{
    private const string ResourceName = "PngSequenceAvi.ffmpeg.exe";
    private const string BuildId = "ffmpeg-n8.1.2-22-g94138f6973-win64-lgpl";
    private static readonly SemaphoreSlim ExtractionLock = new(1, 1);
    private static string? _resolvedPath;

    public static async Task<string> GetExecutablePathAsync(CancellationToken cancellationToken)
    {
        if (_resolvedPath is not null && File.Exists(_resolvedPath))
        {
            return _resolvedPath;
        }

        await ExtractionLock.WaitAsync(cancellationToken);
        try
        {
            if (_resolvedPath is not null && File.Exists(_resolvedPath))
            {
                return _resolvedPath;
            }

            var assembly = Assembly.GetExecutingAssembly();
            var expectedHash = await GetResourceHashAsync(assembly, cancellationToken);
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PngSequenceAvi",
                "ffmpeg",
                BuildId,
                expectedHash[..16]);
            var executablePath = Path.Combine(directory, "ffmpeg.exe");

            Directory.CreateDirectory(directory);
            if (File.Exists(executablePath)
                && string.Equals(
                    await GetFileHashAsync(executablePath, cancellationToken),
                    expectedHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                _resolvedPath = executablePath;
                return executablePath;
            }

            var temporaryPath = Path.Combine(directory, $"ffmpeg.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var resource = assembly.GetManifestResourceStream(ResourceName)
                    ?? throw new InvalidOperationException("同梱FFmpegリソースが見つかりません。"))
                await using (var output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1024 * 1024,
                    useAsync: true))
                {
                    await resource.CopyToAsync(output, 1024 * 1024, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                }

                var extractedHash = await GetFileHashAsync(temporaryPath, cancellationToken);
                if (!string.Equals(extractedHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("同梱FFmpegの整合性確認に失敗しました。");
                }

                File.Move(temporaryPath, executablePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            _resolvedPath = executablePath;
            return executablePath;
        }
        finally
        {
            ExtractionLock.Release();
        }
    }

    private static async Task<string> GetResourceHashAsync(
        Assembly assembly,
        CancellationToken cancellationToken)
    {
        await using var resource = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("同梱FFmpegリソースが見つかりません。");
        return await GetStreamHashAsync(resource, cancellationToken);
    }

    private static async Task<string> GetFileHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            useAsync: true);
        return await GetStreamHashAsync(stream, cancellationToken);
    }

    private static async Task<string> GetStreamHashAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
