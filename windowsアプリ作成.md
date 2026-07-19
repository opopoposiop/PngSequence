# windowsアプリ作成

## アプリの機能

連番PNGファイルからApple ProRes 4444 MOVまたはOpenDML AVIを作成する。

動画作成には、アプリへ同梱したLGPL版FFmpegを使用する。

## 動作OS

Windows 11

## 実行ファイル

exeファイル単体起動

## ユーザーインターフェイス

- 画像ファイルのドロップエリア
- 動画出力先フォルダ
- 出力形式選択
- FPS入力
- 動作状況表示
- 停止ボタン
- 変換実行ボタン
- 変換進捗表示バー

## 動作の概略

1. 連番PNGファイルをドロップ
2. 出力形式を選択
   - Apple ProRes 4444 MOV
   - 無圧縮OpenDML AVI
   - UtVideo OpenDML AVI
3. MOVまたはAVIファイル作成

## UT Video Codec

FFmpeg内蔵のUtVideoエンコーダーを使用するため、Codecの別途インストールは不要。
