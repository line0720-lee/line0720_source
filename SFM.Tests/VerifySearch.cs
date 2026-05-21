using System.Diagnostics;
using SFM.Core;
using SFM.Data;
using SFM.Models;

namespace SFM.Tests;

/// <summary>
/// 실제 운영 DB로 인덱싱 → 검색 동작 검증.
/// </summary>
internal static class VerifySearch
{
    public static async Task RunAsync()
    {
        Console.WriteLine("=== SFM 검색 동작 검증 ===\n");

        // ── 설정 로드 ─────────────────────────────────────────
        var settings = AppSettings.Load();

        // 인덱싱 경로가 없으면 기본 경로 자동 설정
        if (settings.IndexRootPaths.Length == 0)
        {
            var paths = new List<string>();
            foreach (var special in new[]
            {
                Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.MyDocuments,
            })
            {
                var p = Environment.GetFolderPath(special);
                if (Directory.Exists(p)) paths.Add(p);
            }

            var downloads = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloads)) paths.Add(downloads);

            // 소스코드가 있는 Study 폴더 추가 (검색 테스트용)
            var study = @"C:\Claude(바이브코딩)\Study";
            if (Directory.Exists(study)) paths.Add(study);

            settings.IndexRootPaths = paths.ToArray();
            settings.Save();

            Console.WriteLine("[설정] 인덱싱 경로 자동 설정:");
            foreach (var p in settings.IndexRootPaths)
                Console.WriteLine($"  · {p}");
            Console.WriteLine();
        }
        else
        {
            Console.WriteLine("[설정] 기존 인덱싱 경로:");
            foreach (var p in settings.IndexRootPaths)
                Console.WriteLine($"  · {p}");
            Console.WriteLine();
        }

        // ── DB 초기화 ─────────────────────────────────────────
        var db = new DatabaseManager(settings.DatabasePath);
        db.Initialize();

        var statsBefore = db.GetIndexStats();
        Console.WriteLine($"[DB] 인덱싱 전: {statsBefore.TotalFiles:N0}개 파일, {statsBefore.DatabaseSizeMb:F1} MB\n");

        // ── 인덱싱 실행 ──────────────────────────────────────
        var indexer  = new Indexer(db, new FileParser(), settings);
        var sw       = Stopwatch.StartNew();
        int progress = 0;

        indexer.IndexProgressChanged += p =>
        {
            progress = p.Processed;
            if (p.Processed % 100 == 0 || p.Processed == p.Total)
                Console.Write($"\r  인덱싱 중: {p.Processed:N0}/{p.Total:N0} ({p.Phase})   ");
        };
        indexer.IndexErrorOccurred += e =>
            Console.WriteLine($"\n  [오류] {Path.GetFileName(e.FilePath)}: {e.Message}");

        Console.WriteLine("[인덱싱] 시작...");
        await indexer.BuildIndexAsync(settings.IndexRootPaths);
        sw.Stop();
        Console.WriteLine($"\n[인덱싱] 완료: {sw.Elapsed.TotalSeconds:F1}초\n");

        // ── 인덱싱 후 통계 ───────────────────────────────────
        var statsAfter = db.GetIndexStats();
        Console.WriteLine($"[DB] 인덱싱 후: {statsAfter.TotalFiles:N0}개 파일, {statsAfter.DatabaseSizeMb:F1} MB");

        if (statsAfter.TotalFiles == 0)
        {
            Console.WriteLine("\n[경고] 인덱싱된 파일이 없습니다!");
            Console.WriteLine("  - 인덱싱 경로를 확인하세요.");
            Console.WriteLine("  - 숨김·바이너리 파일은 제외됩니다.");
            db.Dispose();
            return;
        }

        // ── 검색 검증 ─────────────────────────────────────────
        var searcher  = new Searcher(db);
        var keywords  = new List<string>();

        // 실제 파일명에서 검색 키워드 뽑기
        var sample = db.SearchByNameFiltered("%", [], null, null, null, null, 20, 0);
        foreach (var f in sample.Take(5))
        {
            var name = Path.GetFileNameWithoutExtension(f.FileName);
            if (name.Length >= 2) keywords.Add(name[..Math.Min(4, name.Length)]);
        }
        if (keywords.Count == 0) keywords.AddRange(["SFM", "main", "test"]);

        Console.WriteLine($"\n[검색] 키워드 {keywords.Count}개로 검증:");
        Console.WriteLine(new string('─', 70));

        bool anyFound = false;
        foreach (var kw in keywords.Distinct().Take(5))
        {
            var query = new SearchQuery
            {
                Keyword    = kw,
                MaxResults = 10,
            };
            var results = await searcher.SearchAsync(query);
            var status  = results.Count > 0 ? $"✔  {results.Count}개" : "✘  0개";
            Console.WriteLine($"  \"{kw,-15}\" → {status}");
            if (results.Count > 0)
            {
                anyFound = true;
                Console.WriteLine($"     첫 번째: {results[0].FileName}  ({Path.GetDirectoryName(results[0].FilePath)})");
            }
        }

        Console.WriteLine(new string('─', 70));
        Console.WriteLine($"\n[결과] {(anyFound ? "✔ 검색 정상 동작!" : "✘ 검색 결과 없음 — 인덱싱 경로 재확인 필요")}");

        db.Dispose();
    }
}
