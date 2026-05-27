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

    /// <summary>
    /// 내용 인덱싱 대상 확장자 (고정).
    /// 텍스트 · 한글(HWP/HWPX) · 파워포인트 · 워드 · 엑셀 · PDF
    /// </summary>
    private static readonly HashSet<string> _contentIndexExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".hwp", ".hwpx",
        ".pptx", ".ppt",
        ".docx", ".doc",
        ".xlsx", ".xls",
        ".pdf",
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

    /// <summary>
    /// BuildIndexAsync / UpdateIndexAsync 실행 중 true.
    /// IndexSingleFileAsync 가 동시에 실행되어 배치 트랜잭션과 충돌하는 것을 막는다.
    /// </summary>
    private volatile bool _isBulkIndexing = false;

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
    /// 1000건 단위 배치 트랜잭션으로 처리하여 단건 커밋 대비 수십 배 빠른 속도를 제공한다.
    /// 기존 레코드는 InsertOrUpdate 로 갱신되므로 먼저 삭제할 필요 없다.
    /// </summary>
    public async Task BuildIndexAsync(string[] rootPaths, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        int indexed = 0, errors = 0;
        const int BatchSize = 1_000;

        _isBulkIndexing = true;
        try
        {
            // ── Phase 1: 파일 목록 수집 ─────────────────────────────────
            // Task.Run으로 백그라운드 스레드에서 실행 → UI 블로킹 방지.
            // 200개마다 이벤트를 발생시켜 오버레이 Marquee 애니메이션이 살아있게 한다.
            var files = new List<string>();
            await Task.Run(() =>
            {
                foreach (var path in CollectFiles(rootPaths))
                {
                    ct.ThrowIfCancellationRequested();
                    files.Add(path);
                    if (files.Count % 200 == 0)
                        RaiseProgress(path, files.Count, 0, "수집 중");   // Total=0 → Marquee
                }
            }, ct).ConfigureAwait(false);

            int total = files.Count;

            // 수집 완료 알림: Total 확정 → UI가 Marquee → Continuous 전환 준비
            // Processed=0 으로 전달해 진행 바가 0%에서 시작하게 한다
            if (total > 0)
                RaiseProgress(files[^1], 0, total, "수집 완료");

            // ── Phase 2: 메타 + 내용 인덱싱 (배치 트랜잭션) ─────────────
            // 배치마다 DbLock 을 획득/해제하여 검색 요청이 배치 사이 틈에 실행될 수 있게 한다.
            for (int batchStart = 0; batchStart < files.Count; batchStart += BatchSize)
            {
                ct.ThrowIfCancellationRequested();
                int batchEnd = Math.Min(batchStart + BatchSize, files.Count);

                await _db.DbLock.WaitAsync(ct).ConfigureAwait(false);   // 배치 시작 전 락 획득
                try
                {
                    using var tx = _db.BeginTransaction();
                    try
                    {
                        for (int i = batchStart; i < batchEnd; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            var path = files[i];

                            try
                            {
                                await IndexCoreAsync(path, tx).ConfigureAwait(false);
                                indexed++;
                            }
                            catch (Exception ex)
                            {
                                errors++;
                                RaiseError(path, ex);
                            }

                            RaiseProgress(path, indexed + errors, total, "인덱싱");
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
                finally
                {
                    _db.DbLock.Release();   // 배치 끝나면 즉시 해제
                }

                // 배치 사이에 스레드 스케줄러에 양보 → 대기 중인 검색 요청이 실행될 기회
                await Task.Yield();
            }
        }
        finally
        {
            _isBulkIndexing = false;
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

        _isBulkIndexing = true;
        try
        {
            // ── DB 스냅샷 읽기 (락 보호) ─────────────────────────────
            Dictionary<string, FileIndex> indexed;
            await _db.DbLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                indexed = _db.GetAllIndexedFiles()
                    .ToDictionary(f => f.FilePath, StringComparer.OrdinalIgnoreCase);
            }
            finally { _db.DbLock.Release(); }

            // 디스크 파일 수집 (파일시스템 접근 — 락 불필요)
            var diskFiles = CollectFiles(_settings.IndexRootPaths)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int totalOps  = indexed.Count + diskFiles.Count;
            int processed = 0;

            // ── ① 삭제된 파일: DB 에 있지만 디스크에 없음 ────────────
            var toDelete = indexed.Keys.Except(diskFiles, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var path in toDelete)
            {
                ct.ThrowIfCancellationRequested();
                await _db.DbLock.WaitAsync(ct).ConfigureAwait(false);
                try   { _db.DeleteFile(path); deleted++; }
                catch (Exception ex) { errors++; RaiseError(path, ex); }
                finally { _db.DbLock.Release(); }
                RaiseProgress(path, ++processed, totalOps, "삭제");
            }

            // ── ② 새 파일 추가 + 변경 파일 재인덱싱 ──────────────────
            // Parse-Write 분리 구조 (BuildMissingContentAsync 와 동일 패턴):
            //   Phase A (락 없음) : 해시 비교 → 처리 대상 추출 → 문서 파싱
            //   Phase B (락 점유) : DB 쓰기만 수행 (빠른 작업)
            // → 락 점유 시간 = 파싱 시간 제거 → 검색 블로킹 없음
            const int UpdateBatch = 1_000;
            var diskFilesList = diskFiles.ToList();

            for (int batchStart = 0; batchStart < diskFilesList.Count; batchStart += UpdateBatch)
            {
                ct.ThrowIfCancellationRequested();
                int batchEnd = Math.Min(batchStart + UpdateBatch, diskFilesList.Count);

                // ── Phase A: 락 없이 해시 비교 + 문서 파싱 ──────────────
                var toWrite = new List<(FileInfo Info, string Hash, string? Content, bool IsNew)>();

                for (int bi = batchStart; bi < batchEnd; bi++)
                {
                    ct.ThrowIfCancellationRequested();
                    var path = diskFilesList[bi];

                    try
                    {
                        var info    = new FileInfo(path);
                        if (!info.Exists) { RaiseProgress(path, ++processed, totalOps, "업데이트"); continue; }
                        var newHash = ComputeFileHash(info);

                        bool isNew     = !indexed.TryGetValue(path, out var existing);
                        bool isChanged = !isNew && !string.Equals(existing!.ContentHash, newHash, StringComparison.Ordinal);

                        if (!isNew && !isChanged) { RaiseProgress(path, ++processed, totalOps, "업데이트"); continue; }

                        // 내용 파싱 — 락 밖에서 수행 (느린 작업)
                        string? content = null;
                        var ext = Path.GetExtension(path);
                        if (_settings.EnableContentIndexing && _contentIndexExts.Contains(ext))
                        {
                            content = await _parser.ParseFileContent(path, _settings.MaxFileSizeMB)
                                                   .ConfigureAwait(false);
                        }

                        toWrite.Add((info, newHash, content, isNew));
                    }
                    catch (Exception ex) { errors++; RaiseError(path, ex); }

                    RaiseProgress(path, ++processed, totalOps, "업데이트");
                }

                // ── Phase B: 락 점유 → DB 쓰기만 수행 (빠른 작업) ──────
                if (toWrite.Count > 0)
                {
                    await _db.DbLock.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        using var batchTx = _db.BeginTransaction();
                        try
                        {
                            foreach (var (info, hash, content, isNew) in toWrite)
                            {
                                ct.ThrowIfCancellationRequested();
                                try
                                {
                                    var fileId = _db.InsertOrUpdateFile(new FileIndex
                                    {
                                        FilePath    = info.FullName,
                                        FileName    = info.Name,
                                        Extension   = info.Extension.ToLowerInvariant(),
                                        FileSize    = info.Length,
                                        ModifiedAt  = info.LastWriteTimeUtc,
                                        IndexedAt   = DateTime.UtcNow,
                                        ContentHash = hash,
                                    }, batchTx);

                                    if (!string.IsNullOrWhiteSpace(content))
                                        _db.InsertOrUpdateContent(fileId, content, batchTx);

                                    if (isNew) added++;
                                    else       updated++;
                                }
                                catch (Exception ex) { errors++; RaiseError(info.FullName, ex); }
                            }
                            batchTx.Commit();
                        }
                        catch (OperationCanceledException) { batchTx.Rollback(); throw; }
                        catch { batchTx.Rollback(); }
                    }
                    finally { _db.DbLock.Release(); }
                }

                await Task.Yield(); // 배치 사이에 검색 실행 기회
            }
        }
        finally
        {
            _isBulkIndexing = false;
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
        // 대량 인덱싱(BuildIndexAsync / UpdateIndexAsync) 진행 중에는 스킵.
        // 배치 트랜잭션과의 DB 충돌 방지 및 IndexCompleted 이중 발생 억제.
        if (_isBulkIndexing) return;

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

            await _db.DbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await IndexCoreAsync(filePath).ConfigureAwait(false);
            }
            finally
            {
                _db.DbLock.Release();
            }

            sw.Stop();
            IndexCompleted?.Invoke(new IndexResult(1, 1, 0, 0, sw.Elapsed));
        }
        catch (Exception ex)
        {
            RaiseError(filePath, ex);
        }
    }

    /// <summary>파일 인덱스(메타 + 내용)를 DB 에서 삭제한다.</summary>
    public void RemoveFile(string filePath)
    {
        // 대량 인덱싱 중에는 스킵 — UpdateIndexAsync 가 다음 주기에 삭제를 처리한다.
        if (_isBulkIndexing) return;

        _db.DbLock.Wait();
        try { _db.DeleteFile(filePath); }
        finally { _db.DbLock.Release(); }
    }

    // ─────────────────────────────────────────────────────────
    // 4. BuildMissingContentAsync — 내용 미인덱싱 파일만 추가
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 메타 인덱스는 그대로 유지하고, has_content = 0 인 파일의 내용만 추가 인덱싱한다.
    /// 내용 인덱싱 대상은 <see cref="_contentIndexExts"/> 에 고정된 확장자만 처리한다.
    /// EnableContentIndexing 설정 값과 무관하게 항상 내용을 파싱한다.
    /// </summary>
    public async Task BuildMissingContentAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // FileWatcher 의 IndexSingleFileAsync / RemoveFile 이 동시에 실행되면
        // IndexCompleted 이벤트가 Stage 2 도중 발생 → UI 스레드에서 DB 접근 경합 → 응답없음
        // _isBulkIndexing = true 로 설정하여 이 경로를 완전히 차단한다.
        _isBulkIndexing = true;
        int total = 0, added = 0;

        try
        {
            // DB 에서 내용 미인덱싱 파일 목록 수집 (락 보호)
            List<FileIndex> files;
            await _db.DbLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                files = _db.GetAllIndexedFiles()
                    .Where(f => !f.HasContent
                        && _contentIndexExts.Contains(Path.GetExtension(f.FilePath)))
                    .ToList();
            }
            finally { _db.DbLock.Release(); }

            total = files.Count;

            // 내용 파싱은 CPU/IO 집약적 → 100건 소배치
            // 파싱(락 불필요)과 DB 저장(락 필요)을 분리하여 검색 지연 최소화
            const int ContentBatch = 100;

            for (int batchStart = 0; batchStart < files.Count; batchStart += ContentBatch)
            {
                ct.ThrowIfCancellationRequested();
                int batchEnd = Math.Min(batchStart + ContentBatch, files.Count);

                // ── Step 1: 파일 내용 파싱 (락 없이 실행) ─────────────
                // ConfigureAwait(false) → 쓰레드풀에서 실행, UI 스레드 점유 없음
                var parsed = new List<(FileIndex File, string Content)>();
                for (int i = batchStart; i < batchEnd; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var file = files[i];
                    try
                    {
                        var content = await _parser.ParseFileContent(file.FilePath, _settings.MaxFileSizeMB)
                                                   .ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(content))
                            parsed.Add((file, content));
                    }
                    catch (Exception ex) { RaiseError(file.FilePath, ex); }

                    RaiseProgress(file.FilePath, i + 1, total, "내용 인덱싱");
                }

                // ── Step 2: DB 저장 (락 보호, 쓰레드풀에서 실행) ──────
                if (parsed.Count > 0)
                {
                    await _db.DbLock.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        using var tx = _db.BeginTransaction();
                        try
                        {
                            foreach (var (file, content) in parsed)
                            {
                                _db.InsertOrUpdateContent(file.Id, content, tx);
                                added++;
                            }
                            tx.Commit();
                        }
                        catch (OperationCanceledException) { tx.Rollback(); throw; }
                        catch { tx.Rollback(); }
                    }
                    finally { _db.DbLock.Release(); }
                }

                await Task.Yield(); // 배치 사이 검색 실행 기회
            }
        }
        finally
        {
            _isBulkIndexing = false;
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

        // 2단계: 내용 파싱
        // EnableContentIndexing ON + 고정 내용 인덱싱 확장자(_contentIndexExts) 에 포함된 경우만
        var ext = Path.GetExtension(filePath);
        bool shouldIndexContent = _settings.EnableContentIndexing
            && _contentIndexExts.Contains(ext);

        if (shouldIndexContent)
        {
            var content = await _parser.ParseFileContent(filePath, _settings.MaxFileSizeMB)
                                       .ConfigureAwait(false);
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
