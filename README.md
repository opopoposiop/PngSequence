# PNG Sequence AVI Forge

連番のPNG画像を番号順に読み込み、AVI動画へ変換するWindows 11向けデスクトップアプリです。  
ファイルを画面へドラッグ＆ドロップするだけで、無圧縮AVIまたはUT Video Codecを使ったAVIを作成できます。

![アプリ画面](implemented-ui-rounded-final.png)

## このアプリでできること

- PNGファイルまたはPNG入りフォルダーのドラッグ＆ドロップ
- 「ファイルを選択」ボタンから複数のPNGを選択
- ファイル名末尾の番号を読み取り、正しい順番へ並べ替え
- 欠番の確認
- AVI出力先フォルダーの指定
- 1～240 FPSの指定
- 無圧縮AVIの作成
- 64bit版UT Video Codecの検出
- コーデックが実際に使用可能か確認して表示
- RGB動画とアルファ付きRGBA動画への対応
- 進捗、ログ、停止操作

## 対応するファイル名

ファイル名の末尾に連番があるPNGを使用します。

```text
frame_0001.png
frame_0002.png
frame_0003.png
```

次のように途中に別の数字があっても、末尾の数字を連番として扱います。

```text
shot01_frame_0001.png
shot01_frame_0002.png
```

異なる命名規則のファイルを同時に選んだ場合は、同じフォルダー・接頭辞・桁数で構成される最も枚数の多いグループを使用します。

## 使い方

1. `PngSequenceAvi.exe`を起動します。
2. 連番PNGまたは連番PNGが入ったフォルダーを緑色の領域へドロップします。
3. 「AVI出力先フォルダー」を確認します。
4. フレームレートを1～240の整数で入力します。
5. 「AVI圧縮形式」を選択します。
6. 「AVIに変換」を押します。
7. 処理状況に「AVI作成完了」と表示されたら完了です。

変換中に中止する場合は「停止」を押します。

## 実行ファイル

開発用のRelease成果物:

```text
bin\Release\net8.0-windows10.0.22000.0\win-x64\PngSequenceAvi.exe
```

単体配布用の自己完結exe:

```text
bin\Release\net8.0-windows10.0.22000.0\win-x64\publish\PngSequenceAvi.exe
```

配布には`publish`フォルダー内のexeを使用してください。

## 動作環境

- Windows 11 64bit
- x64 CPU
- 無圧縮AVIのみ使う場合、追加コーデックは不要
- UT Videoを使う場合、64bit版UT Video Codecが必要

UT Video Codecは通常、次の場所へインストールされます。

```text
C:\Program Files\utvideo
```

32bit版だけをインストールしても、この64bitアプリからは利用できません。

## AVI圧縮形式

アプリはWindowsのVideo for Windows APIを使います。

- `無圧縮 AVI`: コーデック不要。ファイルサイズは大きくなります。
- `UtVideo RGB [ULRG]`: RGB画像向けです。
- `UtVideo RGBA [ULRA]`: アルファを保持する32bit入力です。
- その他のUT Video形式: インストール状況に応じて表示されます。

名称が一覧に存在するだけでは「使用可能」にしません。FourCCを圧縮モードで実際に開けた場合だけ「使用可能」と表示します。

## 出力ファイル名

入力ファイル名の接頭辞と圧縮形式から自動作成します。

例:

```text
frame_0001.png
↓
frame_uncompressed.avi
```

圧縮形式を使う場合は、FourCCとコーデック名がファイル名へ追加されます。

## ビルド方法

必要なもの:

- .NET 8 SDK
- Windows 11

通常のReleaseビルド:

```powershell
dotnet build -c Release
```

