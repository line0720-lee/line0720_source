using System.Text;
using Microsoft.Data.Sqlite;
using SFM.Models;

namespace SFM.Data;

/// <summary>
/// SFM SQLite 데이터베이스 접근 계층.
/// 경로: %APPDATA%\SFM\sfm_index.db
///
/// ── 연결 분리 아키텍처 ─────────────────────────────────────────
///   _writeConn  Indexer 전용 (INSERT/UPDATE/DELETE/DDL)
///               WriteLock(1,1) 으로 직렬화
///
///   _readConn   Searcher / 통계 전용 (SELECT)
///               ReadLock(1,1) 으로 직렬화
///               WAL 스냅샷 격리 → _writeConn 쓰기와 동시 실행 가능
///
/// WAL 모드에서 두 연결은 독립적으로 동작한다.
/// _writeConn 이 FTS5 트랜잭션을 진행 중이어도
/// _readConn 의 SELECT 는 마지막 커밋된 스냅샷을 즉시 반환한다.
/// ──────────────────────────────────────────────────────────────
/// </summary>
public class DatabaseManager : IDisposable
{
    // ── 반환 타입 ────────────────────────────────────────────
    /// <summary>인덱스 통계 스냅샷.</summary>
    public record IndexStats(int TotalFiles, int ContentIndexedFiles, double DatabaseSizeMb);

    // ── 내부 필드 ────────────────────────────────────────────
    private readonly string _dbPath;
    private SqliteConnection? _writeConn;   // 쓰기 전용 연결 (Indexer)
    private SqliteConnection? _readConn;    // 읽기 전용 연결 (Searcher / Stats)

    /// <summary>
    /// 쓰기 직렬화 락.
    /// Indexer 의 모든 INSERT / UPDATE / DELETE 호출 전후에 WaitAsync / Release.
    /// </summary>
    public readonly SemaphoreSlim WriteLock = new(1, 1);

    /// <summary>
    /// 읽기 직렬화 락.
    /// Searcher / MainForm 의 SELECT 호출 전후에 Wait / Release.
    /// WriteLock 과 독립적이므로 쓰기 중에도 읽기 진행 가능.
    /// </summary>
    public readonly SemaphoreSlim ReadLock = new(1, 1);

    private string ConnectionString => $"Data Source={_dbPath}";

    // ── 생성자 ───────────────────────────────────────────────
    public DatabaseManager(string dbPath)
    {
        _dbPath = dbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
    }

    // ─────────────────────────────────────────────────────────
    // 초기화
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// DB 연결 열기, 성능 PRAGMA 적용, 스키마 생성.
    /// SQLite 오류코드 11(CORRUPT) 또는 26(NOTADB) 감지 시 DB 파일을 백업하고 재생성한다.
    /// </summary>
    public void Initialize()
    {
        try
        {
            OpenAndInit();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 11 or 26)
        {
            // 손상된 DB → 타임스탬프 백업 후 재생성
            RecoverCorruptDatabase();
            OpenAndInit();
        }
    }

    private void OpenAndInit()
    {
        // ── 쓰기 전용 연결 ─────────────────────────────────────
        _writeConn = new SqliteConnection(ConnectionString);
        _writeConn.Open();
        ApplyWritePragmas();

        // 빠른 무결성 검사 (데이터 페이지 손상 조기 감지)
        using (var chk = _writeConn.CreateCommand())
        {
            chk.CommandText = "PRAGMA quick_check(1)";
            var result = chk.ExecuteScalar()?.ToString();
            if (result != "ok")
                throw new SqliteException($"DB quick_check 실패: {result}", 11);
        }

        CreateSchema();
        MigrateToTrigramIfNeeded();   // unicode61 → trigram 자동 마이그레이션
        Exec("PRAGMA optimize");      // 시작 시 쿼리 플래너 통계 갱신

        // ── 읽기 전용 연결 ─────────────────────────────────────
        // _writeConn 이 WAL 모드를 설정했으므로 _readConn 은 자동으로 WAL 을 사용한다.
        // 별도 연결이므로 WriteLock 없이 SELECT 를 실행할 수 있다.
        _readConn = new SqliteConnection(ConnectionString);
        _readConn.Open();
        ApplyReadPragmas();
    }

