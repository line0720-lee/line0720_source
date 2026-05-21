using System.Diagnostics;
using Microsoft.Data.Sqlite;
using SFM.Core;
using SFM.Data;
using SFM.Models;

namespace SFM.Tests;

/// <summary>
/// SFM 성능 테스트 콘솔 앱.
///
/// 테스트 항목:
///   1. 더미 파일 10만 건 배치 인덱싱 속도
///   2. 파일명 검색 10개 키워드 응답 속도
///   3. FTS5 내용 검색 10개 키워드 응답 속도
///   4. 메모리 사용량 측정
///   5. DB 파일 손상 복구 검증
///
/// 실행:
///   dotnet run --project SFM.Tests\SFM.Tests.csproj -c Release
/// </summary>
internal static class PerformanceTest
{
    // ── 공통 상수 ────────────────────────────────────────────
    private const int TotalFiles   = 100_000;
    private const int BatchSize    = 1_000;
    private const int ContentFiles = 10_000;   // FTS5 내용 인덱싱 대상 (전체의 10%)
    private const int MaxResults   = 200;

    private static readonly string[] Extensions =
        [".txt", ".cs", ".py", ".pdf", ".docx", ".xlsx", ".json", ".log", ".md", ".xml"];

    private static readonly string[] SearchKeywords =
        ["report", "meeting", "2024", "src", "config", "api", "data", "main", "index", "test"];

    private static readonly string[] ContentKeywords =
        ["function", "class", "import", "return", "error", "select", "update", "create", "delete", "insert"];

    private static string _dbPath = string.Empty;

    // ════════════════════════════════════════════════════════
    // Main
    // ════════════════════════════════════════════════════════
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.CursorVisible  = false;

        PrintHeader();

        _dbPath = Path.Combine(Path.GetTempPath(), "sfm_perf_test.db");

