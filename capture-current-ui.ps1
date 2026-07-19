param(
    [string]$ExecutablePath = "D:\Projects\PngSequence\bin\Release\net8.0-windows\win-x64\PngSequenceAvi.exe",
    [string]$OutputPath = "D:\Projects\PngSequence\current-ui.png",
    [switch]$OpenCodecList
)

# UI検証専用スクリプトです。アプリの変換処理や利用者向け機能には含まれません。
#
# ユーザーが変更してよい箇所:
# - ExecutablePath、OutputPath
#
# 変更不可の箇所:
# - DPI awareness設定とウィンドウ終了処理。高DPI画面の正確な撮影とプロセス残留防止に必要です。
#
# Codex用覚書:
# - HOW: 対象アプリを一時起動し、タイトル一致したウィンドウだけをPNGへ保存して終了する。
# - WHY NOT: 画面全体を無条件に撮影しない。背面アプリや個人情報が写る可能性があるため。

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class NativeWindowCapture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
}
"@

[NativeWindowCapture]::SetProcessDpiAwarenessContext([IntPtr]::new(-4)) | Out-Null

$launchedProcess = Start-Process -FilePath $ExecutablePath -PassThru
$windowProcess = $null
try {
    for ($attempt = 0; $attempt -lt 50 -and $null -eq $windowProcess; $attempt++) {
        Start-Sleep -Milliseconds 100
        $windowProcess = Get-Process | Where-Object {
            $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -eq "PNG Sequence AVI Forge"
        } | Select-Object -First 1
    }

    if ($null -eq $windowProcess) {
        throw "The application main window was not detected."
    }

    [NativeWindowCapture]::SetForegroundWindow($windowProcess.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 300

    if ($OpenCodecList) {
        $rootElement = [System.Windows.Automation.AutomationElement]::FromHandle($windowProcess.MainWindowHandle)
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ComboBox
        )
        $comboElement = $rootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition
        )
        if ($null -eq $comboElement) {
            throw "The codec combo box was not detected."
        }

        $pattern = $comboElement.GetCurrentPattern(
            [System.Windows.Automation.ExpandCollapsePattern]::Pattern
        )
        $pattern.Expand()
        Start-Sleep -Milliseconds 400
    }

    $rect = New-Object NativeWindowCapture+RECT
    if (-not [NativeWindowCapture]::GetWindowRect($windowProcess.MainWindowHandle, [ref]$rect)) {
        throw "The application window bounds could not be read."
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        if ($OpenCodecList) {
            $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
        }
        else {
            $deviceContext = $graphics.GetHdc()
            try {
                if (-not [NativeWindowCapture]::PrintWindow($windowProcess.MainWindowHandle, $deviceContext, 2)) {
                    throw "The application window could not be rendered."
                }
            }
            finally {
                $graphics.ReleaseHdc($deviceContext)
            }
        }
        $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}
finally {
    if ($null -ne $windowProcess -and -not $windowProcess.HasExited) {
        $windowProcess.CloseMainWindow() | Out-Null
        Start-Sleep -Milliseconds 200
        if (-not $windowProcess.HasExited) {
            $windowProcess.Kill()
        }
    }
    if (-not $launchedProcess.HasExited) {
        $launchedProcess.Kill()
    }
}

Write-Output $OutputPath