    /// <summary>외부에서 강제 복구 요청 시 호출. 손상 파일 백업 후 재생성.</summary>
    public void RecoverAndReinit()
    {
        RecoverCorruptDatabase();
        OpenAndInit();
    }

    private void RecoverCorruptDatabase()
    {
        _writeConn?.Dispose(); _writeConn = null;
        _readConn?.Dispose();  _readConn  = null;

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        foreach (var suffix in (string[])["", "-wal", "-shm"])
        {
            var path = _dbPath + suffix;
            if (!File.Exists(path)) continue;
            try
            {
                File.Move(path, $"{path}.corrupt_{stamp}", overwrite: true);
            }
            catch
            {
                try { File.Delete(path); } catch { /* 삭제도 실패하면 무시 */ }
            }
        }
    }

    /// <summary>쓰기 연결용 PRAGMA — WAL · 동기화 · 캐시 · 자동체크포인트.</summary>
    private void ApplyWritePragmas()
    {
        Exec("PRAGMA journal_mode       = WAL");
        Exec("PRAGMA synchronous        = NORMAL");
        Exec("PRAGMA foreign_keys       = ON");
        Exec("PRAGMA temp_store         = MEMORY");
        Exec("PRAGMA cache_size         = -131072");    // 128 MB 캐시
        Exec("PRAGMA mmap_size          = 536870912");  // 512 MB 메모리맵 I/O
        Exec("PRAGMA wal_autocheckpoint = 4000");       // WAL 4000 페이지마다 체크포인트
    }

    /// <summary>읽기 연결용 PRAGMA — 읽기 캐시만 설정 (WAL 모드는 파일 레벨 공유).</summary>
    private void ApplyReadPragmas()
    {
        ExecRead("PRAGMA cache_size = -65536");     // 64 MB 읽기 캐시
        ExecRead("PRAGMA mmap_size  = 536870912");  // 512 MB 메모리맵
        ExecRead("PRAGMA temp_store = MEMORY");
    }

    private void CreateSchema()
    {
        // ── 1. 파일 메타 인덱스 ──────────────────────────────
        Exec("""
            CREATE TABLE IF NOT EXISTS file_index (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path    TEXT    UNIQUE NOT NULL,
                file_name    TEXT    NOT NULL,
                extension    TEXT,
                file_size    INTEGER,
                modified_at  TEXT,
                indexed_at   TEXT,
                content_hash TEXT,
                has_content  INTEGER DEFAULT 0
            )
            """);

        // ── 단일 컬럼 인덱스 ─────────────────────────────────
        Exec("CREATE INDEX IF NOT EXISTS idx_fi_name      ON file_index (file_name)");
        Exec("CREATE INDEX IF NOT EXISTS idx_fi_extension ON file_index (extension)");
        Exec("CREATE INDEX IF NOT EXISTS idx_fi_modified  ON file_index (modified_at)");
        Exec("CREATE INDEX IF NOT EXISTS idx_fi_content   ON file_index (has_content)");
        Exec("CREATE INDEX IF NOT EXISTS idx_fi_size      ON file_index (file_size)");

        // ── 복합 인덱스 (필터+정렬 조합 최적화) ─────────────
        Exec("CREATE INDEX IF NOT EXISTS idx_fi_ext_mod   ON file_index (extension, modified_at)");
        Exec("CREATE INDEX IF NOT EXISTS idx_fi_mod_ext   ON file_index (modified_at, extension)");

        // ── 2. FTS5 전문 검색 가상 테이블 ────────────────────
        Exec("""
            CREATE VIRTUAL TABLE IF NOT EXISTS file_content USING fts5(
                file_id  UNINDEXED,
                content,
                tokenize = 'trigram'
            )
            """);

        // ── 3. 검색 히스토리 ─────────────────────────────────
        Exec("""
            CREATE TABLE IF NOT EXISTS search_history (
                id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                query              TEXT    NOT NULL,
                was_content_search INTEGER DEFAULT 0,
                searched_at        TEXT
            )
            """);
    }

