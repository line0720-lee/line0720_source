using System.Text;
using SFM.Data;
using SFM.Models;

namespace SFM.Core;

/// <summary>
/// 파일 인덱스 검색 엔진.
/// 일반 키워드 / 한글 초성 혼합 검색 / FTS5 내용 검색을 지원한다.
/// </summary>
public sealed class Searcher
{
    // 초성 순수 검색("ㅎㄱ")처럼 LIKE 패턴이 너무 넓어질 때 DB에서 가져올 최대 후보 수.
    private const int ChoSeongCandidateMultiplier = 20;
    private const int ChoSeongCandidateMax        = 10_000;

    private readonly DatabaseManager _db;

    public Searcher(DatabaseManager db) => _db = db;

    // ─────────────────────────────────────────────────────────
    // 공개 API
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// <paramref name="query"/> 조건으로 파일을 검색한다.
    /// 검색 결과는 파일명 일치 → 내용 일치 → 최신 수정일 순으로 정렬된다.
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(SearchQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.Keyword))
            return [];

        var keyword      = query.Keyword.Trim();
        bool hasChoSeong = KoreanUtil.HasChoSeong(keyword);

        List<SearchResult> results = hasChoSeong
            ? await SearchWithChoSeongAsync(keyword, query)
            : await SearchRegularAsync(keyword, query);

        // 검색 히스토리 저장 (fire-and-forget)
        try { _db.SaveSearchHistory(keyword, query.SearchInContent && !hasChoSeong); }
        catch { /* 히스토리 저장 실패가 검색 결과에 영향 X */ }

        return results;
    }

    // ─────────────────────────────────────────────────────────
    // 일반 검색 (초성 없음)
    // ─────────────────────────────────────────────────────────

    private async Task<List<SearchResult>> SearchRegularAsync(
        string keyword, SearchQuery query)
    {
        // ── 1. 파일명 LIKE 검색 ───────────────────────────────
        int dbOffset = query.Page * query.MaxResults;

        var nameHits = await Task.Run(() => _db.SearchByNameFiltered(
            likePattern : $"%{EscapeLike(keyword)}%",
            extensions  : query.Extensions,
            minSize     : query.MinSize,
            maxSize     : query.MaxSize,
            after       : query.ModifiedAfter,
            before      : query.ModifiedBefore,
            limit       : query.MaxResults,
            offset      : dbOffset));

        var map = new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in nameHits)
            map[f.FilePath] = ToResult(f, string.Empty, matchedName: true, matchedContent: false);

        // ── 2. 내용 FTS5 검색 (옵션 ON) ──────────────────────
        if (query.SearchInContent)
        {
            var ftsQuery    = KoreanUtil.EscapeFts(keyword);
            var contentHits = await Task.Run(() => _db.SearchByContentFiltered(
                ftsQuery   : ftsQuery,
                extensions : query.Extensions,
                minSize    : query.MinSize,
                maxSize    : query.MaxSize,
                after      : query.ModifiedAfter,
                before     : query.ModifiedBefore,
                limit      : query.MaxResults,
                offset     : dbOffset));

            foreach (var (file, rawSnippet) in contentHits)
            {
                var snippet = BuildSnippet(rawSnippet, keyword);
                if (map.TryGetValue(file.FilePath, out var existing))
                {
                    existing.MatchedInContent = true;
                    existing.Snippet          = snippet;
                }
                else
                {
                    map[file.FilePath] = ToResult(file, snippet,
                        matchedName: false, matchedContent: true);
                }
            }
        }

        return Sort(map.Values, query.MaxResults);
    }

    // ─────────────────────────────────────────────────────────
    // 한글 초성 검색
    // ─────────────────────────────────────────────────────────

    private async Task<List<SearchResult>> SearchWithChoSeongAsync(
        string keyword, SearchQuery query)
    {
        bool isPure = KoreanUtil.IsPureChoSeong(keyword);

        List<FileIndex> candidates;

        if (isPure)
        {
            // 순수 초성("ㅎㄱ"): LIKE 패턴이 너무 넓으므로 최신 파일 N개를 가져와 후필터
            var limit = Math.Min(query.MaxResults * ChoSeongCandidateMultiplier,
                                 ChoSeongCandidateMax);
            candidates = await Task.Run(() => _db.GetRecentFiles(
                extensions : query.Extensions,
                minSize    : query.MinSize,
                maxSize    : query.MaxSize,
                after      : query.ModifiedAfter,
                before     : query.ModifiedBefore,
                limit      : limit));
        }
        else
        {
            // 혼합("한ㄱ", "ㅎ교"): 비초성 부분으로 LIKE 필터 후 C# 후필터
            var likePattern = KoreanUtil.ToLikePattern(keyword);
            candidates = await Task.Run(() => _db.SearchByNameFiltered(
                likePattern : likePattern,
                extensions  : query.Extensions,
                minSize     : query.MinSize,
                maxSize     : query.MaxSize,
                after       : query.ModifiedAfter,
                before      : query.ModifiedBefore,
                limit       : Math.Min(query.MaxResults * 5, 5_000)));
        }

        // C# 초성 패턴 매칭
        var matched = candidates
            .Where(f => KoreanUtil.MatchesPattern(f.FileName, keyword))
            .Take(query.MaxResults)
            .Select(f => ToResult(f, string.Empty, matchedName: true, matchedContent: false))
            .ToList();

        return Sort(matched, query.MaxResults);
    }

    // ─────────────────────────────────────────────────────────
    // 헬퍼
    // ─────────────────────────────────────────────────────────

    private static SearchResult ToResult(
        FileIndex f, string snippet, bool matchedName, bool matchedContent) => new()
    {
        FilePath         = f.FilePath,
        FileName         = f.FileName,
        Extension        = f.Extension ?? string.Empty,
        FileSize         = f.FileSize,
        ModifiedAt       = f.ModifiedAt,
        MatchedInName    = matchedName,
        MatchedInContent = matchedContent,
        Snippet          = snippet,
    };

    /// <summary>
    /// FTS5 snippet 또는 원본 텍스트에서 키워드 앞뒤 ≈50자를 발췌한다.
    /// </summary>
    private static string BuildSnippet(string rawSnippet, string keyword, int radius = 50)
    {
        // FTS5 snippet 이 이미 적절하면 그대로 사용
        if (!string.IsNullOrEmpty(rawSnippet)) return rawSnippet;
        return string.Empty;
    }

    /// <summary>파일명 일치 → 내용 일치 → 최신 수정일 순으로 정렬 후 Limit 적용.</summary>
    private static List<SearchResult> Sort(IEnumerable<SearchResult> src, int limit) =>
        src.OrderByDescending(r => r.MatchedInName    ? 1 : 0)
           .ThenByDescending (r => r.MatchedInContent ? 1 : 0)
           .ThenByDescending (r => r.ModifiedAt)
           .Take(limit)
           .ToList();

    /// <summary>SQLite LIKE 특수문자(% _ \)를 이스케이프한다.</summary>
    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

