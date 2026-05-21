namespace SFM.Models;

public class SearchResult
{
    public string   FilePath         { get; set; } = string.Empty;
    public string   FileName         { get; set; } = string.Empty;
    public string   Extension        { get; set; } = string.Empty;
    public long     FileSize         { get; set; }
    public DateTime ModifiedAt       { get; set; }

    /// <summary>파일명에서 키워드가 일치했으면 true.</summary>
    public bool     MatchedInName    { get; set; }

    /// <summary>파일 내용에서 키워드가 일치했으면 true.</summary>
    public bool     MatchedInContent { get; set; }

    /// <summary>내용 일치 시 키워드 앞뒤 ≈50자 발췌. 파일명 일치만이면 빈 문자열.</summary>
    public string   Snippet          { get; set; } = string.Empty;
}
