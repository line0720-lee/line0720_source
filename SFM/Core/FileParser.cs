using System.Diagnostics;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Docnet.Core;
using Docnet.Core.Models;
using A = DocumentFormat.OpenXml.Drawing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace SFM.Core;

/// <summary>
/// 확장자별 텍스트 추출기.
/// 모든 공개 메서드는 예외 대신 빈 문자열을 반환한다.
/// </summary>
public sealed class FileParser
{
    // ── 상수 ─────────────────────────────────────────────────
    /// <summary>최대 크기 초과 파일을 파싱할 때 읽을 바이트(문자) 한도 = 500 KB.</summary>
    private const int FallbackLimit = 500 * 1024;

    // ── 지원 확장자 집합 ─────────────────────────────────────
    private static readonly HashSet<string> _plainTextExts = new(StringComparer.OrdinalIgnoreCase)
    {
        // 일반 텍스트
        ".txt", ".log", ".csv", ".md",
        // 소스 코드
        ".cs", ".py", ".js", ".ts", ".java", ".cpp", ".c", ".h",
        ".go", ".rs", ".rb", ".php", ".swift", ".kt",
        // 마크업·설정
        ".html", ".htm", ".xml", ".json", ".yaml", ".yml",
        ".ini", ".cfg", ".toml", ".css", ".scss", ".sql",
        ".sh", ".bat", ".ps1",
    };

