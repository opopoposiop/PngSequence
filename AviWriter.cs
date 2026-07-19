using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PngSequenceAvi;

/*
 * PNG画像をWindows標準のAVI APIへ渡し、AVIファイルを書き出す中核処理です。
 *
 * ユーザーが変更してよい箇所:
 * - knownFallbacks内の既知コーデック候補
 * - ログとエラーメッセージの文章
 * - 出力ファイル名に使うFileSuffixの整形規則
 *
 * 変更不可の箇所:
 * - AVIFileInit/AVIFileExitと各Release/FreeHGlobalの対応
 * - 24bit/32bitの行サイズ計算と4バイト境界への整列
 * - CopyBottomUpの上下反転
 * - DllImport構造体のフィールド順、型、文字数
 *
 * Codex用覚書:
 * - HOW: PNGをDIB形式のバイト列へ変換し、Video for Windows APIへ1フレームずつ渡します。
 * - WHY NOT: P/Invoke宣言や構造体を見た目だけで整理・改名・並べ替えません。
 *   WindowsネイティブAPIとメモリ配置が一致しなくなり、破損やクラッシュにつながるためです。
 */
internal sealed record AviCodecChoice(
    string DisplayName,
    int FourCc,
    bool IsUncompressed,
    bool UseAlpha,
    bool IsAvailable)
{
    public string FileSuffix
    {
        get
        {
            if (IsUncompressed)
            {
                return "uncompressed";
            }

            var fourCc = AviWriter.FourCcToString(FourCc).ToLowerInvariant();
            var name = DisplayName
                .Replace(" ", "_", StringComparison.Ordinal)
                .Replace("/", "_", StringComparison.Ordinal)
                .Replace("\\", "_", StringComparison.Ordinal)
                .Replace("(", "", StringComparison.Ordinal)
                .Replace(")", "", StringComparison.Ordinal)
                .ToLowerInvariant();

            return $"{fourCc}_{name}";
        }
    }

    public override string ToString()
    {
        if (IsUncompressed)
        {
            return $"{DisplayName} / 使用可能";
        }

        var state = IsAvailable ? "使用可能" : "使用不可";
        return $"{DisplayName} [{AviWriter.FourCcToString(FourCc)}] / {state}";
    }
}

internal sealed class AviWriter
{
    private const int OfWrite = 0x00000001;
    private const int OfCreate = 0x00001000;
    private const int AviIfKeyFrame = 0x00000010;
    private const int BiRgb = 0;
    private const int StreamTypeVideo = 0x73646976;
    private const int IcTypeVideo = 0x63646976;
    private const int IcModeCompress = 1;

