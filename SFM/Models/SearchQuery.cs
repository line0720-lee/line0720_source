namespace SFM.Models;

public class SearchQuery
{
    /// <summary>검색어. 한글 초성(ㅎㄱ 등)도 지원.</summary>
    public string Keyword { get; set; } = string.Empty;

    /// <summary>true: 파일명 + 내용(FTS5) 동시 검색 / false: 파일명만.</summary>
    public bool SearchInContent { get; set; } = true;

    /// <summary>확장자 화이트리스트. 빈 배열 = 전체.</summary>
    public string[] Extensions { get; set; } = [];

    /// <summary>최소 파일 크기(바이트). null = 제한 없음.</summary>
    public long? MinSize { get; set; }

    /// <summary>최대 파일 크기(바이트). null = 제한 없음.</summary>
    public long? MaxSize { get; set; }

    /// <summary>이 날짜 이후 수정된 파일만. null = 제한 없음.</summary>
    public DateTime? ModifiedAfter { get; set; }

    /// <summary>이 날짜 이전 수정된 파일만. null = 제한 없음.</summary>
    public DateTime? ModifiedBefore { get; set; }

    /// <summary>최대 반환 결과 수.</summary>
    public int MaxResults { get; set; } = 200;

    /// <summary>페이지 번호 (0-based). DB 쿼리 OFFSET = Page × MaxResults.</summary>
    public int Page { get; set; } = 0;
}
