namespace SFM.Models;

/// <summary>
/// file_index 테이블 행 매핑 모델.
/// DB 컬럼 순서: id, file_path, file_name, extension, file_size,
///               modified_at, indexed_at, content_hash, has_content
/// </summary>
public class FileIndex
{
    public long    Id          { get; set; }
    public string  FilePath    { get; set; } = string.Empty;
    public string  FileName    { get; set; } = string.Empty;
    public string? Extension   { get; set; }
    public long    FileSize    { get; set; }
    public DateTime ModifiedAt { get; set; }
    public DateTime IndexedAt  { get; set; }

    /// <summary>변경 감지용 해시. 파일 경로+수정일+크기의 SHA-256.</summary>
    public string? ContentHash { get; set; }

    /// <summary>true: file_content FTS 인덱스에 내용이 있음.</summary>
    public bool HasContent { get; set; }
}
