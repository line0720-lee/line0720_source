using System.Text;
using Microsoft.Data.Sqlite;
using SFM.Models;

namespace SFM.Data;

/// <summary>
/// SFM SQLite 데이터베이스 접근 계층.
/// 경로: %APPDATA%\SFM\sfm_index.db
/// </summary>
public class DatabaseManager : IDisposable
{
    // ── 반환 타입 ────────────────────────────────────────────
    /// <summary>인덱스 통계 스냅샷.</summary>
    public record IndexStats(int TotalFiles, int ContentIndexedFiles, double DatabaseSizeMb);

    // ── 내부 필드 ────────────────────────────────────────────
    private readonly string _dbPath;
    private SqliteConnection? _connection;

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
        _connection = new SqliteConnection(ConnectionString);
        _connection.Open();
        ApplyPragmas();

        // ── 빠른 무결성 검사 (데이터 페이지 손상 조기 감지) ─────
        using var chk = _connection.CreateCommand();
        chk.CommandText = "PRAGMA quick_check(1)";
        var result = chk.ExecuteScalar()?.ToString();
        if (result != "ok")
            throw new SqliteException($"DB quick_check 실패: {result}", 11);

        CreateSchema();
        MigrateToTrigramIfNeeded();   // unicode61 → trigram 자동 마이그레이션
        Exec("PRAGMA optimize");      // 시작 시 쿼리 플래너 통계 갱신
    }

    /// <summary>
    /// FTS5 테이블이 unicode61 토크나이저를 사용 중이면 trigram으로 재생성한다.
    /// unicode61 은 한국어를 토큰 단위로 분리하지 못해 부분 검색(예: "가나")이 불가능하다.
    /// trigram 은 3글자 N-gram으로 색인을 구성하여 임의 부분 문자열 검색을 지원한다.
    /// 마이그레이션 후 has_content 를 0으로 초기화하면 다음 시작 시
    /// BuildMissingContentAsync 가 자동으로 내용을 재인덱싱한다.
    /// </summary>
    private void MigrateToTrigramIfNeeded()
    {
        using var chk = _connection!.CreateCommand();
        chk.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='file_content'";
        var ddl = chk.ExecuteScalar()?.ToString() ?? string.Empty;

        // 이미 trigram 이면 마이그레이션 불필요
        if (ddl.Contains("trigram", StringComparison.OrdinalIgnoreCase)) return;

        // 기존 unicode61 FTS 테이블 삭제 → trigram 으로 재생성
        Exec("DROP TABLE IF EXISTS file_content");
        Exec("""
            CREATE VIRTUAL TABLE file_content USING fts5(
                file_id  UNINDEXED,
                content,
                tokenize = 'trigram'
            )
            """);

        // 기존 파일의 has_content 를 0으로 초기화
        // → 다음 시작 시 BuildMissingContentAsync 가 자동으로 재인덱싱
        Exec("UPDATE file_index SET has_content = 0");
    }

    /// <summary>
    /// 외부에서 강제 복구를 요청할 때 호출. 손상 파일을 백업하고 DB를 재생성한다.
    /// 검색 중 오류코드 11(CORRUPT) 발생 시 MainForm 에서 호출한다.
    /// </summary>
    public void RecoverAndReinit()
    {
        RecoverCorruptDatabase();
        OpenAndInit();
    }

    /// <summary>
    /// 손상된 DB 파일(.db / .db-wal / .db-shm)을 타임스탬프 접미사로 이름을 바꾼다.
    /// 이름 변경 실패 시 삭제를 시도한다.
    /// </summary>
    private void RecoverCorruptDatabase()
    {
        _connection?.Dispose();
        _connection = null;

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

    private void ApplyPragmas()
    {
        // WAL: 읽기/쓰기 병렬, NORMAL: 충분한 내구성, MEMORY: 임시 저장소
        Exec("PRAGMA journal_mode       = WAL");
        Exec("PRAGMA synchronous        = NORMAL");
        Exec("PRAGMA foreign_keys       = ON");
        Exec("PRAGMA temp_store         = MEMORY");
        Exec("PRAGMA cache_size         = -131072");   // 128 MB 캐시 (음수 = KB 단위) — 대용량 DB 반복 쿼리 가속
        Exec("PRAGMA mmap_size          = 536870912"); // 512 MB 메모리맵 I/O — 읽기 집약 최적화
        Exec("PRAGMA wal_autocheckpoint = 4000");      // WAL 4000페이지마다 체크포인트 — 검색 중 I/O 감소
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
        Exec("CREATE INDEX IF NOT EXISTS idx_fi_size      ON file_index (file_size)");  // 크기 범위 필터

        // ── 복합 인덱스 (필터+정렬 조합 최적화) ─────────────
        // 확장자 필터 후 최신 수정일 정렬 — SearchByNameFiltered / SearchByContentFiltered
        Exec("CREATE INDEX IF NOT EXISTS idx_fi_ext_mod   ON file_index (extension, modified_at)");
        // 수정일 범위 + 확장자 복합 필터 — GetRecentFiles / 날짜 기반 필터링
        Exec("CREATE INDEX IF NOT EXISTS idx_fi_mod_ext   ON file_index (modified_at, extension)");

        // ── 2. FTS5 전문 검색 가상 테이블 ────────────────────
        // file_id: UNINDEXED → FTS 색인 제외(숫자 키), content: 전문 색인 대상
        // trigram: 3글자 N-gram 색인 → 한국어 포함 임의 부분 문자열 검색 지원
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

    // ─────────────────────────────────────────────────────────
    // 파일 인덱스 CRUD
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 파일 메타를 삽입하거나 갱신한다.
    /// ON CONFLICT 시 has_content 는 건드리지 않아 내용 인덱스 상태를 보존한다.
    /// </summary>
    /// <param name="tx">배치 처리용 외부 트랜잭션. null 이면 자동커밋(기본값).</param>
    /// <returns>삽입 또는 갱신된 행의 id.</returns>
    public long InsertOrUpdateFile(FileIndex file, SqliteTransaction? tx = null)
    {
        using var cmd = Cmd("""
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

        var raw = cmd.ExecuteScalar();
        return Convert.ToInt64(raw);
    }

    /// <summary>
    /// 파일 내용을 FTS5 인덱스에 삽입/갱신하고 has_content = 1 로 표시한다.
    /// FTS5 는 ON CONFLICT 를 지원하지 않으므로 DELETE → INSERT 로 구현한다.
    /// </summary>
    /// <param name="outerTx">배치 처리용 외부 트랜잭션. null 이면 내부 트랜잭션을 생성한다.</param>
    public void InsertOrUpdateContent(long fileId, string content, SqliteTransaction? outerTx = null)
    {
        bool ownsTx = outerTx is null;
        var  tx     = ownsTx ? _connection!.BeginTransaction() : outerTx!;

        try
        {
            // 기존 항목 삭제 (FTS5 ON CONFLICT 미지원 → DELETE + INSERT)
            using var del = Cmd("DELETE FROM file_content WHERE file_id = $id", tx);
            del.Parameters.AddWithValue("$id", fileId);
            del.ExecuteNonQuery();

            // 새 내용 삽입
            using var ins = Cmd("INSERT INTO file_content (file_id, content) VALUES ($id, $content)", tx);
            ins.Parameters.AddWithValue("$id",      fileId);
            ins.Parameters.AddWithValue("$content", content);
            ins.ExecuteNonQuery();

            // 메타 테이블에 내용 인덱싱 완료 표시
            using var upd = Cmd("UPDATE file_index SET has_content = 1 WHERE id = $id", tx);
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

    /// <summary>
    /// file_index 및 file_content 에서 해당 파일의 모든 데이터를 삭제한다.
    /// 경로가 존재하지 않으면 아무 작업도 하지 않는다.
    /// </summary>
    public void DeleteFile(string filePath)
    {
        using var tx = _connection!.BeginTransaction();
        try
        {
            // id 조회
            using var sel = Cmd("SELECT id FROM file_index WHERE file_path = $path", tx);
            sel.Parameters.AddWithValue("$path", filePath);
            var raw = sel.ExecuteScalar();

            if (raw is null)
            {
                tx.Rollback();
                return;
            }

            var id = Convert.ToInt64(raw);

            // FTS5 내용 삭제
            using var delContent = Cmd("DELETE FROM file_content WHERE file_id = $id", tx);
            delContent.Parameters.AddWithValue("$id", id);
            delContent.ExecuteNonQuery();

            // 메타 삭제
            using var delIndex = Cmd("DELETE FROM file_index WHERE id = $id", tx);
            delIndex.Parameters.AddWithValue("$id", id);
            delIndex.ExecuteNonQuery();

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────
    // 경로 조회
    // ─────────────────────────────────────────────────────────

    /// <summary>인덱싱된 전체 파일 경로를 반환한다.</summary>
    public List<string> GetAllIndexedPaths()
    {
        using var cmd = Cmd("SELECT file_path FROM file_index ORDER BY file_path");
        return ReadStrings(cmd);
    }

    /// <summary>내용까지 인덱싱된 파일 경로만 반환한다.</summary>
    public List<string> GetContentIndexedPaths()
    {
        using var cmd = Cmd(
            "SELECT file_path FROM file_index WHERE has_content = 1 ORDER BY file_path");
        return ReadStrings(cmd);
    }

    // ─────────────────────────────────────────────────────────
    // 인덱스 관리
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// file_content(FTS5) 전체를 삭제하고 has_content 플래그를 0으로 초기화한다.
    /// file_index(파일명·메타)는 유지된다.
    /// </summary>
    public void ClearContentIndex()
    {
        using var tx = _connection!.BeginTransaction();
        try
        {
            using var del = Cmd("DELETE FROM file_content", tx);
            del.ExecuteNonQuery();

            using var upd = Cmd("UPDATE file_index SET has_content = 0", tx);
            upd.ExecuteNonQuery();

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>총 파일 수, 내용 인덱싱 파일 수, DB 파일 크기(MB)를 반환한다.</summary>
    public IndexStats GetIndexStats()
    {
        using var cmd = Cmd("""
            SELECT
                COUNT(*)                                         AS total,
                SUM(CASE WHEN has_content = 1 THEN 1 ELSE 0 END) AS content_cnt
            FROM file_index
            """);

        using var r = cmd.ExecuteReader();
        r.Read();

        int total   = r.IsDBNull(0) ? 0 : (int)r.GetInt64(0);
        int content = r.IsDBNull(1) ? 0 : (int)r.GetInt64(1);
        double mb   = new FileInfo(_dbPath) is { Exists: true } fi
            ? Math.Round(fi.Length / (1024.0 * 1024), 2)
            : 0;

        return new IndexStats(total, content, mb);
    }

    // ─────────────────────────────────────────────────────────
    // 전체 파일 조회 (Indexer.UpdateIndexAsync 전용)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 인덱싱된 모든 파일의 메타(내용 제외)를 반환한다.
    /// UpdateIndexAsync 에서 DB 스냅샷과 디스크를 비교하는 데 사용한다.
    /// </summary>
    public List<FileIndex> GetAllIndexedFiles()
    {
        using var cmd = Cmd("""
            SELECT id, file_path, file_name, extension, file_size,
                   modified_at, indexed_at, content_hash, has_content
            FROM   file_index
            ORDER  BY file_path
            """);
        return ReadFileIndexList(cmd);
    }

    // ─────────────────────────────────────────────────────────
    // 전체 초기화
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// file_index · file_content · search_history 전체를 삭제한다.
    /// 테이블 구조(스키마)는 유지된다.
    /// </summary>
    public void ClearAllIndexes()
    {
        using var tx = _connection!.BeginTransaction();
        try
        {
            using var d1 = Cmd("DELETE FROM file_content",   tx); d1.ExecuteNonQuery();
            using var d2 = Cmd("DELETE FROM file_index",     tx); d2.ExecuteNonQuery();
            using var d3 = Cmd("DELETE FROM search_history", tx); d3.ExecuteNonQuery();
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// VACUUM 으로 DB 파일을 압축하여 삭제된 데이터 공간을 회수한다.
    /// 완료 후 PRAGMA optimize 로 쿼리 플래너 통계를 갱신한다.
    /// </summary>
    public void Vacuum()
    {
        Exec("VACUUM");
        Exec("PRAGMA optimize");   // 인덱스 통계 갱신 → 쿼리 플래너 정확도 향상
    }

    /// <summary>
    /// 배치 인덱싱을 위한 트랜잭션을 시작한다.
    /// 호출자가 <c>Commit()</c> / <c>Rollback()</c> / <c>Dispose()</c>를 책임진다.
    /// </summary>
    public SqliteTransaction BeginTransaction() => _connection!.BeginTransaction();

    // ─────────────────────────────────────────────────────────
    // 검색 (Searcher.cs 전용)
    // ─────────────────────────────────────────────────────────

    /// <summary>파일명 LIKE 검색. 내용 미검색.</summary>
    public List<FileIndex> SearchByFileName(string keyword, int limit)
    {
        using var cmd = Cmd("""
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

    /// <summary>
    /// FTS5 전문 검색. SQLite snippet() 함수로 발췌문을 함께 반환한다.
    /// 2자 이하 쿼리는 trigram 인덱스 미지원이므로 LIKE 폴백으로 처리한다.
    /// </summary>
    public List<(FileIndex File, string Snippet)> SearchByContent(string keyword, int limit)
    {
        // 2자 이하: trigram 인덱스가 작동하지 않으므로 LIKE 폴백
        if (NeedsFtsLikeFallback(keyword))
            return SearchByContentLike(keyword, [], null, null, null, null, limit, 0);

        var ftsQuery = EscapeFts(keyword);

        using var cmd = Cmd("""
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
        catch (SqliteException)
        {
            // FTS 쿼리 문법 오류(특수문자 등) → 빈 목록 반환
        }
        return list;
    }

    /// <summary>
    /// 파일명 LIKE 검색 + 다중 필터 (확장자·크기·수정일).
    /// <paramref name="likePattern"/>은 이미 이스케이프된 상태여야 한다.
    /// </summary>
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

        using var cmd = Cmd(sb.ToString());
        cmd.Parameters.AddWithValue("$like",   likePattern);
        AddFilterParams(cmd, extensions, minSize, maxSize, after, before);
        cmd.Parameters.AddWithValue("$limit",  limit);
        cmd.Parameters.AddWithValue("$offset", offset);
        return ReadFileIndexList(cmd);
    }

    /// <summary>
    /// FTS5 전문 검색 + 다중 필터. snippet() 함수로 발췌문을 함께 반환한다.
    /// 2자 이하 쿼리는 trigram 미지원이므로 LIKE 폴백으로 처리한다.
    /// </summary>
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
        // ftsQuery 는 EscapeFts 가 감싼 값이므로 원문을 추출해 길이 확인
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

        using var cmd = Cmd(sb.ToString());
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
        catch (SqliteException)
        {
            // FTS 쿼리 문법 오류(특수문자 등) → 빈 목록
        }
        return list;
    }

    /// <summary>
    /// 2자 이하 키워드용 LIKE 폴백 내용 검색.
    /// file_content 테이블의 content 컬럼에 직접 LIKE '%keyword%' 로 검색한다.
    /// </summary>
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

        using var cmd = Cmd(sb.ToString());
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
                // 스니펫: 키워드 주변 60자 발췌
                var snippet = ExtractSnippet(fullContent, keyword, 60);
                list.Add((ReadFileIndex(r), snippet));
            }
        }
        catch (SqliteException) { /* 오류 시 빈 목록 */ }
        return list;
    }

    /// <summary>content 에서 keyword 주변 maxChars 자를 발췌하여 반환한다.</summary>
    private static string ExtractSnippet(string content, string keyword, int maxChars)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        var idx = content.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return content.Length > maxChars ? content[..maxChars] + "…" : content;

        int start = Math.Max(0, idx - maxChars / 2);
        int end   = Math.Min(content.Length, start + maxChars + keyword.Length);
        start     = Math.Max(0, end - maxChars - keyword.Length);

        var snippet = content[start..end].Replace('\n', ' ').Replace('\r', ' ');
        if (start > 0)         snippet = "…" + snippet;
        if (end < content.Length) snippet += "…";
        return snippet;
    }

    /// <summary>
    /// 최신 수정 파일을 다중 필터 조건으로 가져온다.
    /// 순수 초성 검색("ㅎㄱ")에서 C# 후필터용 후보 수집에 사용된다.
    /// </summary>
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

        using var cmd = Cmd(sb.ToString());
        AddFilterParams(cmd, extensions, minSize, maxSize, after, before);
        cmd.Parameters.AddWithValue("$limit",  limit);
        cmd.Parameters.AddWithValue("$offset", offset);
        return ReadFileIndexList(cmd);
    }

    // ─────────────────────────────────────────────────────────
    // 검색 히스토리
    // ─────────────────────────────────────────────────────────

    /// <summary>검색어를 히스토리에 저장한다.</summary>
    public void SaveSearchHistory(string query, bool wasContentSearch)
    {
        using var cmd = Cmd("""
            INSERT INTO search_history (query, was_content_search, searched_at)
            VALUES ($q, $isContent, $at)
            """);
        cmd.Parameters.AddWithValue("$q",         query);
        cmd.Parameters.AddWithValue("$isContent", wasContentSearch ? 1 : 0);
        cmd.Parameters.AddWithValue("$at",        DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>중복 제거 후 최근 검색어를 최대 <paramref name="limit"/>개 반환한다.</summary>
    public List<string> GetRecentSearches(int limit = 20)
    {
        using var cmd = Cmd("""
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

    /// <summary>단일 SQL 문을 파라미터 없이 실행한다.</summary>
    private void Exec(string sql)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>커맨드 객체를 생성한다.</summary>
    private SqliteCommand Cmd(string sql, SqliteTransaction? tx = null)
    {
        var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        if (tx is not null) cmd.Transaction = tx;
        return cmd;
    }

    /// <summary>null 가능한 값을 DBNull 로 변환한다.</summary>
    private static object AsDbValue(object? value) => value ?? (object)DBNull.Value;

    /// <summary>
    /// 공통 필터 조건절(AND …)을 SQL StringBuilder 에 추가한다.
    /// <paramref name="tableAlias"/>가 비어 있지 않으면 "alias.column" 형태로 생성한다.
    /// </summary>
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
            var inList = string.Join(", ",
                extensions.Select((_, i) => $"$ext{i}"));
            sb.Append($"\nAND {pfx}extension IN ({inList})");
        }
        if (minSize.HasValue) sb.Append($"\nAND {pfx}file_size   >= $minSize");
        if (maxSize.HasValue) sb.Append($"\nAND {pfx}file_size   <= $maxSize");
        if (after.HasValue)   sb.Append($"\nAND {pfx}modified_at >= $after");
        if (before.HasValue)  sb.Append($"\nAND {pfx}modified_at <= $before");
    }

    /// <summary>공통 필터 파라미터를 커맨드 오브젝트에 추가한다.</summary>
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

    /// <summary>FTS5 MATCH 쿼리에서 특수문자를 이스케이프하여 구문(phrase) 검색으로 만든다.</summary>
    private static string EscapeFts(string keyword)
    {
        // 쌍따옴표를 제거하고 구문 검색으로 감싼다 → "keyword"
        var cleaned = keyword.Replace("\"", " ").Trim();
        return string.IsNullOrEmpty(cleaned) ? "\"\"" : $"\"{cleaned}\"";
    }

    /// <summary>
    /// trigram 토크나이저는 3자 미만 쿼리를 처리하지 못하므로,
    /// 2자 이하 쿼리(주로 한국어)는 content 컬럼에 LIKE 검색으로 폴백한다.
    /// </summary>
    private bool NeedsFtsLikeFallback(string keyword) => keyword.Length < 3;

    private List<string> ReadStrings(SqliteCommand cmd)
    {
        using var r  = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private List<FileIndex> ReadFileIndexList(SqliteCommand cmd)
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
        _connection?.Dispose();
        GC.SuppressFinalize(this);
    }
}