        try
        {
            CleanupTestDb();

            using var db = new DatabaseManager(_dbPath);
            db.Initialize();

            var results = new List<TestResult>();

            results.Add(await Test1_BulkInsert(db));
            results.Add(await Test2_ContentInsert(db));
            results.Add(await Test3_NameSearch(db));
            results.Add(await Test4_ContentSearch(db));
            results.Add(      Test5_MemoryUsage(db));
            results.Add(      Test6_CorruptionRecovery());

            PrintSummary(results);
        }
        finally
        {
            Console.CursorVisible = true;
            CleanupTestDb();
        }
    }

    // ════════════════════════════════════════════════════════
    // Test 1: 파일 메타 10만 건 배치 인서트
    // ════════════════════════════════════════════════════════
    static async Task<TestResult> Test1_BulkInsert(DatabaseManager db)
    {
        Print("\n[TEST 1] 파일 메타 10만 건 배치 인덱싱");
        Print("────────────────────────────────────────────────");

        var rand      = new Random(42);
        var sw        = Stopwatch.StartNew();
        long memBefore = GC.GetTotalMemory(forceFullCollection: true);
        int  inserted  = 0;

        for (int bStart = 0; bStart < TotalFiles; bStart += BatchSize)
        {
            int bEnd = Math.Min(bStart + BatchSize, TotalFiles);
            using var tx = db.BeginTransaction();
            try
            {
                for (int i = bStart; i < bEnd; i++)
                {
                    var ext  = Extensions[rand.Next(Extensions.Length)];
                    var file = new FileIndex
                    {
                        FilePath    = $@"C:\PerfTest\dir_{i / 1000:D3}\file_{i:D6}{ext}",
                        FileName    = $"file_{i:D6}{ext}",
                        Extension   = ext,
                        FileSize    = rand.Next(512, 50 * 1024 * 1024),
                        ModifiedAt  = DateTime.UtcNow.AddDays(-rand.Next(0, 730)),
                        IndexedAt   = DateTime.UtcNow,
                        ContentHash = $"HASH_{i:X8}",
                    };
                    db.InsertOrUpdateFile(file, tx);
                    inserted++;
                }
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }

            if (bStart % 10_000 == 0)
                Console.Write($"\r  진행: {inserted:N0}/{TotalFiles:N0}건");
        }

        sw.Stop();
        long memAfter = GC.GetTotalMemory(forceFullCollection: false);
        var  dbSize   = GetDbSizeKB();

        Print($"\r  완료: {inserted:N0}건{new string(' ', 20)}");
        Print($"  소요 시간:  {sw.ElapsedMilliseconds:N0} ms");
        Print($"  처리 속도:  {inserted / sw.Elapsed.TotalSeconds:N0} 건/초");
        Print($"  메모리 증가: {Math.Max(0, memAfter - memBefore) / 1024.0:N1} KB");
        Print($"  DB 크기:   {dbSize:N1} KB");

        bool pass = sw.ElapsedMilliseconds < 30_000;   // 30초 이내
        return new TestResult("배치 인덱싱 (10만건)", sw.Elapsed, pass,
            $"{inserted / sw.Elapsed.TotalSeconds:N0} 건/초");
    }

    // ════════════════════════════════════════════════════════
    // Test 2: FTS5 내용 인덱싱 1만 건
    // ════════════════════════════════════════════════════════
    static async Task<TestResult> Test2_ContentInsert(DatabaseManager db)
    {
        Print("\n[TEST 2] FTS5 내용 인덱싱 1만 건");
        Print("────────────────────────────────────────────────");

        // file_index 에서 처음 ContentFiles 건 조회
        var allFiles  = db.GetAllIndexedFiles();
        var targets   = allFiles.Take(ContentFiles).ToList();
        var rand      = new Random(123);
        var sw        = Stopwatch.StartNew();
        int inserted  = 0;
        const int CBatch = 500;

        for (int bStart = 0; bStart < targets.Count; bStart += CBatch)
        {
            int bEnd = Math.Min(bStart + CBatch, targets.Count);
            using var tx = db.BeginTransaction();
            try
            {
                for (int i = bStart; i < bEnd; i++)
                {
                    var file    = targets[i];
                    var content = GenerateFakeContent(rand, 500);   // 500단어 더미 텍스트
                    db.InsertOrUpdateContent(file.Id, content, tx);
                    inserted++;
                }
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }

            Console.Write($"\r  진행: {inserted:N0}/{targets.Count:N0}건");
        }

        sw.Stop();
        Print($"\r  완료: {inserted:N0}건{new string(' ', 20)}");
        Print($"  소요 시간:  {sw.ElapsedMilliseconds:N0} ms");
        Print($"  처리 속도:  {inserted / sw.Elapsed.TotalSeconds:N0} 건/초");
        Print($"  DB 크기:   {GetDbSizeKB():N1} KB");

        bool pass = sw.ElapsedMilliseconds < 60_000;   // 60초 이내
        return new TestResult("FTS5 내용 인덱싱 (1만건)", sw.Elapsed, pass,
            $"{inserted / sw.Elapsed.TotalSeconds:N0} 건/초");
    }

    // ════════════════════════════════════════════════════════
    // Test 3: 파일명 검색 10개 키워드
    // ════════════════════════════════════════════════════════
    static async Task<TestResult> Test3_NameSearch(DatabaseManager db)
    {
        Print("\n[TEST 3] 파일명 검색 — 키워드 10개");
        Print("────────────────────────────────────────────────");

        long totalMs = 0;
        var  results = new List<(string Keyword, int Count, long Ms)>();

        foreach (var kw in SearchKeywords)
        {
            var sw   = Stopwatch.StartNew();
            var hits = db.SearchByNameFiltered(
                likePattern : $"%{kw}%",
                extensions  : [],
                minSize     : null, maxSize : null,
                after       : null, before  : null,
                limit       : MaxResults);
            sw.Stop();
            totalMs += sw.ElapsedMilliseconds;
            results.Add((kw, hits.Count, sw.ElapsedMilliseconds));
        }

        foreach (var (kw, cnt, ms) in results)
            Print($"  [{ms,4} ms] \"{kw}\" → {cnt,5}건");

        double avg = totalMs / (double)SearchKeywords.Length;
        Print($"\n  평균: {avg:F1} ms / 쿼리  ← 목표 1000 ms 이내");

        bool pass = avg < 1_000;
        return new TestResult($"파일명 검색 ({SearchKeywords.Length}개 키워드)",
            TimeSpan.FromMilliseconds(totalMs), pass, $"평균 {avg:F1} ms");
    }

    // ════════════════════════════════════════════════════════
    // Test 4: FTS5 내용 검색 10개 키워드
    // ════════════════════════════════════════════════════════
    static async Task<TestResult> Test4_ContentSearch(DatabaseManager db)
    {
        Print("\n[TEST 4] FTS5 내용 검색 — 키워드 10개");
        Print("────────────────────────────────────────────────");

        long totalMs = 0;
        var  results = new List<(string Keyword, int Count, long Ms)>();

        foreach (var kw in ContentKeywords)
        {
            var sw   = Stopwatch.StartNew();
            var hits = db.SearchByContentFiltered(
                ftsQuery   : $"\"{kw}\"",
                extensions : [],
                minSize    : null, maxSize : null,
                after      : null, before  : null,
                limit      : MaxResults);
            sw.Stop();
            totalMs += sw.ElapsedMilliseconds;
            results.Add((kw, hits.Count, sw.ElapsedMilliseconds));
        }

        foreach (var (kw, cnt, ms) in results)
            Print($"  [{ms,4} ms] \"{kw}\" → {cnt,5}건");

        double avg = totalMs / (double)ContentKeywords.Length;
        Print($"\n  평균: {avg:F1} ms / 쿼리  ← 목표 1000 ms 이내");

        bool pass = avg < 1_000;
        return new TestResult($"FTS5 내용 검색 ({ContentKeywords.Length}개 키워드)",
            TimeSpan.FromMilliseconds(totalMs), pass, $"평균 {avg:F1} ms");
    }

    // ════════════════════════════════════════════════════════
    // Test 5: 메모리 사용량
    // ════════════════════════════════════════════════════════
    static TestResult Test5_MemoryUsage(DatabaseManager db)
    {
        Print("\n[TEST 5] 메모리 사용량");
        Print("────────────────────────────────────────────────");

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        var proc     = Process.GetCurrentProcess();
        proc.Refresh();

        long gcBytes = GC.GetTotalMemory(forceFullCollection: false);
        long wsBytes = proc.WorkingSet64;
        long pvBytes = proc.PrivateMemorySize64;

        Print($"  GC 힙 메모리:     {gcBytes / 1024.0 / 1024:F1} MB");
        Print($"  Working Set:      {wsBytes / 1024.0 / 1024:F1} MB");
        Print($"  Private Bytes:    {pvBytes / 1024.0 / 1024:F1} MB");
        Print($"  DB 크기:          {GetDbSizeKB() / 1024:F1} MB");

        bool pass = wsBytes < 512L * 1024 * 1024; // 512 MB 이내
        return new TestResult("메모리 사용량", TimeSpan.Zero, pass,
            $"WS {wsBytes / 1024.0 / 1024:F1} MB");
    }

    // ════════════════════════════════════════════════════════
    // Test 6: DB 손상 복구
    // ════════════════════════════════════════════════════════
    static TestResult Test6_CorruptionRecovery()
    {
        Print("\n[TEST 6] DB 손상 자동 복구");
        Print("────────────────────────────────────────────────");

        var testDb = Path.Combine(Path.GetTempPath(), "sfm_corrupt_test.db");
        bool passed = false;

        try
        {
            // 정상 DB 생성
            using (var db = new DatabaseManager(testDb))
                db.Initialize();

            // DB 파일을 쓰레기 데이터로 덮어써 손상 시뮬레이션
            File.WriteAllText(testDb, "THIS IS NOT A VALID SQLITE DATABASE FILE !!!");
            Print("  DB 파일 손상 시뮬레이션 완료");

            // 손상된 DB로 Initialize → 자동 복구 기대
            using (var db = new DatabaseManager(testDb))
            {
                db.Initialize();    // ← 여기서 복구 발생
                var stats = db.GetIndexStats();
                passed = stats.TotalFiles == 0;  // 새 DB이므로 0건
            }

            Print(passed
                ? "  자동 복구 성공 ✓  (새 DB 생성, 0건)"
                : "  복구 시도했으나 예상 결과 아님 ✗");
        }
        catch (Exception ex)
        {
            Print($"  예외 발생 ✗: {ex.Message}");
        }
        finally
        {
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), "sfm_corrupt_test*"))
                try { File.Delete(f); } catch { }
        }

        return new TestResult("DB 손상 자동 복구", TimeSpan.Zero, passed,
            passed ? "복구 성공" : "복구 실패");
    }

    // ════════════════════════════════════════════════════════
    // 헬퍼
    // ════════════════════════════════════════════════════════

    private static readonly string[] FakeWords =
    [
        "function", "class", "import", "return", "error", "select", "update",
        "create", "delete", "insert", "public", "private", "static", "async",
        "await", "string", "int", "bool", "var", "const", "let", "if", "else",
        "for", "while", "try", "catch", "finally", "throw", "new", "null",
        "true", "false", "void", "object", "list", "dict", "array", "type",
        "interface", "enum", "struct", "abstract", "override", "virtual",
        "report", "meeting", "data", "config", "api", "main", "index", "test",
        "user", "admin", "server", "client", "request", "response", "token",
        "file", "path", "name", "value", "key", "id", "code", "message",
    ];

    /// <summary>지정된 단어 수로 구성된 가짜 텍스트를 생성한다.</summary>
    private static string GenerateFakeContent(Random rand, int words)
    {
        var sb = new System.Text.StringBuilder(words * 8);
        for (int i = 0; i < words; i++)
        {
            if (i > 0) sb.Append(i % 15 == 0 ? '\n' : ' ');
            sb.Append(FakeWords[rand.Next(FakeWords.Length)]);
        }
        return sb.ToString();
    }

    private static double GetDbSizeKB()
    {
        var fi = new FileInfo(_dbPath);
        return fi.Exists ? fi.Length / 1024.0 : 0;
    }

    private static void CleanupTestDb()
    {
        foreach (var suffix in (string[])["", "-wal", "-shm"])
        {
            var path = _dbPath + suffix;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    private static void Print(string msg) => Console.WriteLine(msg);

    private static void PrintHeader()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine("  SFM Performance & Reliability Test");
        Console.WriteLine($"  실행 시각: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine("═══════════════════════════════════════════════════════");
    }

    private static void PrintSummary(List<TestResult> results)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════════");
        Console.WriteLine("  결과 요약");
        Console.WriteLine("═══════════════════════════════════════════════════════");

        int passed = 0;
        foreach (var r in results)
        {
            string status = r.Pass ? "✓ PASS" : "✗ FAIL";
            string time   = r.Elapsed > TimeSpan.Zero
                ? $"  [{r.Elapsed.TotalMilliseconds:N0} ms]"
                : string.Empty;
            Console.WriteLine($"  {status}  {r.Name,-35}{time}  {r.Detail}");
            if (r.Pass) passed++;
        }

        Console.WriteLine("───────────────────────────────────────────────────────");
        Console.WriteLine($"  합격: {passed} / {results.Count}");
        Console.WriteLine("═══════════════════════════════════════════════════════");
    }

    // ════════════════════════════════════════════════════════
    // 결과 레코드
    // ════════════════════════════════════════════════════════
    private record TestResult(string Name, TimeSpan Elapsed, bool Pass, string Detail);
}
