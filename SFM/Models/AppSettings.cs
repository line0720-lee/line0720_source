using System.Text.Json;
using System.Text.Json.Serialization;

namespace SFM.Models;

public class AppSettings
{
    // ── 검색 설정 ────────────────────────────────────────────
    /// <summary>true: 파일명 + 내용 동시 검색 / false: 파일명만 (빠른 모드)</summary>
    public bool EnableContentSearch { get; set; } = false;

    /// <summary>빈 배열 = 전체 확장자 허용. 예: [".txt", ".cs"]</summary>
    public string[] DefaultExtensions { get; set; } = [];

    public int MaxSearchResults { get; set; } = 200;

    // ── 인덱싱 설정 ──────────────────────────────────────────
    public string[] IndexRootPaths { get; set; } = [];

    /// <summary>true: 파일 내용까지 인덱싱 (DB 용량 증가) / false: 파일명·메타만</summary>
    public bool EnableContentIndexing { get; set; } = false;

    /// <summary>인덱싱에서 제외할 경로 목록 (접두사 일치)</summary>
    public string[] ExcludedPaths { get; set; } = [];

    /// <summary>내용 파싱 허용 최대 파일 크기 (MB). 초과 파일은 내용 미인덱싱.</summary>
    public int MaxFileSizeMB { get; set; } = 50;

    // ── UI 설정 ──────────────────────────────────────────────
    public int WindowWidth { get; set; } = 1024;
    public int WindowHeight { get; set; } = 680;

    /// <summary>-1이면 OS 기본(가운데) 배치.</summary>
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;

    /// <summary>true: 최대화 상태로 시작. 기본값 true.</summary>
    public bool WindowMaximized { get; set; } = true;

    /// <summary>기본값은 Windows 레지스트리 테마에서 자동 감지.</summary>
    public bool IsDarkMode { get; set; } = DetectSystemDarkMode();

    /// <summary>내용 검색 결과에 스니펫(발췌) 표시 여부.</summary>
    public bool ShowSnippetInResult { get; set; } = true;

    // ── 인프라 (직렬화 제외) ─────────────────────────────────
    [JsonIgnore]
    public string DatabasePath { get; } = Path.Combine(AppDataDir, "sfm_index.db");

    // ── 경로 상수 ────────────────────────────────────────────
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SFM");

    public static string SettingsFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SFM", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented      = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    // ── 영속화 메서드 ─────────────────────────────────────────

    /// <summary>
    /// 설정 파일을 읽어 반환한다.
    /// 파일이 없거나 파싱 실패 시 기본값 인스턴스를 반환한다.
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return new AppSettings();

            var json = File.ReadAllText(SettingsFilePath, System.Text.Encoding.UTF8);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>현재 설정을 %APPDATA%\SFM\settings.json 에 저장한다.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(SettingsFilePath, json, System.Text.Encoding.UTF8);
    }

    /// <summary>모든 필드를 코드 기본값으로 되돌린다.</summary>
    public void ResetToDefault()
    {
        EnableContentSearch  = false;
        DefaultExtensions    = [];
        MaxSearchResults     = 200;

        IndexRootPaths       = [];
        EnableContentIndexing = false;
        ExcludedPaths        = [];
        MaxFileSizeMB        = 50;

        WindowWidth          = 1024;
        WindowHeight         = 680;
        WindowX              = -1;
        WindowY              = -1;
        WindowMaximized      = true;
        IsDarkMode           = DetectSystemDarkMode();
        ShowSnippetInResult  = true;
    }

    // ── 내부 유틸 ────────────────────────────────────────────

    private static bool DetectSystemDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return (int?)key?.GetValue("AppsUseLightTheme") == 0;
        }
        catch
        {
            return false;
        }
    }
}
