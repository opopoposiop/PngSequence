# PNG Sequence AVI Forge

連番PNGを読み込み、Windows上でAVIへ変換するデスクトップアプリです。

## 主な機能

- PNGファイルまたはPNGフォルダーのドラッグ＆ドロップ
- ファイル名末尾の連番を読み取って並べ替え
- 1～240 FPSの指定
- 無圧縮AVIの出力
- UtVideo RGB / RGBA Codecの検出と選択
- 変換状況、ログ、進捗率の表示

## 必要環境

- Windows 11 64bit
- .NET 8 SDK（ビルド時）
- UtVideoを使用する場合は、64bit版UtVideo Codec

## 使い方

1. `PngSequenceAvi.exe`を起動します。
2. 連番PNGまたはPNGが入ったフォルダーをドロップします。
3. AVI出力先フォルダーを選択します。
4. フレームレートを入力します。
5. AVI圧縮形式を選択します。
6. 「AVIに変換」をクリックします。

ファイル名の例です。

```text
frame_0001.png
frame_0002.png
frame_0003.png
```

ファイル名の途中に番号がある場合も、末尾の番号を連番として使用します。

## ビルド

```powershell
dotnet build -c Release
```

単一実行ファイルを作成する場合は、次を実行します。

```powershell
powershell -ExecutionPolicy Bypass -File .\build-single-exe.ps1
```

生成物は次の場所に作成されます。

```text
bin\Release\net8.0-windows10.0.22000.0\win-x64\publish\PngSequenceAvi.exe
```

## ソース構成

| ファイル | 役割 |
|---|---|
| `Program.cs` | アプリケーションの起動 |
| `MainForm.cs` | UI、入力、ドラッグ＆ドロップ、変換進捗 |
| `SequenceSpec.cs` | PNGの連番解析 |
| `AviWriter.cs` | PNGからAVIへの変換とWindows API呼び出し |
| `PngSequenceAvi.csproj` | .NETとWindows Formsのビルド設定 |
| `app.manifest` | Windows実行時の設定 |
| `build-single-exe.ps1` | 単一実行ファイルの作成 |

## 変更してよい箇所

非エンジニアの方が変更する場合は、まず次の箇所を対象にしてください。

- `MainForm.cs`の色、文字サイズ、余白、表示文言
- `AviWriter.cs`の`knownFallbacks`にあるCodec候補
- READMEの説明文

変更後は必ずReleaseビルドとアプリ起動を確認してください。

## 変更しない箇所

次の処理はWindows API、メモリ管理、スレッド処理に関係するため、動作を理解せず変更しないでください。

- `AviWriter.cs`の`DllImport`宣言
- `Marshal.AllocHGlobal`と`Marshal.FreeHGlobal`の対応
- `AVIFileInit`、`AVIFileExit`、各`Release`処理
- DIBの4バイト境界と画像の上下反転処理
- `MainForm.cs`の`InvokeRequired`とキャンセル処理
- `Program.cs`の`[STAThread]`と`ApplicationConfiguration.Initialize()`

## コメントとコミットのルール

- コードには`HOW`を書く：どのように処理しているか
- テストコードには`WHAT`を書く：何を確認するテストか
- コードコメントには`WHY NOT`を書く：なぜ別の方法を採用しないか
- コミットメッセージには`WHY`を書く：なぜ変更したか
- 非エンジニアにも伝わる日本語で、変更可能箇所と変更不可箇所を説明する

## テスト項目

自動テストプロジェクトはありません。変更後は次を手動確認します。

1. アプリが起動する
2. PNGフォルダーをドロップできる
3. 複数PNGを選択できる
4. 連番が正しく表示される
5. AVIを出力できる
6. 変換中に進捗が更新される
7. 停止操作が動作する
8. 100%、150%、175%のDPIで表示が崩れない

## トラブルシューティング

### PNGが見つからない

- 拡張子が`.png`であることを確認してください。
- ファイル名末尾に数字があることを確認してください。
- すべての画像サイズが同じであることを確認してください。

### UtVideoが使用できない

64bit版Codecがインストールされていることを確認してから、アプリを再起動してください。

### AVIを出力できない

出力先の書き込み権限、空き容量、同名AVIを別アプリで開いていないかを確認してください。

## UIデザイン

UIは次のデザインシステムを参考にしています。

```text
D:\Projects\Refine_PngSequence\design-system-example-components-html
```
