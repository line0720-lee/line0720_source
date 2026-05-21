using SFM.Models;

namespace SFM.UI;

/// <summary>
/// ListView에 바인딩되는 검색 결과 표시용 래퍼.
/// </summary>
public class SearchResultItem
{
    private readonly SearchResult _result;

    public SearchResultItem(SearchResult result)
    {
        _result = result;
    }

    public string FileName   => _result.FileName;
    public string FilePath   => _result.FilePath;
    public string Extension  => _result.Extension.TrimStart('.').ToUpperInvariant();
    public string FileSize   => FormatSize(_result.FileSize);
    public string ModifiedAt => _result.ModifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string Snippet    => _result.Snippet;
    public SearchResult Raw  => _result;

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024             => $"{bytes} B",
        < 1024 * 1024      => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / 1024.0 / 1024:F1} MB",
        _                  => $"{bytes / 1024.0 / 1024 / 1024:F1} GB",
    };
}