単体配布版の作成:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-single-exe.ps1
```

## ソース構成

| ファイル | 役割 |
|---|---|
| `Program.cs` | アプリを初期化して画面を起動します |
| `MainForm.cs` | 画面、入力、ドラッグ＆ドロップ、進捗を管理します |
| `SequenceSpec.cs` | ファイル名から連番、順番、欠番を判定します |
| `AviWriter.cs` | PNGをDIBへ変換し、Windows APIでAVIを書き出します |
| `PngSequenceAvi.csproj` | .NET、Windows Forms、高DPIなどのビルド設定です |
| `app.manifest` | Windowsでの実行権限を宣言します |
| `build-single-exe.ps1` | 単体配布版を作成します |
| `capture-current-ui.ps1` | 開発時にUIを画像で確認する補助ツールです |

## ユーザーが変更してよい箇所

比較的安全に変更できます。

- `MainForm.cs`先頭の色
- 画面の文言
- 余白、フォントサイズ、角丸半径
- エラーメッセージ、ログメッセージ
- `AviWriter.cs`の`knownFallbacks`にある既知コーデック候補
- 欠番表示の上限
- 出力exeの名前

変更後は必ずReleaseビルドを行い、アプリの起動と画面表示を確認してください。

## 変更不可、または専門知識が必要な箇所

次の箇所は、Windows API、メモリ、スレッドに関係します。目的と検証方法が明確でない限り変更しないでください。

- `AviWriter.cs`の`DllImport`宣言
- AVI用構造体のフィールド順、型、文字数
- `Marshal.AllocHGlobal`と`Marshal.FreeHGlobal`の対応
- `AVIFileInit`、`AVIFileExit`、各`Release`の対応
- DIBの4バイト境界計算
- `CopyBottomUp`の上下反転
- `MainForm.cs`の`InvokeRequired`処理
- `CancellationTokenSource`の生成、停止、破棄
- ComboBox一覧を角丸化するWindows API宣言
- `Program.cs`の`[STAThread]`
- `ApplicationConfiguration.Initialize()`
- `app.manifest`の実行権限

## コメントと変更履歴のルール

コメントは「コードを読めば分かること」の繰り返しではなく、保守時に必要な情報を書きます。

| 場所 | 書く内容 | 例 |
|---|---|---|
| 実装コード | `HOW`: どのように実現しているか | DIBを4バイト境界へ揃える方法 |
| コードコメント | `WHY NOT`: なぜ別の方法を採用しないか | UIスレッドで変換しない理由 |
| テストコード | `WHAT`: 何を保証するテストか | 欠番を12件まで表示すること |
| コミットメッセージ | `WHY`: なぜ変更したか | 高DPIで角が切れて見えるため |

### コメント記入のポイント

- 非エンジニアが読んでも対象と危険性が分かる文章にします。
- 単純な代入やメソッド名を日本語に言い換えただけのコメントは追加しません。
- 変更可能な箇所と変更不可の箇所を明記します。
- 数値には単位や制約を書きます。
- Windows APIの近くには、変更してはいけない理由を書きます。
- コメントと実際の処理が食い違った場合は、同じ変更でコメントも直します。

## Codex用覚書

Codexがこのプロジェクトを変更するときは、次を守ってください。

1. UI変更とAVI変換ロジックの変更を同時に行わない。
2. `bin`と`obj`を正しいソースとして編集しない。
3. 既存の日本語表示とUTF-8を維持する。
4. P/Invokeや構造体を整理目的で変更しない。
5. UIは高DPI環境で確認する。
6. プルダウンは文字サイズ、閉じた表示領域、展開項目高を確認する。
7. 変更後は`dotnet build -c Release`を実行する。
8. 配布版が必要な場合は`build-single-exe.ps1`を実行する。
9. 実装コードのコメントは`HOW`と`WHY NOT`を中心にする。
10. 新しくテストを追加する場合、テスト名またはコメントで`WHAT`を明確にする。

## テスト

現在、自動テストプロジェクトはありません。現時点では次の手動確認を行います。

1. アプリが起動する。
2. 連番PNGをドラッグ＆ドロップできる。
3. ファイル選択ボタンから複数PNGを選べる。
4. PNGが番号順に認識される。
5. 欠番が表示される。
6. 無圧縮AVIを作成できる。
7. 使用不可コーデックを選んだ場合に警告される。
8. 変換中に進捗が更新される。
9. 停止操作が動作する。
10. 100%、150%、175%などのDPIで角丸や文字が切れない。

将来テストコードを追加する場合は、「内部実装の方法」ではなく「利用者から見た保証内容（WHAT）」をテスト名またはコメントへ記載してください。

## トラブルシューティング

### PNGファイルが見つからない

- 拡張子が`.png`か確認してください。
- ファイル名末尾に数字があるか確認してください。
- ファイルが削除・移動されていないか確認してください。

### UT Videoが使用不可になる

- 64bit版がインストールされているか確認してください。
- アプリを再起動してください。
- 対象FourCCがWindowsへ登録されているか確認してください。

### 画像サイズが一致しない

連番内のすべてのPNGを同じ幅・高さにしてください。途中の画像だけサイズが異なる場合、変換を停止してエラーを表示します。

### 出力できない

- 出力先フォルダーへ書き込めるか確認してください。
- 同名AVIを別アプリで開いていないか確認してください。
- 空き容量を確認してください。特に無圧縮AVIは大きな容量を使います。

## UIデザイン参照

UIは次のデザインシステムを参考にしています。

```text
D:\Projects\Refine_PngSequence\design-system-example-components-html
```
#   P n g S e q u e n c e  
 #   P n g S e q u e n c e  
 