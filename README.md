# PNG Sequence Video Forge

連番PNGを読み込み、Apple ProRes 4444 MOVまたはOpenDML AVIへ変換するWindowsアプリです。

## v1.3.2の変更

- 高DPI環境で出力形式のコーデック名が上下に見切れる問題を修正
- フォーム表示後の実DPIと描画デバイスから文字高を再計測
- DPI変更時にも候補行、選択欄、バッジ、余白を再計算
- ComboBox本体の枠・ボタンと外側の角丸枠を含む推奨高を選択欄へ反映
- 168 DPI（175%）で選択中のコーデック名と候補名がすべて表示されることを確認

## v1.3.1の変更

- 出力形式の選択欄を固定48pxから「フォント実寸＋上下余白」の高さへ変更
- プルダウン候補行を固定30pxからフォント実寸に合わせて自動調整
- 右側の「同梱」バッジを最大20pxに縮小し、文字とバッジの高さを分離
- WindowsのDPI設定やフォントサイズに応じて高さを再計算

## v1.3.0の主な変更

- Apple ProRes 4444 MOVを出力形式の1番目に追加
- 無圧縮OpenDML AVIを2番目に配置
- AVIの作成をWindows AVIFile APIからFFmpegのAVI muxerへ移行
- 4GBを超えるAVIを分割せず、OpenDML対応の単一ファイルとして出力
- UtVideoエンコーダーをFFmpegに同梱し、外部Codecのインストールを不要化
- 出力形式プルダウンの項目高を文字サイズに合わせて調整

## 出力形式

プルダウンには次の順で表示されます。

1. Apple ProRes 4444 MOV
2. 無圧縮 AVI (OpenDML)
3. UtVideo RGB AVI (OpenDML)
4. UtVideo RGBA AVI (OpenDML)
5. UtVideo YUV420 BT.601 AVI (OpenDML)
6. UtVideo YUV422 BT.601 AVI (OpenDML)
7. UtVideo YUV420 BT.709 AVI (OpenDML)
8. UtVideo YUV422 BT.709 AVI (OpenDML)

ProRes 4444は`prores_ks`、profile 4、`yuva444p10le`、16bitアルファで出力します。RGBA PNGのアルファチャンネルを保持できます。

AVIはFFmpegのOpenDML対応AVI muxerで作成します。従来のRIFF AVIで問題になっていた約4GBの境界を越えても、自動分割せず1つの`.avi`として保存します。必要な空き容量と、保存先ファイルシステムの最大ファイルサイズには注意してください。FAT32には4GBを超えるファイルを保存できません。

## 必要環境

- Windows 11 64bit
- 配布版の実行には.NETやCodecの追加インストール不要
- ソースからのビルドには.NET 8 SDKとPowerShell
- FFmpeg取得時はインターネット接続

FFmpegは単一EXE内に埋め込まれ、最初の変換時に`%LOCALAPPDATA%\PngSequenceAvi\ffmpeg`以下へ整合性を確認して展開されます。アプリが外部の`ffmpeg.exe`を探索することはありません。

## 使い方

1. `PngSequenceAvi.exe`を起動します。
2. 連番PNGまたはPNGが入ったフォルダーをドロップします。
3. 動画出力先フォルダーを選択します。
4. フレームレートを入力します。
5. 出力形式を選択します。
6. 「動画に変換」をクリックします。

連番ファイル名の例:

```text
frame_0001.png
frame_0002.png
frame_0003.png
```

ファイル名末尾の番号を使用して並べ替えます。

## ビルド

通常のReleaseビルド:

```powershell
powershell -ExecutionPolicy Bypass -File .\prepare-ffmpeg.ps1
dotnet build -c Release
```

単一実行ファイル:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-single-exe.ps1
```

`prepare-ffmpeg.ps1`は固定したBtbN LGPLビルドを取得し、アーカイブと`ffmpeg.exe`のSHA-256を検証します。生成物:

```text
bin\Release\net8.0-windows\win-x64\publish\PngSequenceAvi.exe
```

## FFmpegとライセンス

同梱物はBtbN FFmpeg BuildsのLGPL版です。固定バージョン、取得元、SHA-256、ソース入手先は[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)に記載しています。ライセンス全文は[FFMPEG-LICENSE.txt](FFMPEG-LICENSE.txt)を参照してください。

## ソース構成

| ファイル | 役割 |
|---|---|
| `Program.cs` | アプリケーションの起動 |
| `MainForm.cs` | UI、入力、ドラッグ＆ドロップ、変換進捗 |
| `SequenceSpec.cs` | PNGの連番解析 |
| `VideoWriter.cs` | FFmpeg起動、PNGパイプ入力、MOV/OpenDML AVI出力 |
| `prepare-ffmpeg.ps1` | 固定FFmpegの取得・SHA-256検証 |
| `build-single-exe.ps1` | 圧縮済み単一実行ファイルの作成 |
| `PngSequenceAvi.csproj` | .NETとFFmpeg埋め込みのビルド設定 |

## テスト項目

変更後は次を確認します。

1. アプリが起動し、出力形式が上記の順で表示される
2. PNGフォルダーと複数PNGを読み込める
3. ProRes 4444 MOVを作成でき、アルファを保持する
4. 無圧縮AVIと各UtVideo AVIを作成できる
5. 4GBを超えるAVIがOpenDML単一ファイルとして完了する
6. 進捗表示と停止操作が動作する
7. 100%、150%、175%のDPIで表示が崩れない

## トラブルシューティング

### PNGが見つからない

- 拡張子が`.png`であることを確認してください。
- ファイル名末尾に数字があることを確認してください。
- すべての画像サイズが同じであることを確認してください。

### 変換できない

- 出力先の書き込み権限と空き容量を確認してください。
- 同名のMOV/AVIを別アプリで開いていないか確認してください。
- 4GBを超える場合、保存先がNTFSまたはexFATであることを確認してください。
- セキュリティソフトが`%LOCALAPPDATA%\PngSequenceAvi\ffmpeg`の実行を遮断していないか確認してください。

## UIデザイン

UIは[デジタル庁デザインシステム](https://design.digital.go.jp/dads/)を参考にしています。