// ═════════════════════════════════════════════════════════════
// 한글 초성 유틸
// ═════════════════════════════════════════════════════════════

/// <summary>
/// 한글 초성(자음) 관련 유틸리티.
/// 유니코드 완성형 한글 체계를 이용해 초성 범위를 계산한다.
///
/// 완성형 공식: 가 = 0xAC00, 각 음절 = 초성×588 + 중성×28 + 종성 + 0xAC00
/// 초성 19개: ㄱ ㄲ ㄴ ㄷ ㄸ ㄹ ㅁ ㅂ ㅃ ㅅ ㅆ ㅇ ㅈ ㅉ ㅊ ㅋ ㅌ ㅍ ㅎ
/// </summary>
internal static class KoreanUtil
{
    // ── 초성 테이블 (Unicode 자모 순서와 동일) ────────────────
    private static readonly char[] Choseong =
    [
        'ㄱ','ㄲ','ㄴ','ㄷ','ㄸ','ㄹ','ㅁ','ㅂ','ㅃ',
        'ㅅ','ㅆ','ㅇ','ㅈ','ㅉ','ㅊ','ㅋ','ㅌ','ㅍ','ㅎ',
    ];

    private static readonly Dictionary<char, int> ChoseongIndex;

    private const int HangulBase    = 0xAC00;
    private const int NumJungseong  = 21;
    private const int NumJongseong  = 28;
    private const int SyllableBlock = NumJungseong * NumJongseong; // 588

    static KoreanUtil()
    {
        ChoseongIndex = new Dictionary<char, int>(Choseong.Length);
        for (int i = 0; i < Choseong.Length; i++)
            ChoseongIndex[Choseong[i]] = i;
    }

    // ── 분류 헬퍼 ────────────────────────────────────────────

    /// <summary>단일 문자가 초성 자음이면 true.</summary>
    public static bool IsChoseong(char c) => ChoseongIndex.ContainsKey(c);

    /// <summary>문자가 완성형 한글 음절이면 true.</summary>
    public static bool IsHangulSyllable(char c) =>
        c >= (char)HangulBase && c <= (char)(HangulBase + 19 * SyllableBlock - 1);

    /// <summary>키워드에 초성 자음이 한 자라도 포함되면 true.</summary>
    public static bool HasChoseong(string keyword) => keyword.Any(IsChoseong);

    // 외부 노출용 별칭 (대·소문자 혼용 방지)
    public static bool HasChoSeong(string keyword) => HasChoseong(keyword);

