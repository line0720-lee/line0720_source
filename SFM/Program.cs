using SFM.UI;

namespace SFM;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // 글로벌 예외 핸들러 — 스택 트레이스를 stderr + 로그파일에 기록
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("UnhandledException", e.ExceptionObject as Exception);

        Application.ThreadException += (_, e) =>
            LogCrash("ThreadException", e.Exception);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private static void LogCrash(string kind, Exception? ex)
    {
        var msg  = ex?.ToString() ?? "(null)";
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}:\n{msg}\n";
        var log  = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SFM", "crash.log");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.AppendAllText(log, line);
        }
        catch { /* 로그 저장 실패는 무시 */ }

        Console.Error.WriteLine(line);

        MessageBox.Show(
            $"오류가 발생했습니다.\n\n{ex?.Message}\n\n로그 파일: {log}",
            "SFM 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