    private static readonly HashSet<string> _richDocExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".xlsx", ".pptx",
    };

    // ── 정적 초기화 ──────────────────────────────────────────
    static FileParser()
    {
        // EUC-KR(CP949) 등 확장 인코딩 활성화
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // ─────────────────────────────────────────────────────────
    // 공개 API
    // ─────────────────────────────────────────────────────────

    /// <summary>해당 파일의 내용 파싱 지원 여부를 반환한다 (UI 안내용).</summary>
    public bool CanParseContent(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return _plainTextExts.Contains(ext) || _richDocExts.Contains(ext);
    }

    /// <summary>
    /// 파일 내용을 텍스트로 추출한다.<br/>
    /// <paramref name="maxSizeMB"/> 초과 파일은 첫 500 KB(문자)만 파싱한다.<br/>
    /// 지원하지 않는 형식이거나 파싱 실패 시 빈 문자열을 반환한다.
    /// </summary>
    public async Task<string> ParseFileContent(string filePath, int maxSizeMB = 50)
    {
        try
        {
            if (!File.Exists(filePath)) return string.Empty;

            var ext      = Path.GetExtension(filePath);
            var info     = new FileInfo(filePath);
            var maxBytes = (long)maxSizeMB * 1024 * 1024;
            var overLimit = info.Length > maxBytes;

            // 플레인 텍스트: 파일에서 직접 바이트 읽기
            if (_plainTextExts.Contains(ext))
            {
                int readSize = overLimit ? FallbackLimit : (int)info.Length;
                return await ReadPlainTextAsync(filePath, readSize);
            }

            // 리치 문서: 파싱 후 문자 수 제한
            int charLimit = overLimit ? FallbackLimit : int.MaxValue;

            return ext.ToLowerInvariant() switch
            {
                ".pdf"  => await ParsePdfAsync(filePath, charLimit),
                ".docx" => await Task.Run(() => ParseDocx(filePath, charLimit)),
                ".xlsx" => await Task.Run(() => ParseXlsx(filePath, charLimit)),
                ".pptx" => await Task.Run(() => ParsePptx(filePath, charLimit)),
                _       => string.Empty,
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileParser] {Path.GetFileName(filePath)}: {ex.Message}");
            return string.Empty;
        }
    }

    // ─────────────────────────────────────────────────────────
    // 플레인 텍스트
    // ─────────────────────────────────────────────────────────

    private static async Task<string> ReadPlainTextAsync(string filePath, int readSize)
    {
        if (readSize <= 0) return string.Empty;

        var buffer = new byte[readSize];
        await using var fs = new FileStream(
            filePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, bufferSize: 65536, useAsync: true);

        int totalRead = 0;
        while (totalRead < readSize)
        {
            int n = await fs.ReadAsync(buffer, totalRead, readSize - totalRead);
            if (n == 0) break;
            totalRead += n;
        }

        // 실제로 읽은 크기만큼만 디코딩
        return DecodeBytes(buffer.AsSpan(0, totalRead).ToArray());
    }

    // ─────────────────────────────────────────────────────────
    // PDF — PdfiumSharp
    // ─────────────────────────────────────────────────────────

    private static Task<string> ParsePdfAsync(string filePath, int charLimit)
    {
        return Task.Run(() =>
        {
            try
            {
                using var lib = DocLib.Instance;
                using var docReader = lib.GetDocReader(
                    filePath, new PageDimensions(1080, 1920));
                var pageCount = docReader.GetPageCount();
                var sb = new StringBuilder();

                for (int i = 0; i < pageCount; i++)
                {
                    using var pageReader = docReader.GetPageReader(i);
                    var text = pageReader.GetText();
                    if (!string.IsNullOrEmpty(text))
                        sb.AppendLine(text);

                    if (sb.Length >= charLimit) break;
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FileParser/PDF] {Path.GetFileName(filePath)}: {ex.Message}");
                return string.Empty;
            }
        });
    }

    // ─────────────────────────────────────────────────────────
    // DOCX — Open XML SDK
    // ─────────────────────────────────────────────────────────

    private static string ParseDocx(string filePath, int charLimit)
    {
        using var doc = WordprocessingDocument.Open(filePath, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;

        var sb = new StringBuilder();
        foreach (var para in body.Descendants<W.Paragraph>())
        {
            var text = para.InnerText;
            if (string.IsNullOrWhiteSpace(text)) continue;

            sb.AppendLine(text);
            if (sb.Length >= charLimit) break;
        }
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────
    // XLSX — Open XML SDK
    // ─────────────────────────────────────────────────────────

    private static string ParseXlsx(string filePath, int charLimit)
    {
        using var doc = SpreadsheetDocument.Open(filePath, isEditable: false);
        var workbookPart = doc.WorkbookPart;
        if (workbookPart is null) return string.Empty;

        // 공유 문자열 테이블 로드
        var sharedStrings = workbookPart.SharedStringTablePart?
            .SharedStringTable
            .Elements<SharedStringItem>()
            .Select(s => s.InnerText)
            .ToArray() ?? [];

        var sb          = new StringBuilder();
        bool done       = false;

        foreach (var wsPart in workbookPart.WorksheetParts)
        {
            if (done) break;
            var sheetData = wsPart.Worksheet.GetFirstChild<SheetData>();
            if (sheetData is null) continue;

            foreach (var row in sheetData.Elements<Row>())
            {
                var cells = row.Elements<Cell>()
                    .Select(c => GetXlsxCellValue(c, sharedStrings))
                    .Where(v => !string.IsNullOrEmpty(v));

                sb.AppendLine(string.Join('\t', cells));

                if (sb.Length < charLimit) continue;
                done = true;
                break;
            }
        }
        return sb.ToString();
    }

    private static string GetXlsxCellValue(Cell cell, string[] sharedStrings)
    {
        var raw = cell.CellValue?.Text ?? string.Empty;

        // SharedString 타입이면 인덱스로 실제 값 조회
        if (cell.DataType?.Value == CellValues.SharedString &&
            int.TryParse(raw, out var idx) &&
            (uint)idx < (uint)sharedStrings.Length)
        {
            return sharedStrings[idx];
        }
        return raw;
    }

    // ─────────────────────────────────────────────────────────
    // PPTX — Open XML SDK
    // ─────────────────────────────────────────────────────────

    private static string ParsePptx(string filePath, int charLimit)
    {
        using var doc = PresentationDocument.Open(filePath, isEditable: false);
        var presPart = doc.PresentationPart;
        if (presPart is null) return string.Empty;

        var sb = new StringBuilder();
        foreach (var slidePart in presPart.SlideParts)
        {
            // Drawing.Text 에 슬라이드 텍스트 박스·제목 등의 텍스트가 담겨 있다
            foreach (var textElem in slidePart.Slide.Descendants<A.Text>())
            {
                if (string.IsNullOrWhiteSpace(textElem.Text)) continue;
                sb.Append(textElem.Text).Append(' ');
            }
            sb.AppendLine(); // 슬라이드 구분

            if (sb.Length >= charLimit) break;
        }
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────
    // 인코딩 자동 감지 (UTF-8 / EUC-KR 대응)
    // ─────────────────────────────────────────────────────────

    private static string DecodeBytes(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;

        // ① BOM 우선 확인
        if (bytes.Length >= 3
            && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);         // UTF-16 LE

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2); // UTF-16 BE

        // ② UTF-8 (BOM 없음) 시도 — 유효하지 않은 시퀀스가 있으면 예외 발생
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false,
                                    throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException) { /* fall through */ }

        // ③ EUC-KR (CP 949) 시도
        try
        {
            return Encoding.GetEncoding(949).GetString(bytes);
        }
        catch { /* fall through */ }

        // ④ 최종 fallback — 대체 문자(U+FFFD) 포함 가능하지만 절대 예외 없음
        return Encoding.UTF8.GetString(bytes);
    }
}