    /// <summary>
    /// FTS5 테이블이 unicode61 토크나이저를 사용 중이면 trigram 으로 재생성한다.
    /// </summary>
    private void MigrateToTrigramIfNeeded()
    {
        using var chk = _writeConn!.CreateCommand();
        chk.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='file_content'";
        var ddl = chk.ExecuteScalar()?.ToString() ?? string.Empty;

        if (ddl.Contains("trigram", StringComparison.OrdinalIgnoreCase)) return;

        Exec("DROP TABLE IF EXISTS file_content");
        Exec("""
            CREATE VIRTUAL TABLE file_content USING fts5(
                file_id  UNINDEXED,
                content,
                tokenize = 'trigram'
            )
            """);
        Exec("UPDATE file_index SET has_content = 0");
    }

    // ─────────────────────────────────────────────────────────
    // 파일 인덱스 CRUD  ── 쓰기 (_writeConn / WriteLock)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 파일 메타를 삽입하거나 갱신한다.
    /// ON CONFLICT 시 has_content 는 건드리지 않아 내용 인덱스 상태를 보존한다.
    /// </summary>
    public long InsertOrUpdateFile(FileIndex file, SqliteTransaction? tx = null)
    {
        using var cmd = WriteCmd("""
            INSERT INTO file_index
                (file_path, file_name, extension, file_size, modified_at, indexed_at, content_hash)
            VALUES
                ($path, $name, $ext, $size, $modified, $indexed, $hash)
            ON CONFLICT(file_path) DO UPDATE SET
                file_name    = excluded.file_name,
                extension    = excluded.extension,
                file_size    = excluded.file_size,
                modified_at  = excluded.modified_at,
                indexed_at   = excluded.indexed_at,
                content_hash = excluded.content_hash
            RETURNING id
            """, tx);

        cmd.Parameters.AddWithValue("$path",     file.FilePath);
        cmd.Parameters.AddWithValue("$name",     file.FileName);
        cmd.Parameters.AddWithValue("$ext",      AsDbValue(file.Extension));
        cmd.Parameters.AddWithValue("$size",     AsDbValue(file.FileSize));
        cmd.Parameters.AddWithValue("$modified", file.ModifiedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$indexed",  file.IndexedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$hash",     AsDbValue(file.ContentHash));

        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>
    /// 파일 내용을 FTS5 인덱스에 삽입/갱신하고 has_content = 1 로 표시한다.
    /// FTS5 는 ON CONFLICT 를 지원하지 않으므로 DELETE → INSERT 로 구현한다.
    /// </summary>
    public void InsertOrUpdateContent(long fileId, string content, SqliteTransaction? outerTx = null)
    {
        bool ownsTx = outerTx is null;
        var  tx     = ownsTx ? _writeConn!.BeginTransaction() : outerTx!;

        try
        {
            using var del = WriteCmd("DELETE FROM file_content WHERE file_id = $id", tx);
            del.Parameters.AddWithValue("$id", fileId);
            del.ExecuteNonQuery();

            using var ins = WriteCmd("INSERT INTO file_content (file_id, content) VALUES ($id, $content)", tx);
            ins.Parameters.AddWithValue("$id",      fileId);
            ins.Parameters.AddWithValue("$content", content);
            ins.ExecuteNonQuery();

            using var upd = WriteCmd("UPDATE file_index SET has_content = 1 WHERE id = $id", tx);
            upd.Parameters.AddWithValue("$id", fileId);
            upd.ExecuteNonQuery();

            if (ownsTx) tx.Commit();
        }
        catch
        {
            if (ownsTx) try { tx.Rollback(); } catch { }
            throw;
        }
        finally
        {
            if (ownsTx) tx.Dispose();
        }
    }

    /// <summary>file_index 및 file_content 에서 해당 파일의 모든 데이터를 삭제한다.</summary>
    public void DeleteFile(string filePath)
    {
        using var tx = _writeConn!.BeginTransaction();
        try
        {
            using var sel = WriteCmd("SELECT id FROM file_index WHERE file_path = $path", tx);
            sel.Parameters.AddWithValue("$path", filePath);
            var raw = sel.ExecuteScalar();

            if (raw is null) { tx.Rollback(); return; }

            var id = Convert.ToInt64(raw);

            using var delContent = WriteCmd("DELETE FROM file_content WHERE file_id = $id", tx);
            delContent.Parameters.AddWithValue("$id", id);
            delContent.ExecuteNonQuery();

            using var delIndex = WriteCmd("DELETE FROM file_index WHERE id = $id", tx);
            delIndex.Parameters.AddWithValue("$id", id);
            delIndex.ExecuteNonQuery();

            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    // ─────────────────────────────────────────────────────────
    // 경로 조회 ── 읽기 (_readConn / ReadLock)
    // ─────────────────────────────────────────────────────────

    /// <summary>인덱싱된 전체 파일 경로를 반환한다.</summary>
    public List<string> GetAllIndexedPaths()
    {
        using var cmd = ReadCmd("SELECT file_path FROM file_index ORDER BY file_path");
        return ReadStrings(cmd);
    }

    /// <summary>내용까지 인덱싱된 파일 경로만 반환한다.</summary>
    public List<string> GetContentIndexedPaths()
    {
        using var cmd = ReadCmd(
            "SELECT file_path FROM file_index WHERE has_content = 1 ORDER BY file_path");
        return ReadStrings(cmd);
    }

    // ─────────────────────────────────────────────────────────
    // 인덱스 관리
    // ─────────────────────────────────────────────────────────

    /// <summary>file_content(FTS5) 전체를 삭제하고 has_content 플래그를 0 으로 초기화한다.</summary>
    public void ClearContentIndex()
    {
        using var tx = _writeConn!.BeginTransaction();
        try
        {
            using var del = WriteCmd("DELETE FROM file_content", tx);
            del.ExecuteNonQuery();

            using var upd = WriteCmd("UPDATE file_index SET has_content = 0", tx);
            upd.ExecuteNonQuery();

            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>총 파일 수, 내용 인덱싱 파일 수, DB 파일 크기(MB)를 반환한다.</summary>
    public IndexStats GetIndexStats()
    {
        using var cmd = ReadCmd("""
            SELECT
                COUNT(*)                                          AS total,
                SUM(CASE WHEN has_content = 1 THEN 1 ELSE 0 END) AS content_cnt
            FROM file_index
            """);

        using var r = cmd.ExecuteReader();
        r.Read();

        int    total   = r.IsDBNull(0) ? 0 : (int)r.GetInt64(0);
        int    content = r.IsDBNull(1) ? 0 : (int)r.GetInt64(1);
        double mb      = new FileInfo(_dbPath) is { Exists: true } fi
            ? Math.Round(fi.Length / (1024.0 * 1024), 2) : 0;

        return new IndexStats(total, content, mb);
    }

    // ─────────────────────────────────────────────────────────
    // 전체 파일 조회 ── 읽기 (UpdateIndexAsync 전용)
    // ─────────────────────────────────────────────────────────

    /// <summary>인덱싱된 모든 파일의 메타(내용 제외)를 반환한다.</summary>
    public List<FileIndex> GetAllIndexedFiles()
    {
        using var cmd = ReadCmd("""
            SELECT id, file_path, file_name, extension, file_size,
                   modified_at, indexed_at, content_hash, has_content
            FROM   file_index
            ORDER  BY file_path
            """);
        return ReadFileIndexList(cmd);
    }

    // ─────────────────────────────────────────────────────────
    // 전체 초기화 ── 쓰기
    // ─────────────────────────────────────────────────────────

    /// <summary>file_index · file_content · search_history 전체를 삭제한다 (스키마 유지).</summary>
    public void ClearAllIndexes()
    {
        using var tx = _writeConn!.BeginTransaction();
        try
        {
            using var d1 = WriteCmd("DELETE FROM file_content",   tx); d1.ExecuteNonQuery();
            using var d2 = WriteCmd("DELETE FROM file_index",     tx); d2.ExecuteNonQuery();
            using var d3 = WriteCmd("DELETE FROM search_history", tx); d3.ExecuteNonQuery();
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    /// <summary>VACUUM 으로 DB 파일을 압축하여 삭제된 데이터 공간을 회수한다.</summary>
    public void Vacuum()
    {
        Exec("VACUUM");
        Exec("PRAGMA optimize");
    }

    /// <summary>배치 인덱싱을 위한 트랜잭션을 시작한다 (쓰기 연결 전용).</summary>
    public SqliteTransaction BeginTransaction() => _writeConn!.BeginTransaction();

    // ─────────────────────────────────────────────────────────
    // 검색 ── 읽기 (_readConn / ReadLock)
    // ─────────────────────────────────────────────────────────

    /// <summary>파일명 LIKE 검색. 내용 미검색.</summary>
    public List<FileIndex> SearchByFileName(string keyword, int limit)
    {
        using var cmd = ReadCmd("""
            SELECT id, file_path, file_name, extension, file_size,
                   modified_at, indexed_at, content_hash, has_content
            FROM   file_index
            WHERE  file_name LIKE $kw
            ORDER  BY modified_at DESC
            LIMIT  $limit
            """);
        cmd.Parameters.AddWithValue("$kw",    $"%{keyword}%");
        cmd.Parameters.AddWithValue("$limit", limit);
        return ReadFileIndexList(cmd);
    }

    /// <summary>FTS5 전문 검색. 2자 이하 키워드는 LIKE 폴백.</summary>
    public List<(FileIndex File, string Snippet)> SearchByContent(string keyword, int limit)
    {
        if (NeedsFtsLikeFallback(keyword))
            return SearchByContentLike(keyword, [], null, null, null, null, limit, 0);

        var ftsQuery = EscapeFts(keyword);

        using var cmd = ReadCmd("""
            SELECT fi.id, fi.file_path, fi.file_name, fi.extension, fi.file_size,
                   fi.modified_at, fi.indexed_at, fi.content_hash, fi.has_content,
                   snippet(file_content, 1, '【', '】', '…', 12) AS snippet
            FROM   file_content
            JOIN   file_index fi ON fi.id = file_content.file_id
            WHERE  file_content MATCH $q
            ORDER  BY rank
            LIMIT  $limit
            """);
        cmd.Parameters.AddWithValue("$q",     ftsQuery);
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<(FileIndex, string)>();
        try
        {
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add((ReadFileIndex(r), r.IsDBNull(9) ? string.Empty : r.GetString(9)));
        }
        catch (SqliteException) { }
        return list;
    }

    /// <summary>파일명 LIKE 검색 + 다중 필터 (확장자·크기·수정일).</summary>
    public List<FileIndex> SearchByNameFiltered(
        string    likePattern,
        string[]  extensions,
        long?     minSize,
        long?     maxSize,
        DateTime? after,
        DateTime? before,
        int       limit,
        int       offset = 0)
    {
        var sb = new StringBuilder("""
            SELECT id, file_path, file_name, extension, file_size,
                   modified_at, indexed_at, content_hash, has_content
            FROM   file_index
            WHERE  file_name LIKE $like ESCAPE '\'
            """);

        AppendFilters(sb, extensions, minSize, maxSize, after, before);
        sb.Append("\nORDER BY modified_at DESC\nLIMIT $limit OFFSET $offset");

        using var cmd = ReadCmd(sb.ToString());
        cmd.Parameters.AddWithValue("$like",   likePattern);
        AddFilterParams(cmd, extensions, minSize, maxSize, after, before);
        cmd.Parameters.AddWithValue("$limit",  limit);
        cmd.Parameters.AddWithValue("$offset", offset);
        return ReadFileIndexList(cmd);
    }

    /// <summary>FTS5 전문 검색 + 다중 필터. snippet() 함수로 발췌문을 함께 반환한다.</summary>
    public List<(FileIndex File, string Snippet)> SearchByContentFiltered(
        string    ftsQuery,
        string[]  extensions,
        long?     minSize,
        long?     maxSize,
        DateTime? after,
        DateTime? before,
        int       limit,
        int       offset = 0)
    {
        var raw = ftsQuery.Trim('"').Trim();
        if (NeedsFtsLikeFallback(raw))
            return SearchByContentLike(raw, extensions, minSize, maxSize, after, before, limit, offset);

        var sb = new StringBuilder("""
            SELECT fi.id, fi.file_path, fi.file_name, fi.extension, fi.file_size,
                   fi.modified_at, fi.indexed_at, fi.content_hash, fi.has_content,
                   snippet(file_content, 1, '【', '】', '…', 12) AS snippet
            FROM   file_content
            JOIN   file_index fi ON fi.id = file_content.file_id
            WHERE  file_content MATCH $q
            """);

        AppendFilters(sb, extensions, minSize, maxSize, after, before, tableAlias: "fi");
        sb.Append("\nORDER BY rank\nLIMIT $limit OFFSET $offset");

        using var cmd = ReadCmd(sb.ToString());
        cmd.Parameters.AddWithValue("$q",      ftsQuery);
        AddFilterParams(cmd, extensions, minSize, maxSize, after, before);
        cmd.Parameters.AddWithValue("$limit",  limit);
        cmd.Parameters.AddWithValue("$offset", offset);

        var list = new List<(FileIndex, string)>();
        try
        {
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add((ReadFileIndex(r), r.IsDBNull(9) ? string.Empty : r.GetString(9)));
        }
        catch (SqliteException) { }
        return list;
    }

    /// <summary>2자 이하 키워드용 LIKE 폴백 내용 검색.</summary>
    private List<(FileIndex File, string Snippet)> SearchByContentLike(
        string    keyword,
        string[]  extensions,
        long?     minSize,
        long?     maxSize,
        DateTime? after,
        DateTime? before,
        int       limit,
        int       offset)
    {
        var sb = new StringBuilder("""
            SELECT fi.id, fi.file_path, fi.file_name, fi.extension, fi.file_size,
                   fi.modified_at, fi.indexed_at, fi.content_hash, fi.has_content,
                   fc.content AS snippet
            FROM   file_content fc
            JOIN   file_index fi ON fi.id = fc.file_id
            WHERE  fc.content LIKE $like ESCAPE '\'
            """);

        AppendFilters(sb, extensions, minSize, maxSize, after, before, tableAlias: "fi");
        sb.Append("\nORDER BY fi.modified_at DESC\nLIMIT $limit OFFSET $offset");

        using var cmd = ReadCmd(sb.ToString());
        cmd.Parameters.AddWithValue("$like", $"%{keyword}%");
        AddFilterParams(cmd, extensions, minSize, maxSize, after, before);
        cmd.Parameters.AddWithValue("$limit",  limit);
        cmd.Parameters.AddWithValue("$offset", offset);

        var list = new List<(FileIndex, string)>();
        try
        {
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var fullContent = r.IsDBNull(9) ? string.Empty : r.GetString(9);
                var snippet     = ExtractSnippet(fullContent, keyword, 60);
                list.Add((ReadFileIndex(r), snippet));
            }
        }
        catch (SqliteException) { }
        return list;
    }

    /// <summary>최신 수정 파일을 다중 필터 조건으로 가져온다 (초성 검색 후보 수집용).</summary>
    public List<FileIndex> GetRecentFiles(
        string[]  extensions,
        long?     minSize,
        long?     maxSize,
        DateTime? after,
        DateTime? before,
        int       limit,
        int       offset = 0)
    {
        var sb = new StringBuilder("""
            SELECT id, file_path, file_name, extension, file_size,
                   modified_at, indexed_at, content_hash, has_content
            FROM   file_index
            WHERE  1=1
            """);

        AppendFilters(sb, extensions, minSize, maxSize, after, before);
        sb.Append("\nORDER BY modified_at DESC\nLIMIT $limit OFFSET $offset");

        using var cmd = ReadCmd(sb.ToString());
        AddFilterParams(cmd, extensions, minSize, maxSize, after, before);
        cmd.Parameters.AddWithValue("$limit",  limit);
        cmd.Parameters.AddWithValue("$offset", offset);
        return ReadFileIndexList(cmd);
    }

    // ─────────────────────────────────────────────────────────
    // 전체 검색 건수 ── 읽기 (무한 스크롤용 total count)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 파일명 검색(선택적으로 내용 검색 포함) 조건에 해당하는 고유 파일 수를 반환한다.
    /// 파일명 + 내용을 UNION 으로 결합하여 중복 없는 정확한 건수를 계산한다.
    /// </summary>
    public long CountSearchFiltered(
        string    nameLikePattern,
        string?   ftsQuery,
        string[]  extensions,
        long?     minSize,
        long?     maxSize,
        DateTime? after,
        DateTime? before)
    {
        var sbName = new StringBuilder("""
            SELECT file_path
            FROM   file_index
            WHERE  file_name LIKE $nameLike ESCAPE '\'
            """);
        AppendFilters(sbName, extensions, minSize, maxSize, after, before);

        string sql;

        if (ftsQuery is null)
        {
            sql = $"SELECT COUNT(*) FROM ({sbName})";
        }
        else
        {
            var rawFts = ftsQuery.Trim('"').Trim();
            StringBuilder sbContent;

            if (NeedsFtsLikeFallback(rawFts))
            {
                sbContent = new StringBuilder("""
                    SELECT fi.file_path
                    FROM   file_content fc
                    JOIN   file_index fi ON fi.id = fc.file_id
                    WHERE  fc.content LIKE $cLike ESCAPE '\'
                    """);
            }
            else
            {
                sbContent = new StringBuilder("""
                    SELECT fi.file_path
                    FROM   file_content
                    JOIN   file_index fi ON fi.id = file_content.file_id
                    WHERE  file_content MATCH $fts
                    """);
            }
            AppendFilters(sbContent, extensions, minSize, maxSize, after, before, tableAlias: "fi");
            sql = $"SELECT COUNT(*) FROM ({sbName}\nUNION\n{sbContent})";
        }

        using var cmd = ReadCmd(sql);
        cmd.Parameters.AddWithValue("$nameLike", nameLikePattern);
        AddFilterParams(cmd, extensions, minSize, maxSize, after, before);

        if (ftsQuery is not null)
        {
            var rawFts = ftsQuery.Trim('"').Trim();
            if (NeedsFtsLikeFallback(rawFts))
                cmd.Parameters.AddWithValue("$cLike", $"%{rawFts}%");
            else
                cmd.Parameters.AddWithValue("$fts", ftsQuery);
        }

        try
        {
            var result = cmd.ExecuteScalar();
            return result is null ? 0 : Convert.ToInt64(result);
        }
        catch (SqliteException) { return 0; }
    }

    // ─────────────────────────────────────────────────────────
    // 검색 히스토리 ── 쓰기 (SaveSearchHistory) / 읽기 (GetRecentSearches)
    // ─────────────────────────────────────────────────────────

    /// <summary>검색어를 히스토리에 저장한다 (쓰기).</summary>
    public void SaveSearchHistory(string query, bool wasContentSearch)
    {
        using var cmd = WriteCmd("""
            INSERT INTO search_history (query, was_content_search, searched_at)
            VALUES ($q, $isContent, $at)
            """);
        cmd.Parameters.AddWithValue("$q",         query);
        cmd.Parameters.AddWithValue("$isContent", wasContentSearch ? 1 : 0);
        cmd.Parameters.AddWithValue("$at",        DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>중복 제거 후 최근 검색어를 최대 <paramref name="limit"/>개 반환한다 (읽기).</summary>
    public List<string> GetRecentSearches(int limit = 20)
    {
        using var cmd = ReadCmd("""
            SELECT   query
            FROM     search_history
            GROUP BY query
            ORDER BY MAX(searched_at) DESC
            LIMIT    $limit
            """);
        cmd.Parameters.AddWithValue("$limit", limit);
        return ReadStrings(cmd);
    }

    // ─────────────────────────────────────────────────────────
    // 내부 헬퍼
    // ─────────────────────────────────────────────────────────

    /// <summary>쓰기 연결로 단일 SQL 문을 실행한다 (파라미터 없음).</summary>
    private void Exec(string sql)
    {
        using var cmd = _writeConn!.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>읽기 연결로 단일 SQL 문을 실행한다 (파라미터 없음).</summary>
    private void ExecRead(string sql)
    {
        using var cmd = _readConn!.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>쓰기 연결 커맨드 생성.</summary>
    private SqliteCommand WriteCmd(string sql, SqliteTransaction? tx = null)
    {
        var cmd = _writeConn!.CreateCommand();
        cmd.CommandText = sql;
        if (tx is not null) cmd.Transaction = tx;
        return cmd;
    }

    /// <summary>읽기 연결 커맨드 생성.</summary>
    private SqliteCommand ReadCmd(string sql)
    {
        var cmd = _readConn!.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    /// <summary>null 가능한 값을 DBNull 로 변환한다.</summary>
    private static object AsDbValue(object? value) => value ?? (object)DBNull.Value;

    private static void AppendFilters(
        StringBuilder sb,
        string[]  extensions,
        long?     minSize,
        long?     maxSize,
        DateTime? after,
        DateTime? before,
        string    tableAlias = "")
    {
        var pfx = tableAlias.Length > 0 ? tableAlias + "." : string.Empty;

        if (extensions is { Length: > 0 })
        {
            var inList = string.Join(", ", extensions.Select((_, i) => $"$ext{i}"));
            sb.Append($"\nAND {pfx}extension IN ({inList})");
        }
        if (minSize.HasValue) sb.Append($"\nAND {pfx}file_size   >= $minSize");
        if (maxSize.HasValue) sb.Append($"\nAND {pfx}file_size   <= $maxSize");
        if (after.HasValue)   sb.Append($"\nAND {pfx}modified_at >= $after");
        if (before.HasValue)  sb.Append($"\nAND {pfx}modified_at <= $before");
    }

    private static void AddFilterParams(
        SqliteCommand cmd,
        string[]  extensions,
        long?     minSize,
        long?     maxSize,
        DateTime? after,
        DateTime? before)
    {
        if (extensions is { Length: > 0 })
            for (int i = 0; i < extensions.Length; i++)
                cmd.Parameters.AddWithValue($"$ext{i}", extensions[i]);

        if (minSize.HasValue) cmd.Parameters.AddWithValue("$minSize", minSize.Value);
        if (maxSize.HasValue) cmd.Parameters.AddWithValue("$maxSize", maxSize.Value);
        if (after.HasValue)   cmd.Parameters.AddWithValue("$after",   after.Value.ToString("O"));
        if (before.HasValue)  cmd.Parameters.AddWithValue("$before",  before.Value.ToString("O"));
    }

    /// <summary>FTS5 MATCH 쿼리에 사용할 phrase 검색 문자열로 변환한다.</summary>
    private static string EscapeFts(string keyword)
    {
        var cleaned = keyword.Replace("\"", " ").Trim();
        return string.IsNullOrEmpty(cleaned) ? "\"\"" : $"\"{cleaned}\"";
    }

    /// <summary>trigram 은 3자 미만 쿼리를 처리하지 못하므로 LIKE 폴백이 필요하면 true.</summary>
    private static bool NeedsFtsLikeFallback(string keyword) => keyword.Length < 3;

    private static string ExtractSnippet(string content, string keyword, int maxChars)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        var idx = content.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return content.Length > maxChars ? content[..maxChars] + "…" : content;

        int start = Math.Max(0, idx - maxChars / 2);
        int end   = Math.Min(content.Length, start + maxChars + keyword.Length);
        start     = Math.Max(0, end - maxChars - keyword.Length);

        var snippet = content[start..end].Replace('\n', ' ').Replace('\r', ' ');
        if (start > 0)            snippet = "…" + snippet;
        if (end < content.Length) snippet += "…";
        return snippet;
    }

    private static List<string> ReadStrings(SqliteCommand cmd)
    {
        using var r  = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private static List<FileIndex> ReadFileIndexList(SqliteCommand cmd)
    {
        using var r  = cmd.ExecuteReader();
        var list = new List<FileIndex>();
        while (r.Read()) list.Add(ReadFileIndex(r));
        return list;
    }

    /// <summary>
    /// SELECT id, file_path, file_name, extension, file_size,
    ///        modified_at, indexed_at, content_hash, has_content  (col 0–8)
    /// 순서 고정.
    /// </summary>
    private static FileIndex ReadFileIndex(SqliteDataReader r) => new()
    {
        Id          = r.GetInt64(0),
        FilePath    = r.GetString(1),
        FileName    = r.GetString(2),
        Extension   = r.IsDBNull(3) ? null  : r.GetString(3),
        FileSize    = r.IsDBNull(4) ? 0     : r.GetInt64(4),
        ModifiedAt  = r.IsDBNull(5) ? DateTime.MinValue : DateTime.Parse(r.GetString(5)),
        IndexedAt   = r.IsDBNull(6) ? DateTime.MinValue : DateTime.Parse(r.GetString(6)),
        ContentHash = r.IsDBNull(7) ? null  : r.GetString(7),
        HasContent  = !r.IsDBNull(8) && r.GetInt64(8) == 1,
    };

    // ─────────────────────────────────────────────────────────
    // IDisposable
    // ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        try { _writeConn?.Close(); _writeConn?.Dispose(); } catch { }
        try { _readConn?.Close();  _readConn?.Dispose();  } catch { }
        try { WriteLock.Dispose(); } catch { }
        try { ReadLock.Dispose();  } catch { }
        GC.SuppressFinalize(this);
    }
}