    /// <summary>키워드 전체가 초성(또는 공백)으로만 구성되면 true.</summary>
    public static bool IsPureChoSeong(string keyword) =>
        keyword.Length > 0 && keyword.All(c => IsChoseong(c) || c == ' ');

    // ── 초성 → 음절 범위 변환 ────────────────────────────────

    /// <summary>
    /// 초성 문자에 해당하는 완성형 음절 범위를 반환한다.
    /// 예) 'ㄱ' → ('가', '깋'),  'ㅎ' → ('하', '힣')
    /// </summary>
    public static (char Min, char Max) GetSyllableRange(char choseongChar)
    {
        if (!ChoseongIndex.TryGetValue(choseongChar, out var idx))
            return (choseongChar, choseongChar);   // 초성 아닌 문자는 그대로

        var min = (char)(HangulBase + idx * SyllableBlock);
        var max = (char)(HangulBase + (idx + 1) * SyllableBlock - 1);
        return (min, max);
    }

    // ── LIKE 패턴 생성 ────────────────────────────────────────

    /// <summary>
    /// 초성 자음을 SQLite LIKE 와일드카드 '_'로 치환한 패턴을 반환한다.
    /// 비초성 문자는 LIKE 특수문자를 이스케이프하여 그대로 유지한다.
    ///
    /// 예) "한ㄱ" → "%한_%",  "ㅎ교" → "%_교%",  "ㅎㄱ" → "%__%"
    /// </summary>
    public static string ToLikePattern(string keyword)
    {
        var sb = new StringBuilder("%");
        foreach (var c in keyword)
        {
            if (IsChoseong(c))
            {
                sb.Append('_');              // 초성 → 단일 문자 와일드카드
            }
            else
            {
                // LIKE 특수문자 이스케이프
                if (c is '%' or '_' or '\\') sb.Append('\\');
                sb.Append(c);
            }
        }
        sb.Append('%');
        return sb.ToString();
    }

    // ── 초성 패턴 매칭 (C# 후필터) ───────────────────────────

    /// <summary>
    /// <paramref name="fileName"/>의 연속 부분 문자열이 <paramref name="keyword"/> 패턴과
    /// 일치하면 true를 반환한다.
    ///
    /// 규칙:
    ///  - 초성 자음 → 해당 초성으로 시작하는 한글 음절 범위와 비교
    ///  - 그 외 문자 → 대소문자 무시 동등 비교
    ///
    /// 예) MatchesPattern("학교", "ㅎㄱ") → true
    ///     MatchesPattern("한국어", "한ㄱ") → true  (위치 0)
    ///     MatchesPattern("테스트", "ㅎㄱ") → false
    /// </summary>
    public static bool MatchesPattern(string fileName, string keyword)
    {
        if (string.IsNullOrEmpty(keyword) || string.IsNullOrEmpty(fileName))
            return false;

        int kLen = keyword.Length;
        int fLen = fileName.Length;

        for (int i = 0; i <= fLen - kLen; i++)
        {
            bool match = true;
            for (int j = 0; j < kLen; j++)
            {
                char kc = keyword[j];
                char fc = fileName[i + j];

                if (IsChoseong(kc))
                {
                    var (min, max) = GetSyllableRange(kc);
                    if (fc < min || fc > max) { match = false; break; }
                }
                else if (char.ToLowerInvariant(fc) != char.ToLowerInvariant(kc))
                {
                    match = false; break;
                }
            }
            if (match) return true;
        }
        return false;
    }

    // ── FTS5 이스케이프 ──────────────────────────────────────

    /// <summary>
    /// FTS5 MATCH 쿼리에 사용할 안전한 구문(phrase) 검색 문자열로 변환한다.
    /// 쌍따옴표를 제거하고 전체를 따옴표로 감싼다.
    /// </summary>
    public static string EscapeFts(string keyword)
    {
        var cleaned = keyword.Replace("\"", " ").Trim();
        return string.IsNullOrEmpty(cleaned) ? "\"\"" : $"\"{cleaned}\"";
    }

    // ── 진단용 ───────────────────────────────────────────────

    /// <summary>
    /// 초성 문자를 해당 범위의 첫 음절(최솟값)로 변환한다. 비초성은 그대로.
    /// 예) 'ㅎ' → '하',  'ㄱ' → '가'
    /// </summary>
    public static char ToMinSyllable(char choseongChar)
    {
        var (min, _) = GetSyllableRange(choseongChar);
        return min;
    }

    /// <summary>
    /// 초성 문자를 해당 범위의 마지막 음절(최댓값)로 변환한다. 비초성은 그대로.
    /// 예) 'ㅎ' → '힣',  'ㄱ' → '깋'
    /// </summary>
    public static char ToMaxSyllable(char choseongChar)
    {
        var (_, max) = GetSyllableRange(choseongChar);
        return max;
    }
}