    public static IReadOnlyList<AviCodecChoice> GetCodecChoices()
    {
        // HOW: 無圧縮を必ず先頭に置き、インストール済みUT Videoと既知FourCCを後から追加します。
        var choices = new List<AviCodecChoice>
        {
            new("無圧縮 AVI", 0, IsUncompressed: true, UseAlpha: false, IsAvailable: true)
        };

        var installed = EnumerateVideoCodecs()
            .Where(codec => codec.Description.Contains("UtVideo", StringComparison.OrdinalIgnoreCase)
                || codec.Description.Contains("Ut Video", StringComparison.OrdinalIgnoreCase)
                || codec.Name.Contains("UtVideo", StringComparison.OrdinalIgnoreCase)
                || codec.Name.Contains("Ut Video", StringComparison.OrdinalIgnoreCase))
            .GroupBy(codec => new
            {
                codec.FourCc,
                Label = string.IsNullOrWhiteSpace(codec.Description) ? codec.Name : codec.Description
            })
            .Select(group => group.First())
            .OrderBy(codec => codec.Description)
            .ToArray();

        foreach (var codec in installed)
        {
            var label = string.IsNullOrWhiteSpace(codec.Description) ? codec.Name : codec.Description;
            choices.Add(CreateCodecChoice(label, codec.FourCc));
        }

        // WHY NOT: インストール済み一覧だけには限定しません。
        // Windows側の表示名が異なる環境でも、既知FourCCを実際に開けるか確認できるようにします。
        var knownFallbacks = new[]
        {
            ("UtVideo RGB", "ULRG", false),
            ("UtVideo RGBA", "ULRA", true),
            ("UtVideo T2 RGBA", "ULRA", true),
            ("UtVideo YUV420 BT.601", "ULY0", false),
            ("UtVideo YUV422 BT.601", "ULY2", false),
            ("UtVideo YUV420 BT.709", "ULH0", false),
            ("UtVideo YUV422 BT.709", "ULH2", false)
        };

        foreach (var fallback in knownFallbacks)
        {
            var fourCc = FourCc(fallback.Item2);
            if (choices.Any(choice => !choice.IsUncompressed
                && choice.FourCc == fourCc
                && string.Equals(choice.DisplayName, fallback.Item1, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            choices.Add(CreateCodecChoice(fallback.Item1, fourCc, fallback.Item3));
        }

        return choices;
    }

    public void Write(
        IReadOnlyList<string> files,
        string outputPath,
        int fps,
        AviCodecChoice codec,
        Action<string> log,
        Action<int> progress,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            throw new InvalidOperationException("PNGファイルがありません。");
        }

        using var firstImage = Image.FromFile(files[0]);
        var width = firstImage.Width;
        var height = firstImage.Height;
        var bytesPerPixel = codec.UseAlpha ? 4 : 3;
        var bitCount = (ushort)(codec.UseAlpha ? 32 : 24);
        // HOW: WindowsのDIB仕様に合わせ、1行のバイト数を4の倍数へ切り上げます。
        var rowSize = ((width * bytesPerPixel + 3) / 4) * 4;
        var imageSize = rowSize * height;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        IntPtr aviFile = IntPtr.Zero;
        IntPtr rawStream = IntPtr.Zero;
        IntPtr outputStream = IntPtr.Zero;
        IntPtr formatPtr = IntPtr.Zero;

        // WHY NOT: 初期化と解放を呼び出し元へ分散しません。
        // 途中で例外や停止が発生しても、このメソッド内のfinallyで必ず後片付けするためです。
        AVIFileInit();

        try
        {
            ThrowIfFailed(AVIFileOpenW(out aviFile, outputPath, OfWrite | OfCreate, IntPtr.Zero), "AVIファイルを開けませんでした。");

            var streamInfo = new AviStreamInfo
            {
                fccType = StreamTypeVideo,
                fccHandler = 0,
                dwScale = 1,
                dwRate = fps,
                dwSuggestedBufferSize = imageSize,
                dwQuality = -1,
                rcFrame = new AviRect { left = 0, top = 0, right = width, bottom = height },
                szName = "PNG sequence"
            };

            ThrowIfFailed(AVIFileCreateStreamW(aviFile, out rawStream, ref streamInfo), "AVIストリームを作成できませんでした。");

            if (codec.IsUncompressed)
            {
                outputStream = rawStream;
                log("無圧縮AVIで作成します。");
            }
            else
            {
                var options = new AviCompressOptions
                {
                    fccType = StreamTypeVideo,
                    fccHandler = codec.FourCc,
                    dwQuality = -1
                };

                ThrowIfFailed(
                    AVIMakeCompressedStream(out outputStream, rawStream, ref options, IntPtr.Zero),
                    $"{codec.DisplayName} を呼び出せませんでした。64bit版コーデックがインストールされているか確認してください。");

                log($"{codec.DisplayName} [{FourCcToString(codec.FourCc)}] を使用します。");
            }

            var bitmapInfo = new BitmapInfoHeader
            {
                biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                biWidth = width,
                biHeight = height,
                biPlanes = 1,
                biBitCount = bitCount,
                biCompression = BiRgb,
                biSizeImage = (uint)imageSize
            };

            formatPtr = Marshal.AllocHGlobal(Marshal.SizeOf<BitmapInfoHeader>());
            Marshal.StructureToPtr(bitmapInfo, formatPtr, false);
            ThrowIfFailed(
                AVIStreamSetFormat(outputStream, 0, formatPtr, Marshal.SizeOf<BitmapInfoHeader>()),
                "AVIストリームの画像形式を設定できませんでした。");

            progress(0);
            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // HOW: RGBA対応コーデックは32bit、それ以外は24bitのDIBフレームを作ります。
                var frame = codec.UseAlpha
                    ? CreateDibFrame32(files[index], width, height, rowSize)
                    : CreateDibFrame24(files[index], width, height, rowSize);
                var framePtr = Marshal.AllocHGlobal(frame.Length);

                try
                {
                    Marshal.Copy(frame, 0, framePtr, frame.Length);
                    ThrowIfFailed(
                        AVIStreamWrite(outputStream, index, 1, framePtr, frame.Length, AviIfKeyFrame, IntPtr.Zero, IntPtr.Zero),
                        $"フレームを書き込めませんでした: {Path.GetFileName(files[index])}");
                }
                finally
                {
                    Marshal.FreeHGlobal(framePtr);
                }

                progress(index + 1);
            }
        }
        finally
        {
            if (formatPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(formatPtr);
            }

            if (outputStream != IntPtr.Zero && outputStream != rawStream)
            {
                AVIStreamRelease(outputStream);
            }

            if (rawStream != IntPtr.Zero)
            {
                AVIStreamRelease(rawStream);
            }

            if (aviFile != IntPtr.Zero)
            {
                AVIFileRelease(aviFile);
            }

            AVIFileExit();
        }
    }

    public static int FourCc(string value)
    {
        if (value.Length != 4)
        {
            throw new ArgumentException("FourCC must be 4 characters.", nameof(value));
        }

        return value[0] | (value[1] << 8) | (value[2] << 16) | (value[3] << 24);
    }

    public static string FourCcToString(int value)
    {
        return new string(new[]
        {
            (char)(value & 0xFF),
            (char)((value >> 8) & 0xFF),
            (char)((value >> 16) & 0xFF),
            (char)((value >> 24) & 0xFF)
        });
    }

    private static AviCodecChoice CreateCodecChoice(string label, int fourCc, bool? useAlpha = null)
    {
        return new AviCodecChoice(
            label,
            fourCc,
            IsUncompressed: false,
            UseAlpha: useAlpha ?? LooksLikeAlphaCodec(label, fourCc),
            IsAvailable: CanOpenCodec(fourCc));
    }

    private static bool CanOpenCodec(int fourCc)
    {
        // WHY NOT: コーデック名が登録されているだけでは「使用可能」と判定しません。
        // 実際に圧縮モードで開けることを確認します。
        var handle = ICOpen(IcTypeVideo, fourCc, IcModeCompress);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        ICClose(handle);
        return true;
    }

    private static bool LooksLikeAlphaCodec(string label, int fourCc)
    {
        return label.Contains("RGBA", StringComparison.OrdinalIgnoreCase)
            || FourCcToString(fourCc).Equals("ULRA", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<InstalledCodec> EnumerateVideoCodecs()
    {
        for (var index = 0; index < 512; index++)
        {
            var info = new IcInfo
            {
                dwSize = Marshal.SizeOf<IcInfo>()
            };

            if (!ICInfo(IcTypeVideo, index, ref info))
            {
                yield break;
            }

            yield return new InstalledCodec(info.fccHandler, info.szName.TrimEnd('\0'), info.szDescription.TrimEnd('\0'));
        }
    }

    private static byte[] CreateDibFrame24(string path, int width, int height, int rowSize)
    {
        using var source = Image.FromFile(path);
        if (source.Width != width || source.Height != height)
        {
            throw new InvalidOperationException($"画像サイズが一致しません: {Path.GetFileName(path)}");
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
            graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        }

        return CopyBottomUp(bitmap, width, height, rowSize, 3, PixelFormat.Format24bppRgb);
    }

    private static byte[] CreateDibFrame32(string path, int width, int height, int rowSize)
    {
        using var source = Image.FromFile(path);
        if (source.Width != width || source.Height != height)
        {
            throw new InvalidOperationException($"画像サイズが一致しません: {Path.GetFileName(path)}");
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        }

        return CopyBottomUp(bitmap, width, height, rowSize, 4, PixelFormat.Format32bppArgb);
    }

    private static byte[] CopyBottomUp(Bitmap bitmap, int width, int height, int rowSize, int bytesPerPixel, PixelFormat pixelFormat)
    {
        var output = new byte[rowSize * height];
        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, pixelFormat);

        try
        {
            // HOW: DIBは下の行から格納するため、通常の画像データを上下反転してコピーします。
            for (var outputRow = 0; outputRow < height; outputRow++)
            {
                var sourceY = height - 1 - outputRow;
                var sourceRow = data.Stride >= 0
                    ? IntPtr.Add(data.Scan0, sourceY * data.Stride)
                    : IntPtr.Add(data.Scan0, outputRow * data.Stride);

                Marshal.Copy(sourceRow, output, outputRow * rowSize, width * bytesPerPixel);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return output;
    }

    private static void ThrowIfFailed(int result, string message)
    {
        if (result != 0)
        {
            throw new InvalidOperationException($"{message} エラーコード: 0x{result:X8}");
        }
    }

    private sealed record InstalledCodec(int FourCc, string Name, string Description);

    // 変更不可: 以下はWindows APIとの契約です。引数型・順序・CharSetを変更しないでください。
    [DllImport("msvfw32.dll", CharSet = CharSet.Unicode)]
    private static extern bool ICInfo(int fccType, int fccHandler, ref IcInfo lpicinfo);

    [DllImport("msvfw32.dll")]
    private static extern IntPtr ICOpen(int fccType, int fccHandler, int wMode);

    [DllImport("msvfw32.dll")]
    private static extern int ICClose(IntPtr hic);

    [DllImport("avifil32.dll")]
    private static extern void AVIFileInit();

    [DllImport("avifil32.dll")]
    private static extern void AVIFileExit();

    [DllImport("avifil32.dll", EntryPoint = "AVIFileOpenW", CharSet = CharSet.Unicode)]
    private static extern int AVIFileOpenW(out IntPtr ppfile, string szFile, int mode, IntPtr pclsidHandler);

    [DllImport("avifil32.dll", EntryPoint = "AVIFileCreateStreamW")]
    private static extern int AVIFileCreateStreamW(IntPtr pfile, out IntPtr ppavi, ref AviStreamInfo psi);

    [DllImport("avifil32.dll")]
    private static extern int AVIMakeCompressedStream(out IntPtr ppsCompressed, IntPtr psSource, ref AviCompressOptions lpOptions, IntPtr pclsidHandler);

    [DllImport("avifil32.dll")]
    private static extern int AVIStreamSetFormat(IntPtr pavi, int lPos, IntPtr lpFormat, int cbFormat);

    [DllImport("avifil32.dll")]
    private static extern int AVIStreamWrite(IntPtr pavi, int lStart, int lSamples, IntPtr lpBuffer, int cbBuffer, int dwFlags, IntPtr plSampWritten, IntPtr plBytesWritten);

    [DllImport("avifil32.dll")]
    private static extern int AVIStreamRelease(IntPtr pavi);

    [DllImport("avifil32.dll")]
    private static extern int AVIFileRelease(IntPtr pfile);

    [StructLayout(LayoutKind.Sequential)]
    private struct AviRect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AviStreamInfo
    {
        public int fccType;
        public int fccHandler;
        public int dwFlags;
        public int dwCaps;
        public short wPriority;
        public short wLanguage;
        public int dwScale;
        public int dwRate;
        public int dwStart;
        public int dwLength;
        public int dwInitialFrames;
        public int dwSuggestedBufferSize;
        public int dwQuality;
        public int dwSampleSize;
        public AviRect rcFrame;
        public int dwEditCount;
        public int dwFormatChangeCount;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AviCompressOptions
    {
        public int fccType;
        public int fccHandler;
        public int dwKeyFrameEvery;
        public int dwQuality;
        public int dwBytesPerSecond;
        public int dwFlags;
        public IntPtr lpFormat;
        public int cbFormat;
        public IntPtr lpParms;
        public int cbParms;
        public int dwInterleaveEvery;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public int biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IcInfo
    {
        public int dwSize;
        public int fccType;
        public int fccHandler;
        public int dwFlags;
        public int dwVersion;
        public int dwVersionICM;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string szName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szDescription;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szDriver;
    }
}
