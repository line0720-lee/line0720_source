using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using SFM.Data;
using SFM.Models;

namespace SFM.Core;

/// <summary>
/// 파일 시스템을 탐색하여 DB 인덱스를 구축·갱신한다.
/// 이벤트는 백그라운드 스레드에서 발생하므로 UI 구독 시 Invoke 필요.
/// </summary>
public sealed class Indexer
{
    // ─────────────────────────────────────────────────────────
    // 이벤트 데이터 타입
    // ─────────────────────────────────────────────────────────

    /// <summary>파일 하나를 처리할 때마다 전달되는 진행 정보.</summary>
    public record IndexProgress(string FilePath, int Processed, int Total, string Phase);

    /// <summary>작업 완료 시 전달되는 통계.</summary>
    public record IndexResult(int TotalIndexed, int Added, int Updated, int Deleted, TimeSpan Elapsed);

    /// <summary>개별 파일 처리 오류 정보.</summary>
    public record IndexError(string FilePath, string Message);

    // ─────────────────────────────────────────────────────────
    // 이벤트
    // ─────────────────────────────────────────────────────────

    /// <summary>파일 처리 완료마다 발생. 백그라운드 스레드 → UI Invoke 필요.</summary>
    public event Action<IndexProgress>? IndexProgressChanged;

    /// <summary>인덱싱 작업 전체 완료 시 발생.</summary>
    public event Action<IndexResult>? IndexCompleted;

    /// <summary>파일 단위 오류 발생 시 발생. 작업은 중단되지 않고 계속된다.</summary>
    public event Action<IndexError>? IndexErrorOccurred;

    // ─────────────────────────────────────────────────────────
    // 정적 제외 목록 (프로세스 수명 동안 불변)
    // ─────────────────────────────────────────────────────────

    private static readonly HashSet<string> _systemExcludedPaths;

