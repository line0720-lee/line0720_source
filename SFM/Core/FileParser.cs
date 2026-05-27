using System.Diagnostics;
using System.Text;
using System.IO.Compression;
using System.Xml.Linq;
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

    // ── PDF 파싱 직렬화 ──────────────────────────────────────
    // DocLib.Instance 는 프로세스 전역 싱글턴이며 thread-safe 하지 않다.
    // 동시에 여러 스레드가 접근하면 native 포인터가 중복 해제되어 프로세스가 강제 종료된다.
    // SemaphoreSlim(1,1) 으로 PDF 파싱을 직렬화하여 동시 접근을 차단한다.
    private static readonly SemaphoreSlim _pdfLock = new(1, 1);

    // ── 지원 확장자 집합 ─────────────────────────────────────
    private static readonly HashSet<string> _plainTextExts = new(StringComparer.OrdinalIgnoreCase)
    {
        // 일반 텍스트
        ".txt", ".log", ".csv", ".md",
        // 소스 코드
        ".cs", ".py", ".js", ".ts", ".java", ".cpp", ".c", ".h",
        ".go", ".rs", ".rb", ".php", ".swift", ".kt",
        ".pas", ".pc",    // Pascal / Pro*C 소스
        // 마크업·설정
        ".html", ".htm", ".xml", ".json", ".yaml", ".yml",
        ".ini", ".cfg", ".toml", ".css", ".scss", ".sql",
        ".sh", ".bat", ".ps1",
        // .rtf 는 내용 검색 제외 (서식 마크업 노이즈)
    };

    private static readonly HashSet<string> _richDocExts = new(StringComparer.OrdinalIgnoreCase)
    {
        // Open XML (Office 2007+)
        ".docx", ".xlsx", ".pptx",
        // 구형 바이너리 Office (OLE Compound, Office 97–2003)
        ".doc", ".xls", ".ppt",
        // PDF
        ".pdf",
        // 한글 문서
        ".hwp", ".hwpx",
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
                return await ReadPlainTextAsync(filePath, readSize).ConfigureAwait(false);
            }

            // 리치 문서: 파싱 후 문자 수 제한
            int charLimit = overLimit ? FallbackLimit : int.MaxValue;

            return ext.ToLowerInvariant() switch
            {
                // PDF
                ".pdf"  => await ParsePdfAsync(filePath, charLimit).ConfigureAwait(false),
                // Open XML (Office 2007+)
                ".docx" => await Task.Run(() => ParseDocx(filePath, charLimit)).ConfigureAwait(false),
                ".xlsx" => await Task.Run(() => ParseXlsx(filePath, charLimit)).ConfigureAwait(false),
                ".pptx" => await Task.Run(() => ParsePptx(filePath, charLimit)).ConfigureAwait(false),
                // 구형 바이너리 Office — OLE Compound 텍스트 흐름 추출
                ".doc" or ".xls" or ".ppt" => await Task.Run(() => ParseOleBinary(filePath, charLimit)).ConfigureAwait(false),
                // 한글 문서
                ".hwpx" => await Task.Run(() => ParseHwpx(filePath, charLimit)).ConfigureAwait(false),
                ".hwp"  => await Task.Run(() => ParseOleBinary(filePath, charLimit)).ConfigureAwait(false),
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
            int n = await fs.ReadAsync(buffer, totalRead, readSize - totalRead).ConfigureAwait(false);
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
        // Task.Run(async …) 으로 _pdfLock 대기 중에는 스레드풀 스레드를 반환하고,
        // 락을 얻은 후에만 DocLib.Instance 에 단독 접근한다.
        return Task.Run(async () =>
        {
            await _pdfLock.WaitAsync().ConfigureAwait(false);
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
            finally
            {
                _pdfLock.Release();
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
    // HWPX — ZIP 기반 XML (한글 신형 포맷)
    // ─────────────────────────────────────────────────────────

    private static string ParseHwpx(string filePath, int charLimit)
    {
        try
        {
            using var zip = ZipFile.OpenRead(filePath);
            var sb = new StringBuilder();

            // HWPX BodyText 폴더 아래 section 파일들에 단락 텍스트가 들어 있음
            var sections = zip.Entries
                .Where(e => e.FullName.StartsWith("BodyText/", StringComparison.OrdinalIgnoreCase)
                            && e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName);

            foreach (var entry in sections)
            {
                if (sb.Length >= charLimit) break;
                try
                {
                    using var stream = entry.Open();
                    var doc = XDocument.Load(stream);
                    // <hh:t> 또는 <t> 요소에 텍스트 내용이 있음
                    foreach (var t in doc.Descendants()
                                        .Where(e => e.Name.LocalName == "t"))
                    {
                        if (string.IsNullOrWhiteSpace(t.Value)) continue;
                        sb.AppendLine(t.Value);
                        if (sb.Length >= charLimit) break;
                    }
                }
                catch { /* 개별 섹션 파싱 실패 → 건너뜀 */ }
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileParser/HWPX] {Path.GetFileName(filePath)}: {ex.Message}");
            return string.Empty;
        }
    }

    // ─────────────────────────────────────────────────────────
    // OLE 복합 문서 바이너리 텍스트 추출
    // 대상: .hwp(한글) / .doc(Word) / .xls(Excel) / .ppt(PowerPoint) 구형 포맷
    // 모두 OLE Compound File 기반으로 UTF-16LE 텍스트를 저장하므로 동일 로직 사용.
    // ─────────────────────────────────────────────────────────

    private static string ParseOleBinary(string filePath, int charLimit)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                                          FileShare.ReadWrite, 4096);

            // ZIP 시그니처(PK) → 사실상 HWPX 혹은 Open XML로 저장된 파일
            Span<byte> sig4 = stackalloc byte[4];
            if (fs.Read(sig4) == 4 && sig4[0] == 0x50 && sig4[1] == 0x4B)
                return ParseHwpx(filePath, charLimit);

            // OLE 복합 문서 전체를 읽고 UTF-16LE 텍스트 흐름 추출
            // · ASCII 인쇄 가능 (0x20–0x7E)  ← Word/Excel 영문도 UTF-16LE로 저장
            // · 완성형 한글 (0xAC00–0xD7A3)
            // 연속 4자 이상인 흐름만 텍스트로 인정하여 바이너리 노이즈 차단
            fs.Position = 0;
            int readSize = (int)Math.Min(fs.Length, 4 * 1024 * 1024); // 최대 4 MB
            var bytes    = new byte[readSize];
            int total    = 0;
            while (total < readSize)
            {
                int n = fs.Read(bytes, total, readSize - total);
                if (n == 0) break;
                total += n;
            }

            var sb       = new StringBuilder();
            int runStart = -1;
            int runLen   = 0;

            for (int i = 0; i + 1 < total; i += 2)
            {
                int cp = bytes[i] | (bytes[i + 1] << 8);

                bool valid = (cp >= 0xAC00 && cp <= 0xD7A3)
                          || (cp >= 0x20   && cp <= 0x7E)
                          || cp == '\r' || cp == '\n' || cp == '\t';

                if (valid)
                {
                    if (runStart < 0) { runStart = i; runLen = 0; }
                    runLen++;
                }
                else
                {
                    if (runLen >= 4)   // 4자 이상만 의미 있는 텍스트로 취급
                    {
                        for (int j = runStart; j < runStart + runLen * 2 && j + 1 < total; j += 2)
                        {
                            int c = bytes[j] | (bytes[j + 1] << 8);
                            if ((c >= 0xAC00 && c <= 0xD7A3) || (c >= 0x20 && c <= 0x7E))
                                sb.Append((char)c);
                            else if (c == '\n') sb.AppendLine();
                        }
                        sb.AppendLine();
                    }
                    runStart = -1;
                    runLen   = 0;
                }

                if (sb.Length >= charLimit) break;
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileParser/OLE] {Path.GetFileName(filePath)}: {ex.Message}");
            return string.Empty;
        }
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
