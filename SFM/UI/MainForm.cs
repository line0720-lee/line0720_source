using System.Diagnostics;
using System.Text.RegularExpressions;
using SFM.Core;
using SFM.Data;
using SFM.Models;

namespace SFM.UI;

public partial class MainForm : Form
{
    // ─────────────────────────────────────────────────────────
    // 서비스 레이어
    // ─────────────────────────────────────────────────────────
    private readonly AppSettings     _settings;
    private readonly DatabaseManager _db;
    private readonly Indexer         _indexer;
    private readonly Searcher        _searcher;
    private readonly FileWatcher     _watcher;

    // ─────────────────────────────────────────────────────────
    // UI 상태
    // ─────────────────────────────────────────────────────────
    private List<SearchResult> _results      = [];
    private int                _currentPage  = 0;
    private string             _lastKeyword  = string.Empty;

    private ToolStripButton _sslPrevPage = null!;
    private ToolStripButton _sslNextPage = null!;

    private readonly System.Windows.Forms.Timer _debounceTimer;
    private readonly ListBox                    _recentBox;

    // ── 컬럼 정렬 상태 ────────────────────────────────────────
    private int  _sortColumn    = -1;       // 마지막 정렬 컬럼 인덱스
    private bool _sortAscending = true;

    // ── 컬럼 헤더 원본 텍스트 (정렬 화살표 토글용) ────────────
    private readonly string[] _colHeaders = ["파일명", "경로", "크기", "수정일", "일치"];

    // ─────────────────────────────────────────────────────────
    // 파일유형 필터
    // ─────────────────────────────────────────────────────────
    private static readonly string[][] FilterExtensions =
    [
        [],
        [".docx",".doc",".xlsx",".xls",".pptx",".ppt",".txt",".md",".rtf"],
        [".pdf"],
        [".jpg",".jpeg",".png",".gif",".bmp",".svg",".webp",".ico"],
        [".cs",".py",".js",".ts",".java",".cpp",".c",".h",
         ".go",".rs",".rb",".php",".swift",".kt",
         ".html",".htm",".css",".scss",".sql",".sh",".bat",".ps1"],
    ];

    // ─────────────────────────────────────────────────────────
    // 파일 아이콘
    // ─────────────────────────────────────────────────────────
    private static readonly Dictionary<string, int> ExtImageIndex =
        new(StringComparer.OrdinalIgnoreCase)
        {
            {".docx",1},{".doc",1},{".xlsx",1},{".xls",1},
            {".pptx",1},{".ppt",1},{".txt",1},{".md",1},{".rtf",1},
            {".pdf",2},
            {".jpg",3},{".jpeg",3},{".png",3},{".gif",3},
            {".bmp",3},{".svg",3},{".webp",3},{".ico",3},
            {".cs",4},{".py",4},{".js",4},{".ts",4},{".java",4},
            {".cpp",4},{".c",4},{".h",4},{".go",4},{".rs",4},
            {".rb",4},{".php",4},{".swift",4},{".kt",4},
            {".html",4},{".htm",4},{".css",4},{".scss",4},
            {".sql",4},{".sh",4},{".bat",4},{".ps1",4},
        };

    // ─────────────────────────────────────────────────────────
    // 생성자
    // ─────────────────────────────────────────────────────────
    public MainForm()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _db       = new DatabaseManager(_settings.DatabasePath);
        _indexer  = new Indexer(_db, new FileParser(), _settings);
        _searcher = new Searcher(_db);
        _watcher  = new FileWatcher(_indexer, _settings);

        _db.Initialize();

        _debounceTimer          = new System.Windows.Forms.Timer { Interval = 250 };
        _debounceTimer.Tick    += OnDebounceTimerTick;

        _recentBox = new ListBox
        {
            Visible        = false,
            BorderStyle    = BorderStyle.FixedSingle,
            IntegralHeight = false,
            Font           = new Font("Segoe UI", 10F),
        };
        _recentBox.MouseClick += OnRecentBoxClick;
        _recentBox.KeyDown    += OnRecentBoxKeyDown;
        Controls.Add(_recentBox);

        InitImageList();
        InitPagination();
        WireEvents();
        ApplyTheme();
        RestoreWindowBounds();
        RefreshDbStats();