    /// <summary>내용 파싱 없이 파일명도 인덱싱하지 않을 바이너리 확장자.</summary>
    private static readonly HashSet<string> _binaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".sys", ".com", ".scr",
        ".msi", ".pdb", ".lib", ".obj", ".bin",
    };

    static Indexer()
    {
        _systemExcludedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddExcluded(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        AddExcluded(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddExcluded(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddExcluded(Path.GetTempPath());

        static void AddExcluded(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                _systemExcludedPaths.Add(NormDir(path));
        }
    }

    // ─────────────────────────────────────────────────────────
    // 의존성
    // ─────────────────────────────────────────────────────────

    private readonly DatabaseManager _db;
    private readonly FileParser _parser;
    private readonly AppSettings _settings;

    public Indexer(DatabaseManager db, FileParser parser, AppSettings settings)
    {
        _db       = db;
        _parser   = parser;
        _settings = settings;
    }

    // ─────────────────────────────────────────────────────────
    // 1. BuildIndexAsync — 전체 재인덱싱
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 지정한 루트 경로를 재귀 탐색하여 모든 파일을 인덱싱한다.
    /// 기존 레코드는 InsertOrUpdate 로 갱신되므로 먼저 삭제할 필요 없다.
    /// </summary>
    /// <summary>
    /// 지정한 루트 경로를 재귀 탐색하여 모든 파일을 인덱싱한다.
    /// 1000건 단위 배치 트랜잭션으로 처리하여 단건 커밋 대비 수십 배 빠른 속도를 제공한다.
    /// </summary>
    public async Task BuildIndexAsync(string[] rootPaths, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        int indexed = 0, errors = 0;
        const int BatchSize = 1_000;

        var files = CollectFiles(rootPaths).ToList();
        int total  = files.Count;

        for (int batchStart = 0; batchStart < files.Count; batchStart += BatchSize)
        {
            ct.ThrowIfCancellationRequested();
            int batchEnd = Math.Min(batchStart + BatchSize, files.Count);

            using var tx = _db.BeginTransaction();
            try
            {
                for (int i = batchStart; i < batchEnd; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var path = files[i];

                    try
                    {
                        await IndexCoreAsync(path, tx);
                        indexed++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        RaiseError(path, ex);
                    }

                    RaiseProgress(path, indexed + errors, total, "빌드");
                }
                tx.Commit();
            }
            catch (OperationCanceledException)
            {
                tx.Rollback();
                throw;
            }
            catch
            {
                // 배치 단위 오류: 다음 배치로 계속 진행
                tx.Rollback();
            }
        }

        sw.Stop();
        IndexCompleted?.Invoke(new IndexResult(indexed, indexed, 0, 0, sw.Elapsed));
    }

    // ─────────────────────────────────────────────────────────
    // 2. UpdateIndexAsync — 증분 업데이트
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 기존 인덱스와 실제 파일을 비교하여 변경분만 처리한다.
    /// <list type="bullet">
    /// <item>디스크에서 삭제된 파일 → DB 에서도 삭제</item>
    /// <item>새로 생긴 파일 → DB 에 추가</item>
    /// <item>content_hash 가 바뀐 파일 → 재파싱 후 업데이트</item>
    /// <item>hash 동일 → 건너뜀</item>
    /// </list>
    /// </summary>
    public async Task UpdateIndexAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        int added = 0, updated = 0, deleted = 0, errors = 0;

        // DB 현재 인덱스: 경로 → FileIndex (hash 포함)
        var indexed = _db.GetAllIndexedFiles()
            .ToDictionary(f => f.FilePath, StringComparer.OrdinalIgnoreCase);

        // 디스크 현재 파일 목록
        var diskFiles = CollectFiles(_settings.IndexRootPaths)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int totalOps  = indexed.Count + diskFiles.Count;
        int processed = 0;

        // ── ① 삭제된 파일: DB 에 있지만 디스크에 없음 ────────
        var toDelete = indexed.Keys.Except(diskFiles, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var path in toDelete)
        {
            ct.ThrowIfCancellationRequested();
            try   { _db.DeleteFile(path); deleted++; }
            catch (Exception ex) { errors++; RaiseError(path, ex); }
            RaiseProgress(path, ++processed, totalOps, "삭제");
        }

        // ── ② 새 파일 추가 + 변경 파일 재인덱싱 (1000건 배치 트랜잭션) ──
        const int UpdateBatch = 1_000;
        var diskFilesList = diskFiles.ToList();

        for (int batchStart = 0; batchStart < diskFilesList.Count; batchStart += UpdateBatch)
        {
            ct.ThrowIfCancellationRequested();
            int batchEnd = Math.Min(batchStart + UpdateBatch, diskFilesList.Count);

            using var batchTx = _db.BeginTransaction();
            try
            {
                for (int bi = batchStart; bi < batchEnd; bi++)
                {
                    ct.ThrowIfCancellationRequested();
                    var path = diskFilesList[bi];

                    try
                    {
                        var info    = new FileInfo(path);
                        var newHash = ComputeFileHash(info);

                        if (!indexed.TryGetValue(path, out var existing))
                        {
                            await IndexCoreAsync(path, batchTx);
                            added++;
                        }
                        else if (!string.Equals(existing.ContentHash, newHash, StringComparison.Ordinal))
                        {
                            await IndexCoreAsync(path, batchTx);
                            updated++;
                        }
                        // else: 변경 없음 → 건너뜀
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        RaiseError(path, ex);
                    }

                    RaiseProgress(path, ++processed, totalOps, "업데이트");
                }
                batchTx.Commit();
            }
            catch (OperationCanceledException) { batchTx.Rollback(); throw; }
            catch { batchTx.Rollback(); }
        }

        sw.Stop();
        IndexCompleted?.Invoke(new IndexResult(added + updated, added, updated, deleted, sw.Elapsed));
    }

    // ─────────────────────────────────────────────────────────
    // 3. IndexSingleFileAsync — 단일 파일 즉시 인덱싱
    // ─────────────────────────────────────────────────────────

    /// <summary>단일 파일을 즉시 인덱싱한다. FileWatcher 이벤트 등 실시간 감지에 사용.</summary>
    public async Task IndexSingleFileAsync(string filePath)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var info = new FileInfo(filePath);

            if (!info.Exists)
            {
                RaiseError(filePath, new FileNotFoundException("파일이 존재하지 않습니다."));
                return;
            }
            if (ShouldExcludeFile(info)) return;

            await IndexCoreAsync(filePath);
            sw.Stop();
            IndexCompleted?.Invoke(new IndexResult(1, 1, 0, 0, sw.Elapsed));
        }
        catch (Exception ex)
        {
            RaiseError(filePath, ex);
        }
    }

    /// <summary>파일 인덱스(메타 + 내용)를 DB 에서 삭제한다.</summary>
    public void RemoveFile(string filePath) => _db.DeleteFile(filePath);

    // ─────────────────────────────────────────────────────────
    // 4. BuildMissingContentAsync — 내용 미인덱싱 파일만 추가
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 메타 인덱스는 그대로 유지하고, has_content = 0 인 파일의 내용만 추가 인덱싱한다.
    /// EnableContentIndexing 설정 값과 무관하게 항상 내용을 파싱한다.
    /// </summary>
    public async Task BuildMissingContentAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // DB 에서 내용 미인덱싱 파일 수집 (파서 지원 형식만)
        var files = await Task.Run(() =>
            _db.GetAllIndexedFiles()
               .Where(f => !f.HasContent && _parser.CanParseContent(f.FilePath))
               .ToList(), ct);

        int total   = files.Count;
        int added   = 0;

        // 내용 파싱은 CPU 집약적 → 500건 소배치로 트랜잭션 부담을 줄임
        const int ContentBatch = 500;

        for (int batchStart = 0; batchStart < files.Count; batchStart += ContentBatch)
        {
            ct.ThrowIfCancellationRequested();
            int batchEnd = Math.Min(batchStart + ContentBatch, files.Count);

            using var tx = _db.BeginTransaction();
            try
            {
                for (int i = batchStart; i < batchEnd; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var file = files[i];

                    try
                    {
                        var content = await _parser.ParseFileContent(file.FilePath, _settings.MaxFileSizeMB);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            _db.InsertOrUpdateContent(file.Id, content, tx);
                            added++;
                        }
                    }
                    catch (Exception ex) { RaiseError(file.FilePath, ex); }

                    RaiseProgress(file.FilePath, i + 1, total, "내용 인덱싱");
                }
                tx.Commit();
            }
            catch (OperationCanceledException) { tx.Rollback(); throw; }
            catch { tx.Rollback(); }
        }

        sw.Stop();
        IndexCompleted?.Invoke(new IndexResult(total, 0, added, 0, sw.Elapsed));
    }

    // ─────────────────────────────────────────────────────────
    // 핵심 인덱싱 로직 (공통)
    // ─────────────────────────────────────────────────────────

    /// <param name="tx">배치 처리용 외부 트랜잭션. null이면 자동커밋.</param>
    private async Task IndexCoreAsync(string filePath, SqliteTransaction? tx = null)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists) return;

        var hash = ComputeFileHash(info);

        // 1단계: 파일 메타 저장 (항상)
        var fileId = _db.InsertOrUpdateFile(new FileIndex
        {
            FilePath    = filePath,
            FileName    = info.Name,
            Extension   = info.Extension.ToLowerInvariant(),
            FileSize    = info.Length,
            ModifiedAt  = info.LastWriteTimeUtc,
            IndexedAt   = DateTime.UtcNow,
            ContentHash = hash,
        }, tx);

        // 2단계: 내용 파싱 (EnableContentIndexing ON + 파서 지원 형식)
        if (_settings.EnableContentIndexing && _parser.CanParseContent(filePath))
        {
            var content = await _parser.ParseFileContent(filePath, _settings.MaxFileSizeMB);
            if (!string.IsNullOrWhiteSpace(content))
                _db.InsertOrUpdateContent(fileId, content, tx);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 파일 수집 & 필터링
    // ─────────────────────────────────────────────────────────

    private IEnumerable<string> CollectFiles(IEnumerable<string> roots)
    {
        var includedExts = new HashSet<string>(_settings.DefaultExtensions,
            StringComparer.OrdinalIgnoreCase);
        var userExcluded = _settings.ExcludedPaths;

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;

            var rootDir = new DirectoryInfo(root);
            if (IsDirExcluded(rootDir, userExcluded)) continue;

            foreach (var file in RecurseDirectory(rootDir, userExcluded))
            {
                // ① 바이너리 확장자 제외
                if (_binaryExtensions.Contains(file.Extension)) continue;
                // ② 숨김 파일 제외
                if (file.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                // ③ DefaultExtensions 필터 (빈 배열 = 전체 허용)
                if (includedExts.Count > 0 && !includedExts.Contains(file.Extension)) continue;

                yield return file.FullName;
            }
        }
    }

    private static IEnumerable<FileInfo> RecurseDirectory(
        DirectoryInfo dir, string[] userExcluded)
    {
        // 현재 디렉터리의 파일 반환
        foreach (var f in SafeFiles(dir))
            yield return f;

        // 하위 디렉터리 재귀
        foreach (var sub in SafeDirs(dir))
        {
            if (sub.Attributes.HasFlag(FileAttributes.Hidden)) continue;
            if (IsDirExcluded(sub, userExcluded)) continue;

            foreach (var f in RecurseDirectory(sub, userExcluded))
                yield return f;
        }
    }

    /// <summary>디렉터리가 시스템 제외 경로 또는 사용자 지정 제외 경로에 해당하면 true.</summary>
    private static bool IsDirExcluded(DirectoryInfo dir, string[] userExcluded)
    {
        var norm = NormDir(dir.FullName);

        if (_systemExcludedPaths.Contains(norm)) return true;

        foreach (var ep in userExcluded)
        {
            if (norm.StartsWith(NormDir(ep), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>단일 파일이 제외 조건(숨김, 바이너리, 시스템 경로)에 해당하면 true.</summary>
    private static bool ShouldExcludeFile(FileInfo file)
    {
        if (file.Attributes.HasFlag(FileAttributes.Hidden)) return true;
        if (_binaryExtensions.Contains(file.Extension))     return true;

        // 부모 디렉터리 체인에 시스템 경로가 있으면 제외
        var dir = file.Directory;
        while (dir is not null)
        {
            if (_systemExcludedPaths.Contains(NormDir(dir.FullName))) return true;
            dir = dir.Parent;
        }
        return false;
    }

    // ─────────────────────────────────────────────────────────
    // 안전한 파일 시스템 열거 (접근 권한 오류 무시)
    // ─────────────────────────────────────────────────────────

    private static IEnumerable<FileInfo> SafeFiles(DirectoryInfo dir)
    {
        try { return dir.EnumerateFiles(); }
        catch { return []; }
    }

    private static IEnumerable<DirectoryInfo> SafeDirs(DirectoryInfo dir)
    {
        try { return dir.EnumerateDirectories(); }
        catch { return []; }
    }

    // ─────────────────────────────────────────────────────────
    // 해시 계산
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 파일 경로 + 수정일 + 크기로 변경 감지 해시를 생성한다.
    /// 파일 내용을 읽지 않으므로 I/O 부하가 없다.
    /// </summary>
    private static string ComputeFileHash(FileInfo info)
    {
        var raw = $"{info.FullName}|{info.LastWriteTimeUtc:O}|{info.Length}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    // ─────────────────────────────────────────────────────────
    // 이벤트 헬퍼
    // ─────────────────────────────────────────────────────────

    private void RaiseProgress(string path, int processed, int total, string phase) =>
        IndexProgressChanged?.Invoke(new IndexProgress(path, processed, total, phase));

    private void RaiseError(string path, Exception ex) =>
        IndexErrorOccurred?.Invoke(new IndexError(path, ex.Message));

    // ─────────────────────────────────────────────────────────
    // 유틸
    // ─────────────────────────────────────────────────────────

    /// <summary>경로 끝의 구분자를 제거하여 비교 가능한 형태로 정규화한다.</summary>
    private static string NormDir(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
