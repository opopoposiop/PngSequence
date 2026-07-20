using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace PngSequenceAvi;

/*
 * アプリの画面表示、ボタン操作、ドラッグ＆ドロップ、進捗表示を担当します。
 *
 * ユーザーが変更してよい箇所:
 * - ファイル先頭にある色定義
 * - 画面の文言、余白、フォントサイズ、角丸半径
 * - BuildUi、ConfigureDropPanel、CreateSettingsCard内の見た目
 *
 * 変更不可の箇所:
 * - ConvertAsyncからVideoWriter.WriteAsyncへ渡す値と処理順
 * - InvokeRequiredを使ったUIスレッドへの切り替え
 * - CancellationTokenSourceの生成、Cancel、Dispose
 * - ComboBoxのP/Invoke構造体とDllImport宣言
 *
 * Codex用覚書:
 * - HOW: UI部品は小さな作成メソッドへ分け、変換処理はVideoWriterへ委譲します。
 * - WHY NOT: 見た目の修正時に変換ロジックをMainFormへ追加しません。
 *   UIと動画処理が混ざると、表示変更だけで出力結果を壊す危険が増えるためです。
 */
public sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(247, 248, 250);
    private static readonly Color Surface = Color.White;
    private static readonly Color SurfaceMuted = Color.FromArgb(250, 250, 250);
    private static readonly Color Border = Color.FromArgb(179, 179, 179);
    private static readonly Color TextColor = Color.FromArgb(26, 26, 26);
    private static readonly Color SubtleText = Color.FromArgb(102, 102, 102);
    private static readonly Color KeyColor = Color.FromArgb(0, 23, 193);
    private static readonly Color KeyDark = Color.FromArgb(0, 17, 143);
    private static readonly Color KeyTint = Color.FromArgb(232, 241, 254);
    private static readonly Color Success = Color.FromArgb(37, 157, 99);
    private static readonly Color SuccessTint = Color.FromArgb(230, 245, 236);
    private static readonly Color Danger = Color.FromArgb(206, 0, 0);
    private static readonly Color Disabled = Color.FromArgb(230, 230, 230);

    private readonly TextBox _outputFolderBox = new();
    private readonly TextBox _fpsBox = new();
    private readonly TextBox _statusBox = new();
    private readonly ComboBox _codecBox = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Label _progressLabel = new();
    private readonly Label _dropTitleLabel = new();
    private readonly Label _dropDetailLabel = new();
    private readonly Label _frameCountLabel = new();
    private readonly RoundedButton _convertButton = new();
    private readonly RoundedButton _stopButton = new();
    private readonly RoundedButton _selectFilesButton = new();
    private readonly DropZonePanel _dropPanel = new();
    private readonly VideoWriter _writer = new();

    private CancellationTokenSource? _cancellation;
    private SequenceSpec? _sequence;
    private int _progressTotal = 1;

    public MainForm()
    {
        Text = "PNG Sequence Video Forge";
        MinimumSize = new Size(1040, 720);
        Size = new Size(1280, 760);
        BackColor = Background;
        ForeColor = TextColor;
        Font = new Font("Noto Sans JP", 10.5F, FontStyle.Regular, GraphicsUnit.Point);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildUi();
        WireEvents();
        SetInitialValues();
    }

    private void BuildUi()
    {
        // HOW: 画面全体を「見出し」と「左:読込/ログ、右:変換設定」の2段構成で組み立てます。
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(30, 24, 30, 24),
            BackColor = Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Background,
            Margin = new Padding(0)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var headingStack = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Background,
            Margin = new Padding(0)
        };
        headingStack.Controls.Add(new Label
        {
            Text = "PNG Sequence AVI Forge",
            AutoSize = true,
            ForeColor = TextColor,
            Font = new Font(Font.FontFamily, 21F, FontStyle.Bold),
            Margin = new Padding(0)
        }, 0, 0);
        headingStack.Controls.Add(new Label
        {
            Text = "連番PNGを読み込み、AVIファイルへ変換します。",
            AutoSize = true,
            ForeColor = SubtleText,
            Font = new Font(Font.FontFamily, 10F),
            Margin = new Padding(0, 1, 0, 0)
        }, 0, 1);
        header.Controls.Add(headingStack, 0, 0);

        header.Controls.Add(new Label
        {
            Text = "1. 読み込み  ›  2. 設定  ›  3. 変換",
            AutoSize = true,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            ForeColor = KeyColor,
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold),
            Margin = new Padding(12, 0, 0, 14)
        }, 1, 0);
        root.Controls.Add(header, 0, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Background,
            Margin = new Padding(0)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 470));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(content, 0, 1);

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 22, 0),
            BackColor = Background
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(left, 0, 0);

        ConfigureDropPanel();
        left.Controls.Add(_dropPanel, 0, 0);

        var statusCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            BorderColor = Border,
            CornerRadius = 8,
            Margin = new Padding(0, 18, 0, 0),
            Padding = new Padding(8)
        };
        var statusLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Surface,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var statusHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Surface,
            Padding = new Padding(18, 0, 16, 0),
            Margin = new Padding(0)
        };
        statusHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusHeader.Controls.Add(CreateHeadingLabel("処理状況", 12F), 0, 0);
        statusHeader.Controls.Add(new Label
        {
            Text = "●  準備完了",
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            ForeColor = Success,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            Margin = new Padding(0)
        }, 1, 0);
        statusLayout.Controls.Add(statusHeader, 0, 0);

        _statusBox.Multiline = true;
        _statusBox.ReadOnly = true;
        _statusBox.ScrollBars = ScrollBars.Vertical;
        _statusBox.BackColor = SurfaceMuted;
        _statusBox.ForeColor = SubtleText;
        _statusBox.BorderStyle = BorderStyle.None;
        _statusBox.Dock = DockStyle.Fill;
        _statusBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
        _statusBox.WordWrap = false;
        _statusBox.Padding = new Padding(16);
        statusLayout.Controls.Add(_statusBox, 0, 1);
        statusCard.Controls.Add(statusLayout);
        left.Controls.Add(statusCard, 0, 1);

        content.Controls.Add(CreateSettingsCard(), 1, 0);
    }

    private void ConfigureDropPanel()
    {
        _dropPanel.AllowDrop = true;
        _dropPanel.Dock = DockStyle.Fill;
        _dropPanel.Margin = new Padding(0);
        _dropPanel.Padding = new Padding(28, 22, 28, 22);
        _dropPanel.BackColor = SuccessTint;
        _dropPanel.BorderColor = Success;
        _dropPanel.CornerRadius = 8;

        var dropLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = SuccessTint,
            Margin = new Padding(0)
        };
        dropLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        dropLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        dropLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        dropLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        dropLayout.Controls.Add(new Label
        {
            Text = "⇧",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Success,
            Font = new Font("Segoe UI Symbol", 27F, FontStyle.Regular),
            Margin = new Padding(0)
        }, 0, 0);

        _dropTitleLabel.Text = "連番PNGをここにドロップ";
        _dropTitleLabel.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
        _dropTitleLabel.ForeColor = TextColor;
        _dropTitleLabel.Dock = DockStyle.Fill;
        _dropTitleLabel.TextAlign = ContentAlignment.MiddleCenter;
        _dropTitleLabel.Margin = new Padding(0);

        _dropDetailLabel.Text = "フォルダー、または複数のPNGファイルを選択できます";
        _dropDetailLabel.ForeColor = SubtleText;
        _dropDetailLabel.Dock = DockStyle.Fill;
        _dropDetailLabel.TextAlign = ContentAlignment.MiddleCenter;
        _dropDetailLabel.Font = new Font(Font.FontFamily, 9.5F);
        _dropDetailLabel.Margin = new Padding(0);

        ConfigureOutlineButton(_selectFilesButton, "ファイルを選択");
        _selectFilesButton.Anchor = AnchorStyles.Top;
        _selectFilesButton.MinimumSize = new Size(146, 48);
        _selectFilesButton.Margin = new Padding(0, 8, 0, 0);

        dropLayout.Controls.Add(_dropTitleLabel, 0, 1);
        dropLayout.Controls.Add(_dropDetailLabel, 0, 2);
        dropLayout.Controls.Add(_selectFilesButton, 0, 3);
        _dropPanel.Controls.Add(dropLayout);
    }

    private Control CreateSettingsCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            BorderColor = Border,
            CornerRadius = 8,
            Margin = new Padding(0),
            Padding = new Padding(22)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Surface,
            Margin = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

        layout.Controls.Add(CreateHeadingLabel("変換設定", 14F), 0, 0);
        layout.Controls.Add(CreatePathInput(), 0, 1);
        layout.Controls.Add(CreateFpsInput(), 0, 2);
        layout.Controls.Add(CreateCodecInput(), 0, 3);
        layout.Controls.Add(CreateProgressArea(), 0, 4);
        layout.Controls.Add(CreateActionPanel(), 0, 5);
        card.Controls.Add(layout);
        return card;
    }

    private Control CreatePathInput()
    {
        var field = CreateFieldLayout("動画出力先フォルダー", 2);
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Surface,
            Margin = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));

        StyleTextBox(_outputFolderBox);
        _outputFolderBox.Dock = DockStyle.Fill;
        _outputFolderBox.Margin = new Padding(0);

        var browseButton = new RoundedButton();
        ConfigureOutlineButton(browseButton, "選択...");
        browseButton.Dock = DockStyle.Top;
        browseButton.Height = 48;
        browseButton.Margin = new Padding(0);
        browseButton.Click += (_, _) => BrowseOutputFolder();

        var outputHost = CreateRoundedFieldHost(_outputFolderBox, new Padding(0, 0, 8, 0));
        outputHost.Dock = DockStyle.Top;
        outputHost.Height = 48;
        row.Controls.Add(outputHost, 0, 0);
        row.Controls.Add(browseButton, 1, 0);
        field.Controls.Add(row, 0, 1);
        return field;
    }

    private Control CreateFpsInput()
    {
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Surface,
            Margin = new Padding(0)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));

        var fpsField = CreateFieldLayout("フレームレート   1–240", 2);
        _fpsBox.Text = "30";
        _fpsBox.Dock = DockStyle.Fill;
        _fpsBox.Margin = new Padding(0);
        StyleTextBox(_fpsBox);
        var fpsHost = CreateRoundedFieldHost(_fpsBox, new Padding(0, 0, 16, 0));
        fpsHost.Dock = DockStyle.Top;
        fpsHost.Height = 48;
        fpsField.Controls.Add(fpsHost, 0, 1);

        var countField = CreateFieldLayout("予定フレーム数", 2);
        _frameCountLabel.Text = "未選択";
        _frameCountLabel.Dock = DockStyle.Fill;
        _frameCountLabel.TextAlign = ContentAlignment.MiddleLeft;
        _frameCountLabel.BackColor = SurfaceMuted;
        _frameCountLabel.ForeColor = SubtleText;
        _frameCountLabel.BorderStyle = BorderStyle.None;
        _frameCountLabel.Padding = new Padding(14, 0, 0, 0);
        _frameCountLabel.Margin = new Padding(0);
        var countHost = CreateRoundedFieldHost(
            _frameCountLabel,
            new Padding(0),
            SurfaceMuted,
            new Padding(1));
        countHost.Dock = DockStyle.Top;
        countHost.Height = 48;
        _frameCountLabel.BackColor = Color.Transparent;
        countField.Controls.Add(countHost, 0, 1);

        fields.Controls.Add(fpsField, 0, 0);
        fields.Controls.Add(countField, 1, 0);
        return fields;
    }

    private Control CreateCodecInput()
    {
        var field = CreateFieldLayout("出力形式   同梱FFmpegで変換", 2);

        _codecBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _codecBox.DrawMode = DrawMode.OwnerDrawFixed;
        _codecBox.Font = new Font(Font.FontFamily, 12.75F, FontStyle.Regular, GraphicsUnit.Point);
        var codecTextHeight = TextRenderer.MeasureText(
            "Ag",
            _codecBox.Font,
            Size.Empty,
            TextFormatFlags.NoPadding).Height;
        _codecBox.ItemHeight = codecTextHeight;
        _codecBox.DropDownHeight = codecTextHeight * 4 + 8;
        _codecBox.MaxDropDownItems = 4;
        _codecBox.IntegralHeight = false;
        _codecBox.Dock = DockStyle.Fill;
        _codecBox.BackColor = Surface;
        _codecBox.ForeColor = TextColor;
        _codecBox.FlatStyle = FlatStyle.Flat;
        _codecBox.Margin = new Padding(0);
        _codecBox.DrawItem += DrawCodecItem;
        _codecBox.DropDown += (_, _) => BeginInvoke(new Action(() => ApplyComboDropDownRegion(_codecBox, 8)));
        _codecBox.Items.AddRange(VideoWriter.GetOutputChoices().Cast<object>().ToArray());
        _codecBox.SelectedIndex = 0;
        var codecHost = CreateRoundedFieldHost(
            _codecBox,
            new Padding(0),
            Surface,
            new Padding(3));
        codecHost.Dock = DockStyle.Top;
        codecHost.Height = codecTextHeight + 8;
        field.Controls.Add(codecHost, 0, 1);

        return field;
    }

    private Control CreateProgressArea()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Surface,
            Padding = new Padding(0, 18, 0, 0),
            Margin = new Padding(0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _progressLabel.Text = "変換進捗    0 / 0 フレーム";
        _progressLabel.Dock = DockStyle.Fill;
        _progressLabel.ForeColor = SubtleText;
        _progressLabel.Font = new Font(Font.FontFamily, 9.5F);
        _progressLabel.Margin = new Padding(0);

        _progressBar.Dock = DockStyle.Fill;
        _progressBar.Margin = new Padding(0, 6, 0, 6);
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 1;
        _progressBar.Value = 0;
        _progressBar.Style = ProgressBarStyle.Continuous;

        panel.Controls.Add(_progressLabel, 0, 0);
        panel.Controls.Add(_progressBar, 0, 1);
        panel.Controls.Add(new Label
        {
            Text = "ProRes 4444 MOVはアルファを保持します。AVIはOpenDML対応で4GBを超えても単一ファイルです。",
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = SubtleText,
            Font = new Font(Font.FontFamily, 8.5F),
            Margin = new Padding(0, 8, 0, 0)
        }, 0, 2);
        return panel;
    }

    private Control CreateActionPanel()
    {
        var actions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            WrapContents = false,
            BackColor = Surface,
            Padding = new Padding(0, 8, 0, 0),
            Margin = new Padding(0)
        };

        ConfigurePrimaryButton(_convertButton, "動画に変換");
        ConfigureDisabledButton(_stopButton, "停止");
        _stopButton.Enabled = false;
        actions.Controls.Add(_convertButton);
        actions.Controls.Add(_stopButton);
        return actions;
    }

    private static void StyleTextBox(TextBox textBox)
    {
        textBox.BackColor = Surface;
        textBox.ForeColor = TextColor;
        textBox.Font = new Font("Noto Sans JP", 10.5F, FontStyle.Regular, GraphicsUnit.Point);
        textBox.AutoSize = false;
        textBox.Height = 48;
        textBox.BorderStyle = BorderStyle.None;
        textBox.Padding = new Padding(0);
    }

    private static RoundedFieldPanel CreateRoundedFieldHost(
        Control control,
        Padding margin,
        Color? background = null,
        Padding? padding = null)
    {
        var fill = background ?? Surface;
        var host = new RoundedFieldPanel
        {
            Dock = DockStyle.Fill,
            BackColor = fill,
            BorderColor = Color.FromArgb(102, 102, 102),
            FocusBorderColor = Color.Black,
            CornerRadius = 8,
            Margin = margin,
            Padding = padding ?? new Padding(14, 10, 14, 8)
        };
        control.BackColor = fill;
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0);
        control.Enter += (_, _) =>
        {
            host.IsFocused = true;
            host.Invalidate();
        };
        control.Leave += (_, _) =>
        {
            host.IsFocused = false;
            host.Invalidate();
        };
        host.Controls.Add(control);
        return host;
    }

    private static TableLayoutPanel CreateFieldLayout(string labelText, int rows)
    {
        var field = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = rows,
            BackColor = Surface,
            Margin = new Padding(0)
        };
        field.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        if (rows > 1)
        {
            field.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        }
        field.Controls.Add(new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            ForeColor = TextColor,
            Font = new Font("Noto Sans JP", 10F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        }, 0, 0);
        return field;
    }

    private static Label CreateHeadingLabel(string text, float size)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = TextColor,
            Font = new Font("Noto Sans JP", size, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        };
    }

    private static void ConfigurePrimaryButton(RoundedButton button, string text)
    {
        ConfigureButtonBase(button, text);
        button.MinimumSize = new Size(152, 56);
        button.Size = button.MinimumSize;
        button.FillColor = KeyColor;
        button.HoverFillColor = KeyDark;
        button.BorderColor = KeyColor;
        button.TextColor = Color.White;
    }

    private static void ConfigureOutlineButton(RoundedButton button, string text)
    {
        ConfigureButtonBase(button, text);
        button.MinimumSize = new Size(96, 48);
        button.Size = button.MinimumSize;
        button.FillColor = Surface;
        button.HoverFillColor = KeyTint;
        button.BorderColor = KeyColor;
        button.TextColor = KeyColor;
    }

    private static void ConfigureDisabledButton(RoundedButton button, string text)
    {
        ConfigureButtonBase(button, text);
        button.MinimumSize = new Size(104, 56);
        button.Size = button.MinimumSize;
        button.FillColor = Danger;
        button.HoverFillColor = Color.FromArgb(169, 0, 0);
        button.BorderColor = Danger;
        button.TextColor = Color.White;
        button.DisabledFillColor = Disabled;
        button.DisabledBorderColor = Border;
        button.DisabledTextColor = SubtleText;
    }

    private static void ConfigureButtonBase(RoundedButton button, string text)
    {
        button.Text = text;
        button.AutoSize = false;
        button.Margin = new Padding(10, 0, 0, 0);
        button.Font = new Font("Noto Sans JP", 10F, FontStyle.Bold, GraphicsUnit.Point);
        button.Cursor = Cursors.Hand;
        button.CornerRadius = 8;
    }

    private static int ScaleForDpi(Control control, int logicalPixels)
    {
        return Math.Max(1, (int)Math.Round(logicalPixels * control.DeviceDpi / 96D));
    }

    private static void ApplyComboDropDownRegion(ComboBox comboBox, int logicalRadius)
    {
        // HOW: 展開候補は別のWindowsウィンドウなので、そのウィンドウへDPI対応の角丸領域を設定します。
        var info = new ComboBoxInfo { Size = Marshal.SizeOf<ComboBoxInfo>() };
        if (!GetComboBoxInfo(comboBox.Handle, ref info)
            || info.ListHandle == IntPtr.Zero
            || !GetWindowRect(info.ListHandle, out var bounds))
        {
            return;
        }

        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        var radius = ScaleForDpi(comboBox, logicalRadius);
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (region == IntPtr.Zero)
        {
            return;
        }

        if (SetWindowRgn(info.ListHandle, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    private void DrawCodecItem(object? sender, DrawItemEventArgs e)
    {
        // WHY NOT: 標準ComboBoxの描画は使いません。
        // 文字サイズ、項目高、同梱バッジをデザイン仕様どおり表示できないためです。
        e.DrawBackground();
        if (e.Index < 0 || _codecBox.Items[e.Index] is not VideoOutputChoice choice)
        {
            return;
        }

        var isSelected = (e.State & DrawItemState.Selected) != 0;
        var isEdit = (e.State & DrawItemState.ComboBoxEdit) != 0;
        var background = isSelected && !isEdit ? KeyTint : Surface;
        using var backgroundBrush = new SolidBrush(background);
        e.Graphics.FillRectangle(backgroundBrush, e.Bounds);

        var label = choice.DisplayName;
        var textColor = TextColor;
        const string badgeText = "同梱";
        using var badgeFont = new Font(Font.FontFamily, 8F, FontStyle.Bold);
        var badgeSize = TextRenderer.MeasureText(badgeText, badgeFont);
        var badgeWidth = badgeSize.Width + 18;
        var textHeight = TextRenderer.MeasureText(
            label,
            e.Font,
            Size.Empty,
            TextFormatFlags.NoPadding).Height;
        var textBounds = new Rectangle(
            e.Bounds.Left + 16,
            e.Bounds.Top + Math.Max(0, (e.Bounds.Height - textHeight) / 2),
            Math.Max(0, e.Bounds.Width - badgeWidth - 42),
            Math.Min(textHeight, e.Bounds.Height));
        TextRenderer.DrawText(
            e.Graphics,
            label,
            e.Font,
            textBounds,
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (e.Bounds.Width > 280)
        {
            var badgeHeight = Math.Clamp(e.Bounds.Height - 4, 16, 20);
            var badgeBounds = new Rectangle(
                e.Bounds.Right - badgeWidth - 14,
                e.Bounds.Top + (e.Bounds.Height - badgeHeight) / 2,
                badgeWidth,
                badgeHeight);
            var badgeColor = SuccessTint;
            var badgeTextColor = Color.FromArgb(17, 90, 54);
            using var badgePath = CreateRoundedPath(badgeBounds, badgeHeight / 2);
            using var badgeBrush = new SolidBrush(badgeColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(badgeBrush, badgePath);
            TextRenderer.DrawText(
                e.Graphics,
                badgeText,
                badgeFont,
                badgeBounds,
                badgeTextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        e.DrawFocusRectangle();
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ComboBoxInfo
    {
        public int Size;
        public NativeRect ItemBounds;
        public NativeRect ButtonBounds;
        public int ButtonState;
        public IntPtr ComboHandle;
        public IntPtr ItemHandle;
        public IntPtr ListHandle;
    }

    // 変更不可: ComboBoxの展開リストを角丸化するWindows API契約です。
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetComboBoxInfo(IntPtr comboBoxHandle, ref ComboBoxInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect bounds);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int ellipseWidth,
        int ellipseHeight);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr windowHandle, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    private void WireEvents()
    {
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        _dropPanel.DragEnter += OnDragEnter;
        _dropPanel.DragDrop += OnDragDrop;
        _selectFilesButton.Click += (_, _) => BrowsePngFiles();
        _convertButton.Click += async (_, _) => await ConvertAsync();
        _stopButton.Click += (_, _) => StopRunningProcess();
    }

    private void SetInitialValues()
    {
        _outputFolderBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Log("準備完了。PNG連番ファイルをドロップしてください。");
        Log($"出力形式候補: {_codecBox.Items.Count} 件");
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        var dropped = e.Data?.GetData(DataFormats.FileDrop) as string[];
        if (dropped is null || dropped.Length == 0)
        {
            return;
        }

        LoadSequence(ExpandDroppedItems(dropped));
    }

    private static IEnumerable<string> ExpandDroppedItems(IEnumerable<string> dropped)
    {
        foreach (var item in dropped)
        {
            if (Directory.Exists(item))
            {
                foreach (var file in Directory.EnumerateFiles(item, "*.png", SearchOption.TopDirectoryOnly))
                {
                    yield return file;
                }
            }
            else
            {
                yield return item;
            }
        }
    }

    private void BrowsePngFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "連番PNGファイルを選択",
            Filter = "PNGファイル (*.png)|*.png",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            LoadSequence(dialog.FileNames);
        }
    }

    private void LoadSequence(IEnumerable<string> files)
    {
        try
        {
            _sequence = SequenceSpec.FromFiles(files);
            _dropTitleLabel.Text = _sequence.DisplayName;
            _dropDetailLabel.Text = $"{_sequence.Count} ファイル / {_sequence.StartNumber} - {_sequence.EndNumber} / {_sequence.GetMissingNumberSummary()}";
            _frameCountLabel.Text = $"{_sequence.Count:N0}";
            ResetProgress(_sequence.Count);
            Log($"PNG連番を読み込みました: {_sequence.PatternPath}");
            Log(_dropDetailLabel.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "読み込みエラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Log($"エラー: {ex.Message}");
        }
    }

    private async Task ConvertAsync()
    {
        // HOW: 入力確認後にUIを実行中状態へ切り替え、FFmpegの非同期変換を待機します。
        var running = false;

        try
        {
            if (_sequence is null)
            {
                MessageBox.Show(this, "先に連番PNGファイルをドロップしてください。", "確認", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var outputFolder = EnsureOutputFolder();
            var fps = GetFps();
            if (fps is null)
            {
                return;
            }

            var codec = GetSelectedCodec();
            var baseName = SanitizeFileName(string.IsNullOrWhiteSpace(_sequence.Prefix) ? "png_sequence" : _sequence.Prefix.TrimEnd('_', '-', ' '));
            var outputPath = Path.Combine(outputFolder, $"{baseName}_{codec.FileSuffix}{codec.Extension}");

            SetRunning(true);
            running = true;
            _cancellation = new CancellationTokenSource();
            ResetProgress(_sequence.Count);

            Log($"動画作成を開始します: {outputPath}");
            var completedPath = await _writer.WriteAsync(
                _sequence.Files,
                outputPath,
                fps.Value,
                codec,
                LogFromAnyThread,
                UpdateProgressFromAnyThread,
                _cancellation.Token);

            SetProgress(_sequence.Count);
            Log($"動画作成完了: {completedPath}");
        }
        catch (OperationCanceledException)
        {
            Log("停止しました。");
        }
        catch (Exception ex)
        {
            Log($"エラー: {ex.Message}");
            MessageBox.Show(this, ex.Message, "動画作成エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (running)
            {
                SetRunning(false);
            }

            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private string EnsureOutputFolder()
    {
        var folder = _outputFolderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new InvalidOperationException("動画出力先フォルダを指定してください。");
        }

        Directory.CreateDirectory(folder);
        return folder;
    }

    private int? GetFps()
    {
        if (int.TryParse(_fpsBox.Text.Trim(), out var fps) && fps is >= 1 and <= 240)
        {
            return fps;
        }

        MessageBox.Show(this, "FPSは 1 から 240 の整数で指定してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return null;
    }

    private VideoOutputChoice GetSelectedCodec()
    {
        return _codecBox.SelectedItem as VideoOutputChoice
            ?? throw new InvalidOperationException("出力形式を選択してください。");
    }

    private void BrowseOutputFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "動画出力先フォルダを選択",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputFolderBox.Text = dialog.SelectedPath;
        }
    }

    private void StopRunningProcess()
    {
        _cancellation?.Cancel();
        Log("停止要求を送信しました。");
    }

    private void SetRunning(bool running)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetRunning(running)));
            return;
        }

        _convertButton.Enabled = !running;
        _codecBox.Enabled = !running;
        _stopButton.Enabled = running;
        UseWaitCursor = running;
    }

    private void ResetProgress(int totalFrames)
    {
        _progressTotal = Math.Max(totalFrames, 1);
        _progressBar.Maximum = _progressTotal;
        _progressBar.Value = 0;
        _progressLabel.Text = $"変換進捗    0 / {_progressTotal} フレーム";
    }

    private void UpdateProgressFromAnyThread(int frame)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetProgress(frame)));
            return;
        }

        SetProgress(frame);
    }

    private void SetProgress(int frame)
    {
        var value = Math.Clamp(frame, 0, _progressTotal);
        if (_progressBar.Maximum != _progressTotal)
        {
            _progressBar.Maximum = _progressTotal;
        }

        _progressBar.Value = value;
        _progressLabel.Text = $"変換進捗    {value} / {_progressTotal} フレーム";
    }

    private void LogFromAnyThread(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => Log(message)));
            return;
        }

        Log(message);
    }

    private void Log(string message)
    {
        _statusBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private static string SanitizeFileName(string value)
    {
        var fallback = "png_sequence";
        var cleaned = Regex.Replace(value, $"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]", "_").Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private sealed class CardPanel : Panel
    {
        public Color BorderColor { get; set; } = Border;

        public int CornerRadius { get; set; } = 8;

        public CardPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.UserPaint,
                true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var parentColor = Parent?.BackColor ?? Background;
            e.Graphics.Clear(parentColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var radius = ScaleForDpi(this, CornerRadius);
            using var path = CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), radius);
            using var brush = new SolidBrush(BackColor);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width <= 1 || Height <= 1)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var radius = ScaleForDpi(this, CornerRadius);
            using var path = CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), radius);
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawPath(pen, path);
        }
    }

    private sealed class RoundedFieldPanel : Panel
    {
        public Color BorderColor { get; set; } = Border;

        public Color FocusBorderColor { get; set; } = Color.Black;

        public int CornerRadius { get; set; } = 8;

        public bool IsFocused { get; set; }

        public RoundedFieldPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.UserPaint,
                true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var parentColor = Parent?.BackColor ?? Surface;
            e.Graphics.Clear(parentColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var radius = ScaleForDpi(this, CornerRadius);
            using var path = CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), radius);
            using var brush = new SolidBrush(BackColor);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width <= 1 || Height <= 1)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var radius = ScaleForDpi(this, CornerRadius);
            using var path = CreateRoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), radius);
            using var pen = new Pen(IsFocused ? FocusBorderColor : BorderColor, IsFocused ? 2F : 1F);
            e.Graphics.DrawPath(pen, path);
        }
    }

    private sealed class RoundedButton : Button
    {
        // WHY NOT: 標準Buttonの枠をリージョンで切り抜きません。
        // 高DPI環境で角が斜めに切れたように見えるため、アンチエイリアスで直接描画します。
        private bool _isHovered;

        public Color FillColor { get; set; } = Surface;

        public Color HoverFillColor { get; set; } = KeyTint;

        public Color BorderColor { get; set; } = KeyColor;

        public Color TextColor { get; set; } = KeyColor;

        public Color DisabledFillColor { get; set; } = Disabled;

        public Color DisabledBorderColor { get; set; } = Border;

        public Color DisabledTextColor { get; set; } = SubtleText;

        public int CornerRadius { get; set; } = 8;

        public RoundedButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.UserPaint,
                true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? Background);

            var radius = ScaleForDpi(this, CornerRadius);
            var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
            using var path = CreateRoundedPath(bounds, radius);
            var fillColor = Enabled
                ? (_isHovered ? HoverFillColor : FillColor)
                : DisabledFillColor;
            var borderColor = Enabled ? BorderColor : DisabledBorderColor;
            var textColor = Enabled ? TextColor : DisabledTextColor;
            using var fillBrush = new SolidBrush(fillColor);
            using var borderPen = new Pen(borderColor, Math.Max(1F, DeviceDpi / 96F));
            e.Graphics.FillPath(fillBrush, path);
            e.Graphics.DrawPath(borderPen, path);

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                bounds,
                textColor,
                TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis);

            if (Focused && ShowFocusCues)
            {
                var inset = ScaleForDpi(this, 4);
                var focusBounds = Rectangle.Inflate(bounds, -inset, -inset);
                using var focusPath = CreateRoundedPath(focusBounds, Math.Max(2, radius - inset));
                using var focusPen = new Pen(Color.Black) { DashStyle = DashStyle.Dot };
                e.Graphics.DrawPath(focusPen, focusPath);
            }
        }
    }

    private sealed class DropZonePanel : Panel
    {
        public Color BorderColor { get; set; } = Success;

        public int CornerRadius { get; set; } = 8;

        public DropZonePanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.UserPaint,
                true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var parentColor = Parent?.BackColor ?? Background;
            e.Graphics.Clear(parentColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var radius = ScaleForDpi(this, CornerRadius);
            using var path = CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), radius);
            using var brush = new SolidBrush(BackColor);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width <= 2 || Height <= 2)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var radius = ScaleForDpi(this, CornerRadius);
            using var path = CreateRoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), radius);
            using var pen = new Pen(BorderColor, 2F) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawPath(pen, path);
        }
    }
}