        _watcher.StartWatching(_settings.IndexRootPaths);
        ActiveControl = txtSearch;
    }

    // ─────────────────────────────────────────────────────────
    // 창 위치 복원
    // ─────────────────────────────────────────────────────────
    private void RestoreWindowBounds()
    {
        if (_settings.WindowMaximized)
        {
            WindowState = FormWindowState.Maximized;
            return;
        }

        if (_settings.WindowX >= 0 && _settings.WindowY >= 0)
        {
            StartPosition = FormStartPosition.Manual;
            Location      = new Point(_settings.WindowX, _settings.WindowY);
        }
        Size = new Size(_settings.WindowWidth, _settings.WindowHeight);
    }

    // ─────────────────────────────────────────────────────────
    // 페이지네이션 초기화
    // ─────────────────────────────────────────────────────────
    private void InitPagination()
    {
        _sslPrevPage = new ToolStripButton("◀")
        {
            Enabled     = false,
            Alignment   = ToolStripItemAlignment.Right,
            ToolTipText = "이전 페이지 (Alt+←)",
            Padding     = new Padding(4, 0, 4, 0),
        };
        _sslNextPage = new ToolStripButton("▶")
        {
            Enabled     = false,
            Alignment   = ToolStripItemAlignment.Right,
            ToolTipText = "다음 페이지 (Alt+→)",
            Padding     = new Padding(4, 0, 4, 0),
        };

        _sslPrevPage.Click += (_, _) =>
        {
            if (_currentPage > 0) { _currentPage--; _ = RunSearchAsync(); }
        };
        _sslNextPage.Click += (_, _) =>
        {
            _currentPage++;
            _ = RunSearchAsync();
        };

        statusStrip.Items.AddRange([_sslPrevPage, _sslNextPage]);
    }

    // ─────────────────────────────────────────────────────────
    // 이벤트 연결
    // ─────────────────────────────────────────────────────────
    private void WireEvents()
    {
        // ── 검색창 ───────────────────────────────────────────
        txtSearch.TextChanged += (_, _) => { _debounceTimer.Stop(); _debounceTimer.Start(); };
        txtSearch.Enter       += OnSearchBoxEnter;
        txtSearch.Leave       += OnSearchBoxLeave;
        txtSearch.KeyDown     += OnSearchBoxKeyDown;

        // ── 옵션 체크박스 ────────────────────────────────────
        chkContent.CheckedChanged += (_, _) =>
        {
            _settings.EnableContentSearch = chkContent.Checked;
            tsmiSearchContent.Checked     = chkContent.Checked;
            _ = RunSearchAsync();
        };
        chkCase.CheckedChanged  += (_, _) => _ = RunSearchAsync();
        chkWholeWord.CheckedChanged += (_, _) => _ = RunSearchAsync();

        // ── 파일유형 필터 ────────────────────────────────────
        cmbFilter.SelectedIndexChanged += (_, _) => _ = RunSearchAsync();

        // ── ListView ─────────────────────────────────────────
        listResults.RetrieveVirtualItem    += OnRetrieveVirtualItem;
        listResults.DoubleClick            += (_, _) => OpenSelectedFile();
        listResults.KeyDown                += OnListKeyDown;
        listResults.SelectedIndexChanged   += OnListSelectionChanged;
        listResults.ColumnClick            += OnColumnClick;

        // ── ContextMenu ──────────────────────────────────────
        cmsResult.Opening          += (_, e) => e.Cancel = listResults.SelectedIndices.Count == 0;
        tsmiOpenFile.Click         += (_, _) => OpenSelectedFile();
        tsmiOpenFolder.Click       += (_, _) => OpenSelectedFolder();
        tsmiCopyPath.Click         += (_, _) => CopySelectedPaths();
        tsmiCopyName.Click         += (_, _) => CopySelectedNames();
        tsmiDeleteFromIndex.Click  += OnDeleteFromIndex;

        // ── 스니펫 닫기 버튼 ─────────────────────────────────
        btnSnippetClose.Click += (_, _) =>
        {
            pnlSnippet.Visible          = false;
            tsmiViewSnippet.Checked     = false;
        };

        // ── 메뉴: 파일 ───────────────────────────────────────
        tsmiFileNewWindow.Click += (_, _) =>
        {
            var f = new MainForm();
            f.Show();
        };
        tsmiFileExit.Click += (_, _) => Close();

        // ── 메뉴: 보기 ───────────────────────────────────────
        tsmiViewDark.Click += (_, _) =>
        {
            _settings.IsDarkMode = tsmiViewDark.Checked;
            ApplyTheme();
        };
        tsmiViewSnippet.Click += (_, _) =>
        {
            pnlSnippet.Visible = tsmiViewSnippet.Checked;
        };
        tsmiViewAutoCol.Click += (_, _) => AutoResizeColumns();

        // ── 메뉴: 검색 ───────────────────────────────────────
        tsmiSearchContent.Click += (_, _) =>
        {
            chkContent.Checked = tsmiSearchContent.Checked;
        };
        tsmiSearchCase.Click += (_, _) =>
        {
            chkCase.Checked = tsmiSearchCase.Checked;
        };
        tsmiSearchWhole.Click += (_, _) =>
        {
            chkWholeWord.Checked = tsmiSearchWhole.Checked;
        };
        tsmiSearchClear.Click += (_, _) =>
        {
            txtSearch.Clear();
            txtSearch.Focus();
        };

        // ── 메뉴: 도구 ───────────────────────────────────────
        tsmiToolsIndex.Click   += OnSettingsClick;
        tsmiToolsReindex.Click += OnReindexClick;
        tsmiToolsContent.Click += OnBuildContentClick;
        tsmiToolsVacuum.Click  += OnVacuumClick;

        // ── 메뉴: 도움말 ─────────────────────────────────────
        tsmiHelpAbout.Click += (_, _) =>
        {
            MessageBox.Show(
                "SFM — Smart File Manager v1.0\n\n" +
                "로컬 파일 전문 검색 도구\n" +
                "파일명 + 내용 인덱싱 · 한국어 초성 검색 지원\n\n" +
                "단축키\n" +
                "  Ctrl+F    검색창 포커스\n" +
                "  F4        내용 검색 토글\n" +
                "  F5        전체 재인덱싱\n" +
                "  Enter     파일 열기\n" +
                "  Ctrl+C    경로 복사\n" +
                "  Alt+←/→  이전/다음 페이지\n" +
                "  Esc       검색어 지우기",
                "SFM 정보",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };

        // ── Indexer 이벤트 ───────────────────────────────────
        _indexer.IndexProgressChanged += p => Invoke(() =>
            sslIndexStatus.Text =
                $"[{p.Phase}] {p.Processed:N0}/{p.Total:N0}  {Path.GetFileName(p.FilePath)}");

        _indexer.IndexCompleted += r => Invoke(() =>
        {
            sslIndexStatus.Text = $"완료 {r.TotalIndexed:N0}개  +{r.Added} ~{r.Updated} -{r.Deleted}  {r.Elapsed.TotalSeconds:F1}s";
            sslLastIndexed.Text = $"인덱싱: {DateTime.Now:HH:mm}";
            RefreshDbStats();
            if (!string.IsNullOrWhiteSpace(txtSearch.Text))
                _ = RunSearchAsync();
        });

        _indexer.IndexErrorOccurred += err =>
            Debug.WriteLine($"[IndexError] {err.FilePath}: {err.Message}");
    }

    // ─────────────────────────────────────────────────────────
    // 테마
    // ─────────────────────────────────────────────────────────
    private void ApplyTheme()
    {
        tsmiViewDark.Checked = _settings.IsDarkMode;
        if (_settings.IsDarkMode) ApplyDarkTheme();
        else                      ApplyLightTheme();
    }

    private void ApplyDarkTheme()
    {
        var bg   = Color.FromArgb(28,  28,  28);
        var surf = Color.FromArgb(45,  45,  48);
        var text = Color.FromArgb(220, 220, 220);
        var gray = Color.FromArgb(140, 140, 140);

        BackColor = bg;
        ForeColor = text;

        menuStrip.BackColor = Color.FromArgb(37, 37, 38);
        menuStrip.ForeColor = text;
        foreach (ToolStripMenuItem m in menuStrip.Items.OfType<ToolStripMenuItem>())
            m.ForeColor = text;

        pnlSearch.BackColor   = Color.FromArgb(37, 37, 38);
        pnlOptions.BackColor  = Color.FromArgb(37, 37, 38);

        txtSearch.BackColor   = surf;
        txtSearch.ForeColor   = text;
        txtSearch.BorderStyle = BorderStyle.FixedSingle;

        cmbFilter.BackColor = surf;
        cmbFilter.ForeColor = text;
        chkContent.ForeColor = text;
        chkCase.ForeColor    = text;
        chkWholeWord.ForeColor = text;
        lblDbInfo.ForeColor  = gray;
        lblFilter.ForeColor  = text;

        listResults.BackColor = Color.FromArgb(25, 25, 25);
        listResults.ForeColor = text;

        pnlSnippet.BackColor    = Color.FromArgb(35, 35, 35);
        pnlSnippetBar.BackColor = Color.FromArgb(50, 50, 50);
        lblSnippetIcon.ForeColor = text;
        lblSnippetPath.ForeColor = gray;
        btnSnippetClose.BackColor = Color.FromArgb(50, 50, 50);
        btnSnippetClose.ForeColor = gray;
        rtbSnippet.BackColor    = Color.FromArgb(25, 25, 25);
        rtbSnippet.ForeColor    = text;

        statusStrip.BackColor = surf;
        statusStrip.ForeColor = text;
        foreach (ToolStripItem item in statusStrip.Items)
            item.ForeColor = text;
        _sslPrevPage.ForeColor = text;
        _sslNextPage.ForeColor = text;

        _recentBox.BackColor = surf;
        _recentBox.ForeColor = text;
    }

    private void ApplyLightTheme()
    {
        var accentGray = Color.FromArgb(243, 243, 243);

        BackColor = SystemColors.Window;
        ForeColor = SystemColors.WindowText;

        menuStrip.BackColor = SystemColors.MenuBar;
        menuStrip.ForeColor = SystemColors.MenuText;

        pnlSearch.BackColor  = accentGray;
        pnlOptions.BackColor = accentGray;

        txtSearch.BackColor   = SystemColors.Window;
        txtSearch.ForeColor   = SystemColors.WindowText;
        txtSearch.BorderStyle = BorderStyle.FixedSingle;

        cmbFilter.BackColor = SystemColors.Window;
        cmbFilter.ForeColor = SystemColors.WindowText;
        chkContent.ForeColor   = SystemColors.WindowText;
        chkCase.ForeColor      = SystemColors.WindowText;
        chkWholeWord.ForeColor = SystemColors.WindowText;
        lblDbInfo.ForeColor    = SystemColors.GrayText;
        lblFilter.ForeColor    = SystemColors.WindowText;

        listResults.BackColor = SystemColors.Window;
        listResults.ForeColor = SystemColors.WindowText;

        pnlSnippet.BackColor    = Color.FromArgb(252, 252, 252);
        pnlSnippetBar.BackColor = Color.FromArgb(235, 235, 235);
        lblSnippetIcon.ForeColor = SystemColors.WindowText;
        lblSnippetPath.ForeColor = SystemColors.GrayText;
        btnSnippetClose.BackColor = Color.FromArgb(235, 235, 235);
        btnSnippetClose.ForeColor = SystemColors.GrayText;
        rtbSnippet.BackColor    = Color.FromArgb(252, 252, 252);
        rtbSnippet.ForeColor    = SystemColors.WindowText;

        statusStrip.BackColor = accentGray;
        statusStrip.ForeColor = SystemColors.WindowText;
        foreach (ToolStripItem item in statusStrip.Items)
            item.ForeColor = SystemColors.WindowText;
        _sslPrevPage.ForeColor = SystemColors.WindowText;
        _sslNextPage.ForeColor = SystemColors.WindowText;

        _recentBox.BackColor = SystemColors.Window;
        _recentBox.ForeColor = SystemColors.WindowText;
    }

    // ─────────────────────────────────────────────────────────
    // 파일 아이콘
    // ─────────────────────────────────────────────────────────
    private void InitImageList()
    {
        Color[] palette =
        [
            Color.FromArgb(130, 130, 130),  // 0: 기본
            Color.FromArgb( 60, 120, 200),  // 1: 문서
            Color.FromArgb(200,  60,  60),  // 2: PDF
            Color.FromArgb(140,  70, 200),  // 3: 이미지
            Color.FromArgb( 50, 160,  90),  // 4: 코드
        ];
        foreach (var clr in palette)
            imgListSmall.Images.Add(MakeFileIcon(clr));  // ImageList가 소유 — 여기서 dispose 금지
    }

    private static Bitmap MakeFileIcon(Color color)
    {
        var bmp = new Bitmap(16, 16);
        using var g    = Graphics.FromImage(bmp);
        using var fill = new SolidBrush(color);
        using var pen  = new Pen(Color.FromArgb(80, Color.Black));
        using var line = new Pen(Color.FromArgb(90, Color.White), 1f);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        // 파일 모양 (직각 + 접힌 모서리)
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddLines(new PointF[] {
            new(2, 1), new(10, 1), new(13, 4), new(13, 14),
            new(2, 14), new(2, 1)
        });
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
        // 접힌 모서리
        using var fold = new SolidBrush(Color.FromArgb(60, Color.White));
        g.FillPolygon(fold, new PointF[] { new(10, 1), new(13, 4), new(10, 4) });
        // 줄무늬
        g.DrawLine(line, 4, 7, 11, 7);
        g.DrawLine(line, 4, 9, 11, 9);
        g.DrawLine(line, 4, 11, 9, 11);
        return bmp;
    }

    private static int GetImageIndex(string? ext) =>
        !string.IsNullOrEmpty(ext) && ExtImageIndex.TryGetValue(ext, out var idx) ? idx : 0;

    // ─────────────────────────────────────────────────────────
    // VirtualMode
    // ─────────────────────────────────────────────────────────
    private void OnRetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if (e.ItemIndex >= _results.Count) return;
        e.Item = CreateListViewItem(_results[e.ItemIndex]);
    }

    private ListViewItem CreateListViewItem(SearchResult r)
    {
        var lvi = new ListViewItem(r.FileName, GetImageIndex(r.Extension));
        lvi.SubItems.Add(Path.GetDirectoryName(r.FilePath) ?? r.FilePath);
        lvi.SubItems.Add(FormatSize(r.FileSize));
        lvi.SubItems.Add(r.ModifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        lvi.SubItems.Add(FormatMatchType(r));
        lvi.ToolTipText = r.FilePath;
        return lvi;
    }

    // ─────────────────────────────────────────────────────────
    // 컬럼 정렬
    // ─────────────────────────────────────────────────────────
    private void OnColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (_results.Count == 0) return;

        if (_sortColumn == e.Column)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn    = e.Column;
            _sortAscending = true;
        }

        _results = SortResults(_results, _sortColumn, _sortAscending);

        // 헤더 텍스트에 정렬 방향 표시
        for (int i = 0; i < listResults.Columns.Count; i++)
        {
            listResults.Columns[i].Text = i == _sortColumn
                ? _colHeaders[i] + (_sortAscending ? "  ▲" : "  ▼")
                : _colHeaders[i];
        }

        listResults.Invalidate();
    }

    private static List<SearchResult> SortResults(
        List<SearchResult> src, int col, bool asc)
    {
        IOrderedEnumerable<SearchResult> ordered = col switch
        {
            0 => asc ? src.OrderBy(r => r.FileName,  StringComparer.OrdinalIgnoreCase)
                     : src.OrderByDescending(r => r.FileName, StringComparer.OrdinalIgnoreCase),
            1 => asc ? src.OrderBy(r => Path.GetDirectoryName(r.FilePath) ?? "",
                                        StringComparer.OrdinalIgnoreCase)
                     : src.OrderByDescending(r => Path.GetDirectoryName(r.FilePath) ?? "",
                                        StringComparer.OrdinalIgnoreCase),
            2 => asc ? src.OrderBy(r => r.FileSize)
                     : src.OrderByDescending(r => r.FileSize),
            3 => asc ? src.OrderBy(r => r.ModifiedAt)
                     : src.OrderByDescending(r => r.ModifiedAt),
            _ => asc ? src.OrderBy(r => r.MatchedInName ? 0 : 1)
                     : src.OrderByDescending(r => r.MatchedInName ? 0 : 1),
        };
        return ordered.ToList();
    }

    // ─────────────────────────────────────────────────────────
    // 스니펫 패널
    // ─────────────────────────────────────────────────────────
    private void OnListSelectionChanged(object? sender, EventArgs e)
    {
        if (!pnlSnippet.Visible) return;
        var r = GetSelectedResult();
        UpdateSnippetPanel(r);
    }

    private void UpdateSnippetPanel(SearchResult? r)
    {
        if (r is null)
        {
            lblSnippetPath.Text = "항목을 선택하면 미리보기가 표시됩니다";
            rtbSnippet.Clear();
            return;
        }

        lblSnippetPath.Text = r.FilePath;

        rtbSnippet.Clear();
        if (!string.IsNullOrWhiteSpace(r.Snippet))
        {
            rtbSnippet.Text = r.Snippet;
            HighlightKeywordInSnippet(txtSearch.Text.Trim());
        }
        else
        {
            rtbSnippet.ForeColor = SystemColors.GrayText;
            rtbSnippet.Text      = r.MatchedInContent
                ? "(내용 미리보기를 가져오는 중…)"
                : "(파일명으로 일치)";
        }
    }

    private void HighlightKeywordInSnippet(string keyword)
    {
        if (string.IsNullOrEmpty(keyword) || string.IsNullOrEmpty(rtbSnippet.Text)) return;

        var text       = rtbSnippet.Text;
        var comparison = chkCase.Checked
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        int start = 0;
        while (true)
        {
            int idx = text.IndexOf(keyword, start, comparison);
            if (idx < 0) break;

            rtbSnippet.Select(idx, keyword.Length);
            rtbSnippet.SelectionBackColor = Color.FromArgb(255, 240, 0);
            rtbSnippet.SelectionColor     = Color.Black;
            start = idx + keyword.Length;
        }
        rtbSnippet.SelectionLength = 0;
        rtbSnippet.ScrollToCaret();
    }

    // ─────────────────────────────────────────────────────────
    // 검색
    // ─────────────────────────────────────────────────────────
    private void OnDebounceTimerTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        _ = RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        var keyword = txtSearch.Text.Trim();

        if (keyword.Length < 2)
        {
            _results      = [];
            _currentPage  = 0;
            _lastKeyword  = string.Empty;
            listResults.VirtualListSize = 0;
            sslResultCount.Text         = "준비";
            _sslPrevPage.Enabled        = false;
            _sslNextPage.Enabled        = false;
            ResetSortHeaders();
            UpdateSnippetPanel(null);
            Text = "SFM — Smart File Manager";
            return;
        }

        if (keyword != _lastKeyword)
        {
            _currentPage = 0;
            _lastKeyword = keyword;
            ResetSortHeaders();
        }

        var filterIdx = Math.Clamp(cmbFilter.SelectedIndex, 0, FilterExtensions.Length - 1);
        var query = new SearchQuery
        {
            Keyword         = keyword,
            MaxResults      = _settings.MaxSearchResults,
            SearchInContent = chkContent.Checked,
            Extensions      = FilterExtensions[filterIdx],
            Page            = _currentPage,
        };

        sslResultCount.Text  = "검색 중…";
        _sslPrevPage.Enabled = false;
        _sslNextPage.Enabled = false;

        var results = await _searcher.SearchAsync(query);

        if (txtSearch.Text.Trim() != keyword) return;  // stale guard

        _results                    = results;
        _sortColumn                 = -1;       // 정렬 리셋
        listResults.VirtualListSize = _results.Count;
        listResults.Invalidate();

        _sslPrevPage.Enabled = _currentPage > 0;
        _sslNextPage.Enabled = _results.Count >= _settings.MaxSearchResults;

        string pageInfo = _currentPage > 0 ? $"  p.{_currentPage + 1}" : string.Empty;
        sslResultCount.Text = _results.Count switch
        {
            0 => _currentPage > 0 ? "마지막 페이지" : "결과 없음",
            _ => $"{_results.Count:N0}개 결과{pageInfo}",
        };

        // 타이틀바 업데이트
        Text = _results.Count > 0
            ? $"SFM — \"{keyword}\" ({_results.Count:N0}개)"
            : "SFM — Smart File Manager";

        // 첫 번째 항목 선택 시 스니펫 자동 갱신
        if (_results.Count > 0 && pnlSnippet.Visible)
            UpdateSnippetPanel(_results[0]);
    }

    private void ResetSortHeaders()
    {
        _sortColumn    = -1;
        _sortAscending = true;
        for (int i = 0; i < listResults.Columns.Count; i++)
            listResults.Columns[i].Text = _colHeaders[i];
    }

    // ─────────────────────────────────────────────────────────
    // 최근 검색어 드롭다운
    // ─────────────────────────────────────────────────────────
    private void OnSearchBoxEnter(object? sender, EventArgs e) => ShowRecentDropdown();

    private void OnSearchBoxLeave(object? sender, EventArgs e)
        => BeginInvoke(() => _recentBox.Visible = false);

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down when _recentBox.Visible:
                _recentBox.Focus();
                if (_recentBox.Items.Count > 0) _recentBox.SelectedIndex = 0;
                e.Handled = true;
                break;
            case Keys.Escape:
                if (_recentBox.Visible)
                    _recentBox.Visible = false;
                else
                    txtSearch.Clear();
                e.Handled = true;
                break;
            case Keys.Enter:
                _debounceTimer.Stop();
                _ = RunSearchAsync();
                e.Handled = true;
                break;
        }
    }

    private void OnRecentBoxClick(object? sender, MouseEventArgs e)
    {
        if (_recentBox.SelectedItem is string sel)
        {
            txtSearch.Text           = sel;
            txtSearch.SelectionStart = sel.Length;
        }
        _recentBox.Visible = false;
        txtSearch.Focus();
    }

    private void OnRecentBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && _recentBox.SelectedItem is string sel)
        {
            txtSearch.Text     = sel;
            _recentBox.Visible = false;
            txtSearch.Focus();
            _ = RunSearchAsync();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _recentBox.Visible = false;
            txtSearch.Focus();
            e.Handled = true;
        }
    }

    private void ShowRecentDropdown()
    {
        var recents = _db.GetRecentSearches(8);
        if (recents.Count == 0) return;

        _recentBox.Items.Clear();
        foreach (var s in recents) _recentBox.Items.Add(s);

        var screenPt = txtSearch.Parent!.PointToScreen(txtSearch.Location);
        var formPt   = PointToClient(screenPt);
        _recentBox.Location = new Point(formPt.X, pnlSearch.Bottom);
        _recentBox.Width    = txtSearch.Width;
        _recentBox.Height   = Math.Min(recents.Count * 22 + 4, 180);

        _recentBox.BringToFront();
        _recentBox.Visible = true;
    }

    // ─────────────────────────────────────────────────────────
    // 키보드 단축키 (폼 레벨)
    // ─────────────────────────────────────────────────────────
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            // Ctrl+F — 검색창 포커스
            case Keys.Control | Keys.F:
                txtSearch.Focus();
                txtSearch.SelectAll();
                return true;

            // Ctrl+C — 경로 복사 (ListView 포커스 시)
            case Keys.Control | Keys.C when listResults.Focused:
                CopySelectedPaths();
                return true;

            // Alt+← — 이전 페이지
            case Keys.Alt | Keys.Left when _sslPrevPage.Enabled:
                _currentPage--;
                _ = RunSearchAsync();
                return true;

            // Alt+→ — 다음 페이지
            case Keys.Alt | Keys.Right when _sslNextPage.Enabled:
                _currentPage++;
                _ = RunSearchAsync();
                return true;

            // F4 — 내용 검색 토글
            case Keys.F4:
                chkContent.Checked        = !chkContent.Checked;
                tsmiSearchContent.Checked = chkContent.Checked;
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ─────────────────────────────────────────────────────────
    // ListView 키 핸들러
    // ─────────────────────────────────────────────────────────
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter or Keys.Return:
                OpenSelectedFile();
                e.Handled = true;
                break;
            case Keys.Delete:
                OnDeleteFromIndex(sender, e);
                e.Handled = true;
                break;
        }
    }

    // ─────────────────────────────────────────────────────────
    // 파일 동작
    // ─────────────────────────────────────────────────────────
    private SearchResult? GetSelectedResult()
    {
        if (listResults.SelectedIndices.Count == 0) return null;
        var idx = listResults.SelectedIndices[0];
        return idx < _results.Count ? _results[idx] : null;
    }

    private IEnumerable<SearchResult> GetSelectedResults()
    {
        foreach (int idx in listResults.SelectedIndices)
            if (idx < _results.Count) yield return _results[idx];
    }

    private void OpenSelectedFile()
    {
        var r = GetSelectedResult();
        if (r is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(r.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 열 수 없습니다.\n{ex.Message}",
                "SFM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenSelectedFolder()
    {
        var r = GetSelectedResult();
        if (r is null) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe",
                $"/select,\"{r.FilePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"폴더를 열 수 없습니다.\n{ex.Message}",
                "SFM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CopySelectedPaths()
    {
        var paths = string.Join(Environment.NewLine,
            GetSelectedResults().Select(r => r.FilePath));
        if (!string.IsNullOrEmpty(paths))
            try { Clipboard.SetText(paths); } catch { }
    }

    private void CopySelectedNames()
    {
        var names = string.Join(Environment.NewLine,
            GetSelectedResults().Select(r => r.FileName));
        if (!string.IsNullOrEmpty(names))
            try { Clipboard.SetText(names); } catch { }
    }

    private void OnDeleteFromIndex(object? sender, EventArgs e)
    {
        var selected = GetSelectedResults().ToList();
        if (selected.Count == 0) return;

        var msg = selected.Count == 1
            ? $"'{selected[0].FileName}'\n인덱스에서 제거하시겠습니까?"
            : $"{selected.Count}개 항목을 인덱스에서 제거하시겠습니까?";

        if (MessageBox.Show(msg, "SFM", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;

        foreach (var r in selected)
            try { _indexer.RemoveFile(r.FilePath); } catch { }

        _ = RunSearchAsync();
    }

    // ─────────────────────────────────────────────────────────
    // 도구 메뉴 핸들러
    // ─────────────────────────────────────────────────────────
    private void OnSettingsClick(object? sender, EventArgs e)
    {
        using var dlg = new IndexSettingsForm(_indexer, _db, _settings);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        // 설정 반영
        chkContent.Checked = _settings.EnableContentSearch;
        _watcher.StopWatching();
        _watcher.StartWatching(_settings.IndexRootPaths);
        _ = RunSearchAsync();
    }

    private void OnReindexClick(object? sender, EventArgs e)
    {
        if (MessageBox.Show("전체 재인덱싱을 시작하시겠습니까?\n시간이 걸릴 수 있습니다.",
                "SFM", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        sslIndexStatus.Text = "재인덱싱 준비 중…";
        _ = _indexer.BuildIndexAsync(_settings.IndexRootPaths);
    }

    private void OnBuildContentClick(object? sender, EventArgs e)
    {
        sslIndexStatus.Text = "내용 인덱싱 시작…";
        _ = _indexer.BuildMissingContentAsync();
    }

    private void OnVacuumClick(object? sender, EventArgs e)
    {
        sslIndexStatus.Text = "DB 최적화 중…";
        Task.Run(() =>
        {
            try
            {
                _db.Vacuum();
                Invoke(() =>
                {
                    sslIndexStatus.Text = "DB 최적화 완료";
                    RefreshDbStats();
                });
            }
            catch (Exception ex)
            {
                Invoke(() => sslIndexStatus.Text = $"최적화 실패: {ex.Message}");
            }
        });
    }

    // ─────────────────────────────────────────────────────────
    // DB 통계 갱신
    // ─────────────────────────────────────────────────────────
    private void RefreshDbStats()
    {
        try
        {
            var stats = _db.GetIndexStats();
            lblDbInfo.Text     = $"파일 {stats.TotalFiles:N0}개  ·  DB {stats.DatabaseSizeMb:F1} MB";
            sslDbStats.Text    = $"인덱스: {stats.TotalFiles:N0}개 파일 ({stats.DatabaseSizeMb:F1} MB)";
        }
        catch { /* ignore */ }
    }

    // ─────────────────────────────────────────────────────────
    // 열 너비 자동 조정
    // ─────────────────────────────────────────────────────────
    private void AutoResizeColumns()
    {
        if (_results.Count == 0) return;
        listResults.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
        // 파일명 최소 너비 보장
        if (colFileName.Width < 160) colFileName.Width = 160;
    }

    // ─────────────────────────────────────────────────────────
    // 포맷 헬퍼
    // ─────────────────────────────────────────────────────────
    private static string FormatSize(long bytes) => bytes switch
    {
        < 1_024             => $"{bytes} B",
        < 1_048_576         => $"{bytes / 1_024.0:F1} KB",
        < 1_073_741_824     => $"{bytes / 1_048_576.0:F1} MB",
        _                   => $"{bytes / 1_073_741_824.0:F1} GB",
    };

    private static string FormatMatchType(SearchResult r) =>
        (r.MatchedInName, r.MatchedInContent) switch
        {
            (true,  true)  => "이름+내용",
            (true,  false) => "파일명",
            (false, true)  => "내용",
            _              => "-",
        };

    // ─────────────────────────────────────────────────────────
    // 폼 종료
    // ─────────────────────────────────────────────────────────
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Dispose();
        _watcher.Dispose();
        _db.Dispose();

        _settings.WindowMaximized = (WindowState == FormWindowState.Maximized);
        if (WindowState == FormWindowState.Normal)
        {
            // 최대화 상태에서는 RestoreBounds로 원래 크기 저장
            _settings.WindowX      = Location.X;
            _settings.WindowY      = Location.Y;
            _settings.WindowWidth  = Size.Width;
            _settings.WindowHeight = Size.Height;
        }
        else
        {
            var rb = RestoreBounds;
            _settings.WindowX      = rb.X;
            _settings.WindowY      = rb.Y;
            _settings.WindowWidth  = rb.Width;
            _settings.WindowHeight = rb.Height;
        }
        _settings.Save();

        base.OnFormClosed(e);
    }
}
