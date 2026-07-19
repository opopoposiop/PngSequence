using System.Windows.Forms;

namespace PngSequenceAvi;

/*
 * アプリケーションの開始地点です。
 *
 * ユーザーが変更してよい箇所:
 * - 通常はありません。起動時に別の画面を表示したい場合のみ MainForm を変更します。
 *
 * 変更不可の箇所:
 * - [STAThread] と ApplicationConfiguration.Initialize() は削除しないでください。
 *   ファイル選択画面やWindowsのUI部品が正しく動かなくなる可能性があります。
 *
 * Codex用覚書:
 * - HOW: Windows Formsの初期設定を済ませてからMainFormを1つ起動します。
 * - WHY NOT: MainFormを直接newするだけの独自起動処理へ置き換えません。
 *   高DPI設定や既定フォントなど、.NET 8の初期化が抜けるためです。
 */
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
