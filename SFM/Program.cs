using SFM.UI;

namespace SFM;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // ── 단일 인스턴스 체크 (중복 실행 방지) ──────────────────
        using var mutex = new System.Threading.Mutex(true, "SFM_SmartFileManager_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // 이미 실행 중 → 기존 창을 앞으로 가져오고 종료
            BringExistingWindowToFront();
            return;
        }

        // 글로벌 예외 핸들러 — 스택 트레이스를 stderr + 로그파일에 기록
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("UnhandledException", e.ExceptionObject as Exception);

        Application.ThreadException += (_, e) =>
            LogCrash("ThreadException", e.Exception);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        ApplicationConfiguration.Initialize();

        // ── 스플래시 화면: MainForm 생성/로드 전에 즉시 표시 ──
        using var splash = new SFM.UI.SplashForm();
        splash.Show();
        Application.DoEvents();   // 스플래시 즉시 렌더링

        var mainForm = new SFM.UI.MainForm(splash);
        Application.Run(mainForm);
    }

    /// <summary>이미 실행 중인 SFM 창을 최상위로 가져온다.</summary>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static void BringExistingWindowToFront()
    {
        var current = System.Diagnostics.Process.GetCurrentProcess();
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
        {
            if (p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero)
            {
                ShowWindow(p.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(p.MainWindowHandle);
                return;
            }
        }
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
