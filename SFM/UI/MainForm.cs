using System.Diagnostics;
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

    /// <summary>현재 표시 중인 검색 결과 (VirtualMode 데이터 소스).</summary>
    private List<SearchResult> _results = [];

    /// <summary>현재 페이지 번호 (0-based). 새 검색어 입력 시 0으로 리셋.</summary>
    private int _currentPage = 0;

    /// <summary>마지막 검색어 — 페이지 유지 vs 리셋 판단에 사용.</summary>
    private string _lastKeyword = string.Empty;

    /// <summary>이전 / 다음 페이지 버튼 (StatusStrip에 동적 추가).</summary>
    private ToolStripButton _sslPrevPage = null!;
    private ToolStripButton _sslNextPage = null!;

    /// <summary>검색창 디바운스 타이머 (UI 스레드에서 Tick).</summary>
    private readonly System.Windows.Forms.Timer _debounceTimer;

    /// <summary>최근 검색어 드롭다운 ListBox.</summary>
    private readonly ListBox _recentBox;

    // ─────────────────────────────────────────────────────────
    // 파일유형 필터 — cmbFilter 인덱스 → 확장자 배열
    // ─────────────────────────────────────────────────────────
    private static readonly string[][] FilterExtensions =
    [
        [],                                                                   // 0: 전체
        [".docx",".doc",".xlsx",".xls",".pptx",".ppt",".txt",".md",".rtf"], // 1: 문서
        [".pdf"],                                                             // 2: PDF
        [".jpg",".jpeg",".png",".gif",".bmp",".svg",".webp",".ico"],         // 3: 이미지
        [".cs",".py",".js",".ts",".java",".cpp",".c",".h",                   // 4: 소스코드
         ".go",".rs",".rb",".php",".swift",".kt",
         ".html",".htm",".css",".scss",".sql",".sh",".bat",".ps1"],
    ];

    // ─────────────────────────────────────────────────────────
    // 파일 아이콘 — 확장자 → ImageList 인덱스
    // ─────────────────────────────────────────────────────────
    private static readonly Dictionary<string, int> ExtImageIndex =
        new(StringComparer.OrdinalIgnoreCase)
        {
            {".docx",1},{".doc",1},{".xlsx",1},{".xls",1},{".pptx",1},{".ppt",1},{".txt",1},{".md",1},{".rtf",1},
            {".pdf",2},
            {".jpg",3},{".jpeg",3},{".png",3},{".gif",3},{".bmp",3},{".svg",3},{".webp",3},{".ico",3},
            {".cs",4},{".py",4},{".js",4},{".ts",4},{".java",4},{".cpp",4},{".c",4},{".h",4},
            {".go",4},{".rs",4},{".rb",4},{".php",4},{".swift",4},{".kt",4},
            {".html",4},{".htm",4},{".css",4},{".scss",4},{".sql",4},{".sh",4},{".bat",4},{".ps1",4},
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

        // ── 디바운스 타이머 (300 ms, UI 스레드에서 실행) ────
        _debounceTimer          = new System.Windows.Forms.Timer { Interval = 300 };
        _debounceTimer.Tick    += OnDebounceTimerTick;

        // ── 최근 검색어 드롭다운 ListBox (폼에 직접 소속) ──
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
        WireEvents();
        InitPagination();
        ApplyTheme();
        RestoreWindowBounds();

        // 앱 시작 시 백그라운드 파일 감시 개시
        _watcher.StartWatching(_settings.IndexRootPaths);
    }

    // ─────────────────────────────────────────────────────────
    // 창 위치·크기 복원
    // ─────────────────────────────────────────────────────────
    private void RestoreWindowBounds()
    {
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
        _sslPrevPage = new ToolStripButton("◀ 이전")
        {
            Enabled   = false,
            Alignment = ToolStripItemAlignment.Right,
            Padding   = new Padding(6, 0, 6, 0),
        };
        _sslNextPage = new ToolStripButton("다음 ▶")
        {
            Enabled   = false,
            Alignment = ToolStripItemAlignment.Right,
            Padding   = new Padding(6, 0, 6, 0),
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

        // ── 검색 버튼 ────────────────────────────────────────
        btnSearch.Click += (_, _) => { _debounceTimer.Stop(); _ = RunSearchAsync(); };

        // ── 파일유형 필터 ────────────────────────────────────
        cmbFilter.SelectedIndexChanged += (_, _) => _ = RunSearchAsync();

        // ── ListView (VirtualMode) ───────────────────────────
        listResults.RetrieveVirtualItem += OnRetrieveVirtualItem;
        listResults.DoubleClick         += (_, _) => OpenSelectedFile();
        listResults.KeyDown             += OnListKeyDown;

        // ── 컨텍스트 메뉴 ────────────────────────────────────
        cmsResult.Opening     += (_, e) => e.Cancel = listResults.SelectedIndices.Count == 0;
        tsmiOpenFile.Click    += (_, _) => OpenSelectedFile();
        tsmiOpenFolder.Click  += (_, _) => OpenSelectedFolder();
        tsmiCopyPath.Click    += (_, _) => CopySelectedPath();

        // ── 설정/인덱싱 버튼 ─────────────────────────────────
        btnSettings.Click += OnSettingsClick;

        // ── Indexer 이벤트 (백그라운드 → UI 스레드 Invoke) ──
        _indexer.IndexProgressChanged += p => Invoke(() =>
            sslIndexStatus.Text =
                $"[{p.Phase}] {p.Processed:N0} / {p.Total:N0}  {Path.GetFileName(p.FilePath)}");

        _indexer.IndexCompleted += r => Invoke(() =>
        {
            sslIndexStatus.Text = $"완료 {r.TotalIndexed:N0}개  " +
                                  $"+{r.Added} ~{r.Updated} -{r.Deleted}  " +
                                  $"{r.Elapsed.TotalSeconds:F1}s";
            sslLastIndexed.Text = $"마지막 인덱싱: {DateTime.Now:HH:mm}";

            // 인덱싱 완료 직후 검색어가 있으면 자동 재검색
            if (!string.IsNullOrWhiteSpace(txtSearch.Text))
                _ = RunSearchAsync();
        });

        _indexer.IndexErrorOccurred += err =>
            Debug.WriteLine($"[IndexError] {err.FilePath}: {err.Message}");
    }

    // ─────────────────────────────────────────────────────────
    // 테마 (다크 / 라이트)
    // ─────────────────────────────────────────────────────────
    private void ApplyTheme()
    {
        if (_settings.IsDarkMode) ApplyDarkTheme();
        else                      ApplyLightTheme();
    }

    private void ApplyDarkTheme()
    {
        var bg   = Color.FromArgb(30,  30,  30);
        var surf = Color.FromArgb(45,  45,  48);
        var text = Color.FromArgb(220, 220, 220);

        BackColor              = bg;
        ForeColor              = text;
        pnlTop.BackColor       = Color.FromArgb(37, 37, 38);

        txtSearch.BackColor    = surf;
        txtSearch.ForeColor    = text;
        txtSearch.BorderStyle  = BorderStyle.FixedSingle;

        btnSearch.BackColor    = Color.FromArgb(0, 122, 204);
        btnSearch.ForeColor    = Color.White;
        btnSettings.BackColor  = surf;
        btnSettings.ForeColor  = text;
        cmbFilter.BackColor    = surf;
        cmbFilter.ForeColor    = text;

        listResults.BackColor  = Color.FromArgb(28, 28, 28);
        listResults.ForeColor  = text;

        statusStrip.BackColor  = surf;
        statusStrip.ForeColor  = text;
        foreach (ToolStripItem item in statusStrip.Items)
            item.ForeColor = text;

        _recentBox.BackColor   = surf;
        _recentBox.ForeColor   = text;
    }

    private void ApplyLightTheme()
    {
        BackColor              = SystemColors.Window;
        ForeColor              = SystemColors.WindowText;
        pnlTop.BackColor       = SystemColors.Control;

        txtSearch.BackColor    = SystemColors.Window;
        txtSearch.ForeColor    = SystemColors.WindowText;
        txtSearch.BorderStyle  = BorderStyle.Fixed3D;

        btnSearch.BackColor    = Color.FromArgb(0, 120, 212);
        btnSearch.ForeColor    = Color.White;
        btnSettings.BackColor  = SystemColors.Control;
        btnSettings.ForeColor  = SystemColors.WindowText;
        cmbFilter.BackColor    = SystemColors.Window;
        cmbFilter.ForeColor    = SystemColors.WindowText;

        listResults.BackColor  = SystemColors.Window;
        listResults.ForeColor  = SystemColors.WindowText;

        statusStrip.BackColor  = SystemColors.Control;
        statusStrip.ForeColor  = SystemColors.WindowText;

        _recentBox.BackColor   = SystemColors.Window;
        _recentBox.ForeColor   = SystemColors.WindowText;
    }

    // ─────────────────────────────────────────────────────────
    // 파일 아이콘 생성
    // ─────────────────────────────────────────────────────────
    private void InitImageList()
    {
        // 팔레트: 0=기본(회색) 1=문서(파랑) 2=PDF(빨강) 3=이미지(보라) 4=코드(초록)
        Color[] palette =
        [
            Color.FromArgb(140, 140, 140),
            Color.FromArgb( 70, 130, 200),
            Color.FromArgb(200,  70,  70),
            Color.FromArgb(150,  80, 200),
            Color.FromArgb( 60, 160, 100),
        ];
        foreach (var clr in palette)
        {
            using var bmp = MakeFileIcon(clr);
            imgListSmall.Images.Add(bmp);
        }
    }

    private static Bitmap MakeFileIcon(Color color)
    {
        var bmp = new Bitmap(16, 16);
        using var g    = Graphics.FromImage(bmp);
        using var fill = new SolidBrush(color);
        using var pen  = new Pen(Color.FromArgb(160, Color.DarkGray));
        using var line = new Pen(Color.FromArgb(150, Color.White));
        g.Clear(Color.Transparent);
        g.FillRectangle(fill, 2, 1, 10, 13);
        g.DrawRectangle(pen,  2, 1, 10, 13);
        // 문서 줄 (16×16 기준)
        g.DrawLine(line, 4, 5, 10, 5);
        g.DrawLine(line, 4, 7, 10, 7);
        g.DrawLine(line, 4, 9, 10, 9);
        return bmp;
    }

    private static int GetImageIndex(string? ext) =>
        !string.IsNullOrEmpty(ext) && ExtImageIndex.TryGetValue(ext, out var idx) ? idx : 0;

    // ─────────────────────────────────────────────────────────
    // VirtualMode — 항목 요청 시 즉시 생성
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
        lvi.SubItems.Add(r.ModifiedAt.ToString("yyyy-MM-dd HH:mm"));
        lvi.SubItems.Add(FormatMatchType(r));
        lvi.ToolTipText = r.FilePath;
        return lvi;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1_024                   => $"{bytes} B",
        < 1_048_576               => $"{bytes / 1_024.0:F1} KB",
        < 1_073_741_824           => $"{bytes / 1_048_576.0:F1} MB",
        _                         => $"{bytes / 1_073_741_824.0:F1} GB",
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
            _results = [];
            _currentPage  = 0;
            _lastKeyword  = string.Empty;
            listResults.VirtualListSize = 0;
            sslResultCount.Text         = "준비";
            _sslPrevPage.Enabled        = false;
            _sslNextPage.Enabled        = false;
            return;
        }

        // 키워드가 바뀌면 페이지를 첫 번째로 리셋
        if (keyword != _lastKeyword)
        {
            _currentPage = 0;
            _lastKeyword = keyword;
        }

        var filterIdx = Math.Clamp(cmbFilter.SelectedIndex, 0, FilterExtensions.Length - 1);
        var query = new SearchQuery
        {
            Keyword         = keyword,
            MaxResults      = _settings.MaxSearchResults,
            SearchInContent = _settings.EnableContentSearch,
            Extensions      = FilterExtensions[filterIdx],
            Page            = _currentPage,
        };

        sslResultCount.Text  = "검색 중…";
        _sslPrevPage.Enabled = false;
        _sslNextPage.Enabled = false;

        var results = await _searcher.SearchAsync(query);

        // await 완료 전 입력이 바뀌었으면 무시 (stale guard)
        if (txtSearch.Text.Trim() != keyword) return;

        _results                    = results;
        listResults.VirtualListSize = _results.Count;
        listResults.Invalidate();

        // 페이지 버튼 상태 갱신
        _sslPrevPage.Enabled = _currentPage > 0;
        _sslNextPage.Enabled = _results.Count >= _settings.MaxSearchResults; // 더 있을 수 있음

        // 결과 수 + 페이지 정보
        string pageInfo = _currentPage > 0 ? $"  p.{_currentPage + 1}" : string.Empty;
        sslResultCount.Text = _results.Count switch
        {
            0 => _currentPage > 0 ? "마지막 페이지" : "결과 없음",
            _ => $"{_results.Count:N0}개 결과{pageInfo}",
        };
    }

    // ─────────────────────────────────────────────────────────
    // 최근 검색어 드롭다운
    // ─────────────────────────────────────────────────────────
    private void OnSearchBoxEnter(object? sender, EventArgs e) => ShowRecentDropdown();

    private void OnSearchBoxLeave(object? sender, EventArgs e)
    {
        // MouseClick이 먼저 처리되도록 약간 지연
        BeginInvoke(() => _recentBox.Visible = false);
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_recentBox.Visible) return;
        switch (e.KeyCode)
        {
            case Keys.Down:
                _recentBox.Focus();
                if (_recentBox.Items.Count > 0) _recentBox.SelectedIndex = 0;
                e.Handled = true;
                break;
            case Keys.Escape:
                _recentBox.Visible = false;
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
            txtSearch.Text = sel;
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

        // pnlTop 바로 아래, txtSearch 왼쪽 경계에 정렬
        var screenPt = txtSearch.Parent!.PointToScreen(txtSearch.Location);
        var formPt   = PointToClient(screenPt);
        _recentBox.Location = new Point(formPt.X, pnlTop.Bottom);
        _recentBox.Width    = txtSearch.Width;
        _recentBox.Height   = Math.Min(recents.Count * 20 + 4, 164);

        _recentBox.BringToFront();
        _recentBox.Visible = true;
    }

    // ─────────────────────────────────────────────────────────
    // 파일 동작
    // ─────────────────────────────────────────────────────────
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Return)
        {
            OpenSelectedFile();
            e.Handled = true;
        }
    }

    private SearchResult? GetSelectedResult()
    {
        if (listResults.SelectedIndices.Count == 0) return null;
        var idx = listResults.SelectedIndices[0];
        return idx < _results.Count ? _results[idx] : null;
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

    private void CopySelectedPath()
    {
        var r = GetSelectedResult();
        if (r is null) return;
        try { Clipboard.SetText(r.FilePath); }
        catch { /* ignore */ }
    }

    // ─────────────────────────────────────────────────────────
    // 설정 / 인덱싱 버튼
    // ─────────────────────────────────────────────────────────
    private void OnSettingsClick(object? sender, EventArgs e)
    {
        using var dlg = new IndexSettingsForm(_indexer, _db, _settings);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        // 경로 변경 반영: 기존 감시 중단 → 새 경로로 재시작
        _watcher.StopWatching();
        _watcher.StartWatching(_settings.IndexRootPaths);

        // 저장 직후 결과 창 재검색 (MaxResults 등이 바뀌었을 수 있음)
        _ = RunSearchAsync();
    }

    // ─────────────────────────────────────────────────────────
    // 폼 종료
    // ─────────────────────────────────────────────────────────
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Dispose();
        _watcher.Dispose();
        _db.Dispose();

        _settings.WindowX      = Location.X;
        _settings.WindowY      = Location.Y;
        _settings.WindowWidth  = Size.Width;
        _settings.WindowHeight = Size.Height;
        _settings.Save();

        base.OnFormClosed(e);
    }
}
