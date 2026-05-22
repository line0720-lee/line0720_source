using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
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

    // 미리보기 파일 파싱 / 비동기 취소
    private readonly FileParser              _parser     = new();
    private          CancellationTokenSource? _snippetCts;

    // ─────────────────────────────────────────────────────────
    // UI 상태
    // ─────────────────────────────────────────────────────────
    private List<SearchResult> _results     = [];
    private int                _currentPage = 0;
    private string             _lastKeyword = string.Empty;

    private ToolStripButton _sslPrevPage = null!;
    private ToolStripButton _sslNextPage = null!;

    private readonly System.Windows.Forms.Timer _debounceTimer;
    private readonly ListBox                    _recentBox;

    // 컬럼 정렬
    private int  _sortColumn    = -1;
    private bool _sortAscending = true;
    private readonly string[] _colHeaders = ["이름", "경로", "파일 크기", "수정 날짜", "유형"];

    // ─────────────────────────────────────────────────────────
    // 파일유형 필터
    // ─────────────────────────────────────────────────────────
    private static readonly string[] ExtDoc   = [".docx",".doc",".xlsx",".xls",".pptx",".ppt",".txt",".md",".rtf"];
    private static readonly string[] ExtPdf   = [".pdf"];
    private static readonly string[] ExtImage = [".jpg",".jpeg",".png",".gif",".bmp",".svg",".webp",".ico"];
    private static readonly string[] ExtCode  = [".cs",".py",".js",".ts",".java",".cpp",".c",".h",
                                                  ".go",".rs",".rb",".php",".swift",".kt",
                                                  ".html",".htm",".css",".scss",".sql",".sh",".bat",".ps1"];

    // 확장자 → imgListSmall 인덱스 (Windows 셸 아이콘 캐시)
    private readonly Dictionary<string, int> _shellIconCache =
        new(StringComparer.OrdinalIgnoreCase);

    // ── 자동 인덱싱 ──────────────────────────────────────────────
    private bool _isIndexing = false;
    private int  _autoIndexCycle = 0;          // 5분 주기 카운터 (6회 = 30분마다 Vacuum)
    private System.Windows.Forms.Timer? _autoIndexTimer;

    private string[] GetSelectedExtensions()
    {
        var list = new List<string>();
        if (chkTypeDoc.Checked)   list.AddRange(ExtDoc);
        if (chkTypePdf.Checked)   list.AddRange(ExtPdf);
        if (chkTypeImage.Checked) list.AddRange(ExtImage);
        if (chkTypeCode.Checked)  list.AddRange(ExtCode);
        return list.ToArray();
    }

    // ─────────────────────────────────────────────────────────
    // 생성자
    // ─────────────────────────────────────────────────────────
    public MainForm()
    {
        InitializeComponent();
        Icon = CreateAppIcon();

        _settings = AppSettings.Load();
        _db       = new DatabaseManager(_settings.DatabasePath);
        _indexer  = new Indexer(_db, new FileParser(), _settings);
        _searcher = new Searcher(_db);
        _watcher  = new FileWatcher(_indexer, _settings);

        _db.Initialize();

        _debounceTimer       = new System.Windows.Forms.Timer { Interval = 300 };
        _debounceTimer.Tick += OnDebounceTimerTick;

        _recentBox = new ListBox
        {
            Visible = false, BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false, Font = new Font("Segoe UI", 10F),
        };
        _recentBox.MouseClick += OnRecentBoxClick;
        _recentBox.KeyDown    += OnRecentBoxKeyDown;
        Controls.Add(_recentBox);

        InitImageList();
        InitPagination();

        // ── 이벤트 연결 전에 초기 UI 상태를 설정 (CheckedChanged 이벤트 오발 방지) ──
        chkContent.Checked         = _settings.EnableContentSearch;
        tsmiSearchContent.Checked  = _settings.EnableContentSearch;

        // 오버레이 카드 Paint — 둥근 테두리
        pnlOverlayCard.Paint += OnOverlayCardPaint;
        pnlOverlay.Resize    += (_, _) => CenterOverlayCard();

        WireEvents();
        ApplyTheme();
        RestoreWindowBounds();
        RefreshDbStats();

        // 시작 시 마지막 인덱싱 날짜 표시
        if (_settings.LastIndexedAt.HasValue)
            sslLastIndexed.Text = $"인덱싱: {_settings.LastIndexedAt.Value:yyyy-MM-dd HH:mm}";

        _watcher.StartWatching(_settings.IndexRootPaths);
        ActiveControl = txtSearch;

        // 5분 자동 증분 인덱싱 타이머 설정
        SetupAutoIndexTimer();

        // SplitterDistance는 폼 크기 확정 후 설정 (EndInit 오류 방지)
        Load += (_, _) =>
        {
            try
            {
                splitPreview.Panel1MinSize    = 300;
                splitPreview.Panel2MinSize    = 180;
                splitPreview.SplitterDistance = Math.Clamp(
                    (int)(splitPreview.Width * 0.72),
                    splitPreview.Panel1MinSize,
                    splitPreview.Width - splitPreview.Panel2MinSize - splitPreview.SplitterWidth);
            }
            catch { /* 방어적 처리 */ }

            // 첫 실행 감지: LastIndexedAt == null → 자동 인덱싱 시작
            if (_settings.LastIndexedAt == null)
            {
                StartFirstRunIndex();
            }
            else if (_settings.EnableContentIndexing)
            {
                // 이미 인덱스는 있지만 내용이 한 건도 없는 경우
                // (이전 버전에서 EnableContentIndexing=false 로 인덱싱한 경우 등)
                // → 백그라운드에서 내용만 추가 인덱싱
                try
                {
                    var stats = _db.GetIndexStats();
                    if (stats.TotalFiles > 0 && stats.ContentIndexedFiles == 0)
                    {
                        sslIndexStatus.Text = "내용 인덱싱 시작 (백그라운드)…";
                        _isIndexing = true;
                        _ = _indexer.BuildMissingContentAsync();
                    }
                }
                catch { /* DB 통계 조회 실패 → 무시 */ }
            }
        };
    }

    // ─────────────────────────────────────────────────────────
    // 창 위치 복원
    // ─────────────────────────────────────────────────────────
    private void RestoreWindowBounds()
    {
        if (_settings.WindowMaximized) { WindowState = FormWindowState.Maximized; return; }
        if (_settings.WindowX >= 0 && _settings.WindowY >= 0)
        { StartPosition = FormStartPosition.Manual; Location = new Point(_settings.WindowX, _settings.WindowY); }
        Size = new Size(_settings.WindowWidth, _settings.WindowHeight);
    }

    // ─────────────────────────────────────────────────────────
    // 이미지 리스트 — Windows 셸 아이콘 사용
    // ─────────────────────────────────────────────────────────
    private void InitImageList()
    {
        // index 0 : 알 수 없는 파일 대표 아이콘 (폴백)
        GetShellIconIndex(null);
    }

    /// <summary>
    /// 확장자에 해당하는 Windows 셸 아이콘을 imgListSmall 에 추가하고
    /// 인덱스를 반환한다. 같은 확장자는 캐시에서 즉시 반환.
    /// </summary>
    private int GetShellIconIndex(string? ext)
    {
        // null / 빈 확장자는 ".*" 일반 파일 아이콘으로 통일
        var key = string.IsNullOrEmpty(ext) ? ".*" : ext;
        if (_shellIconCache.TryGetValue(key, out var cached)) return cached;

        int idx = 0; // 폴백
        try
        {
            var sfi      = new NativeSHFILEINFO();
            var dummyPath = "file" + (key == ".*" ? ".dat" : key);
            NativeMethods.SHGetFileInfo(
                dummyPath,
                NativeMethods.FILE_ATTRIBUTE_NORMAL,
                ref sfi,
                Marshal.SizeOf<NativeSHFILEINFO>(),
                NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON
                    | NativeMethods.SHGFI_USEFILEATTRIBUTES);

            if (sfi.hIcon != IntPtr.Zero)
            {
                try
                {
                    // Icon.FromHandle 은 HICON 소유권을 갖지 않으므로 직접 해제
                    // ※ bmp 에 'using' 금지 — ImageList 가 핸들 생성 전까지 참조를 유지해야 함
                    //   (disposed 비트맵에 접근 시 "Parameter is not valid" 발생)
                    using var tmpIcon = Icon.FromHandle(sfi.hIcon);
                    var bmp = tmpIcon.ToBitmap();   // 해제 안 함: ImageList 내부 _originals 가 소유
                    imgListSmall.Images.Add(bmp);
                    idx = imgListSmall.Images.Count - 1;
                }
                finally { NativeMethods.DestroyIcon(sfi.hIcon); }
            }
        }
        catch { /* 아이콘 로드 실패 → idx=0 폴백 유지 */ }

        _shellIconCache[key] = idx;
        return idx;
    }

    // ── 프로그램 아이콘 (돋보기 모양) ───────────────────────────
    private static Icon CreateAppIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using var g   = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // 렌즈 배경
        using var lensBack = new SolidBrush(Color.FromArgb(210, 232, 255));
        g.FillEllipse(lensBack, 2, 2, 19, 19);

        // 렌즈 테두리
        using var lensPen = new Pen(Color.FromArgb(0, 102, 204), 2.5f);
        g.DrawEllipse(lensPen, 2, 2, 19, 19);

        // 손잡이
        using var handlePen = new Pen(Color.FromArgb(0, 102, 204), 3.5f);
        handlePen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
        handlePen.EndCap   = System.Drawing.Drawing2D.LineCap.Round;
        g.DrawLine(handlePen, 18, 18, 28, 28);

        // 렌즈 내부 하이라이트
        using var hl = new Pen(Color.FromArgb(120, Color.White), 1.5f);
        g.DrawArc(hl, 5, 5, 9, 9, 200, 120);

        var hIcon = bmp.GetHicon();
        var icon  = (Icon)Icon.FromHandle(hIcon).Clone();
        NativeMethods.DestroyIcon(hIcon);
        return icon;
    }

    // ─────────────────────────────────────────────────────────
    // 페이지네이션
    // ─────────────────────────────────────────────────────────
    private void InitPagination()
    {
        _sslPrevPage = new ToolStripButton("◀")
        { Enabled = false, Alignment = ToolStripItemAlignment.Right, ToolTipText = "이전 페이지 (Alt+←)", Padding = new Padding(4,0,4,0) };
        _sslNextPage = new ToolStripButton("▶")
        { Enabled = false, Alignment = ToolStripItemAlignment.Right, ToolTipText = "다음 페이지 (Alt+→)", Padding = new Padding(4,0,4,0) };

        _sslPrevPage.Click += (_, _) => { if (_currentPage > 0) { _currentPage--; _ = RunSearchAsync(); } };
        _sslNextPage.Click += (_, _) => { _currentPage++; _ = RunSearchAsync(); };
        statusStrip.Items.AddRange([_sslPrevPage, _sslNextPage]);
    }

    // ─────────────────────────────────────────────────────────
    // 이벤트 연결
    // ─────────────────────────────────────────────────────────
    private void WireEvents()
    {
        // ── 툴바 버튼 ────────────────────────────────────────────
        btnSearch.Click += (_, _) => { _debounceTimer.Stop(); _ = RunSearchAsync(); };
        btnClear.Click  += (_, _) => { txtSearch.Clear(); txtSearch.Focus(); };

        // ── 검색창 ───────────────────────────────────────────────
        txtSearch.TextChanged += (_, _) =>
        {
            _recentBox.Visible = false;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        };
        txtSearch.Enter   += OnSearchBoxEnter;
        txtSearch.Leave   += OnSearchBoxLeave;
        txtSearch.KeyDown += OnSearchBoxKeyDown;

        // ── 옵션 체크박스 ────────────────────────────────────────
        chkContent.CheckedChanged += (_, _) =>
        {
            _settings.EnableContentSearch = chkContent.Checked;
            tsmiSearchContent.Checked     = chkContent.Checked;
            _ = RunSearchAsync();
        };
        EventHandler typeChanged = (_, _) => _ = RunSearchAsync();
        chkTypeDoc.CheckedChanged   += typeChanged;
        chkTypePdf.CheckedChanged   += typeChanged;
        chkTypeImage.CheckedChanged += typeChanged;
        chkTypeCode.CheckedChanged  += typeChanged;

        // ── ListView ─────────────────────────────────────────────
        listResults.RetrieveVirtualItem  += OnRetrieveVirtualItem;
        listResults.DoubleClick          += (_, _) => OpenSelectedFile();
        listResults.KeyDown              += OnListKeyDown;
        listResults.SelectedIndexChanged += OnListSelectionChanged;
        listResults.ColumnClick          += OnColumnClick;

        // ── ContextMenu ───────────────────────────────────────────
        cmsResult.Opening += (_, e) =>
        {
            bool hasSel = listResults.SelectedIndices.Count > 0, hasRes = _results.Count > 0;
            tsmiOpenFile.Enabled = tsmiOpenFolder.Enabled =
                tsmiDeleteFile.Enabled = tsmiProperties.Enabled =
                tsmiCopyPath.Enabled = tsmiCopyDir.Enabled =
                tsmiCopyName.Enabled = tsmiDeleteFromIndex.Enabled = hasSel;
            tsmiSelectAll.Enabled = hasRes;
            if (!hasRes) e.Cancel = true;
        };
        tsmiOpenFile.Click        += (_, _) => OpenSelectedFile();
        tsmiOpenFolder.Click      += (_, _) => OpenSelectedFolder();
        tsmiDeleteFile.Click      += (_, _) => DeleteSelectedFiles();
        tsmiProperties.Click      += (_, _) => ShowProperties();
        tsmiSelectAll.Click       += (_, _) => SelectAll();
        tsmiCopyPath.Click        += (_, _) => CopySelectedPaths();
        tsmiCopyDir.Click         += (_, _) => CopySelectedDirs();
        tsmiCopyName.Click        += (_, _) => CopySelectedNames();
        tsmiDeleteFromIndex.Click += OnDeleteFromIndex;

        // ── 스니펫 닫기 ──────────────────────────────────────────
        btnSnippetClose.Click += (_, _) =>
        {
            splitPreview.Panel2Collapsed = true;
            tsmiViewSnippet.Checked = false;
        };

        // ── 메뉴 ─────────────────────────────────────────────────
        tsmiFileNewWindow.Click += (_, _) => new MainForm().Show();
        tsmiFileExit.Click      += (_, _) => Close();

        tsmiViewDark.Click += (_, _) => { _settings.IsDarkMode = tsmiViewDark.Checked; ApplyTheme(); };
        tsmiViewSnippet.Click += (_, _) =>
        {
            splitPreview.Panel2Collapsed = !tsmiViewSnippet.Checked;
        };
        tsmiViewAutoCol.Click += (_, _) =>
        {
            _settings.AutoResizeColumns = tsmiViewAutoCol.Checked;
            _settings.Save();
            if (tsmiViewAutoCol.Checked) AutoResizeColumns();
        };

        tsmiSearchContent.Click += (_, _) => chkContent.Checked = tsmiSearchContent.Checked;
        tsmiSearchClear.Click   += (_, _) => { txtSearch.Clear(); txtSearch.Focus(); };

        tsmiToolsReindex.Click += OnReindexClick;
        tsmiToolsVacuum.Click  += OnVacuumClick;

        tsmiHelpAbout.Click += (_, _) => new AboutForm().ShowDialog(this);

        // ── Indexer 이벤트 ───────────────────────────────────────
        _indexer.IndexProgressChanged += p => Invoke(() =>
        {
            sslIndexStatus.Text = $"[{p.Phase}] {p.Processed:N0}/{p.Total:N0}  {Path.GetFileName(p.FilePath)}";
            // 첫 실행 오버레이 진행 표시
            if (pnlOverlay.Visible)
            {
                lblOverlayProgress.Text = p.Total > 0
                    ? $"{p.Processed:N0} / {p.Total:N0} 파일 처리 중..."
                    : $"{p.Processed:N0} 파일 처리 중...";
            }
        });

        _indexer.IndexCompleted += r => Invoke(() =>
        {
            _isIndexing = false;
            HideFirstRunOverlay();   // 첫 실행 오버레이가 떠 있으면 닫음

            bool hasRealChanges = r.Added + r.Updated + r.Deleted > 0;

            if (hasRealChanges)
            {
                // 실제 변경이 있을 때만 상태 갱신·저장
                sslIndexStatus.Text = $"완료 {r.TotalIndexed:N0}개  +{r.Added} ~{r.Updated} -{r.Deleted}  {r.Elapsed.TotalSeconds:F1}s";

                var now = DateTime.Now;
                _settings.LastIndexedAt = now;
                _settings.Save();
                sslLastIndexed.Text = $"인덱싱: {now:yyyy-MM-dd HH:mm}";

                RefreshDbStats();

                // 파일 삭제 발생 시 DB 파편화 정리
                if (r.Deleted > 0)
                    Task.Run(() => { try { _db.Vacuum(); } catch { } });

                if (!string.IsNullOrWhiteSpace(txtSearch.Text)) _ = RunSearchAsync();
            }
            else
            {
                // 변경 없음 → 상태바만 조용히 비움, LastIndexedAt·설정 파일 갱신 없음
                sslIndexStatus.Text = string.Empty;
            }
        });
        _indexer.IndexErrorOccurred += err => Debug.WriteLine($"[IndexError] {err.FilePath}: {err.Message}");
    }

    // ─────────────────────────────────────────────────────────
    // 테마
    // ─────────────────────────────────────────────────────────
    private void ApplyTheme()
    {
        tsmiViewDark.Checked    = _settings.IsDarkMode;
        tsmiViewAutoCol.Checked = _settings.AutoResizeColumns;
        if (_settings.IsDarkMode) ApplyDarkTheme();
        else                      ApplyLightTheme();
    }

    // ── 커스텀 메뉴 렌더러 ──────────────────────────────────────
    private sealed class BlueMenuRenderer : ToolStripProfessionalRenderer
    {
        public BlueMenuRenderer() : base(new BlueColorTable()) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected) { base.OnRenderMenuItemBackground(e); return; }
            using var br = new SolidBrush(Color.FromArgb(30, 120, 210));
            e.Graphics.FillRectangle(br, e.Item.ContentRectangle);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { /* 테두리 제거 */ }
    }
    private sealed class BlueColorTable : ProfessionalColorTable
    {
        readonly Color _bg = Color.FromArgb(13, 71, 161);
        public override Color MenuStripGradientBegin           => _bg;
        public override Color MenuStripGradientEnd             => _bg;
        public override Color MenuItemSelectedGradientBegin    => Color.FromArgb(30, 120, 210);
        public override Color MenuItemSelectedGradientEnd      => Color.FromArgb(30, 120, 210);
        public override Color MenuItemSelected                 => Color.FromArgb(30, 120, 210);
        public override Color MenuItemBorder                   => Color.FromArgb(60, 150, 240);
        public override Color MenuItemPressedGradientBegin     => Color.FromArgb(10, 60, 150);
        public override Color MenuItemPressedGradientEnd       => Color.FromArgb(10, 60, 150);
        public override Color ToolStripDropDownBackground      => Color.FromArgb(250, 252, 255);
        public override Color MenuBorder                       => Color.FromArgb(190, 215, 245);
        public override Color SeparatorDark                    => Color.FromArgb(190, 215, 245);
        public override Color SeparatorLight                   => Color.White;
        public override Color ImageMarginGradientBegin         => Color.FromArgb(237, 244, 255);
        public override Color ImageMarginGradientMiddle        => Color.FromArgb(237, 244, 255);
        public override Color ImageMarginGradientEnd           => Color.FromArgb(237, 244, 255);
        public override Color CheckBackground                  => Color.FromArgb(200, 225, 255);
        public override Color CheckSelectedBackground          => Color.FromArgb(170, 205, 250);
        public override Color CheckPressedBackground           => Color.FromArgb(150, 190, 245);
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Color.White;
            base.OnRenderItemText(e);
        }
    }
    private sealed class DarkColorTable : ProfessionalColorTable
    {
        readonly Color _bg = Color.FromArgb(30, 30, 40);
        public override Color MenuStripGradientBegin           => _bg;
        public override Color MenuStripGradientEnd             => _bg;
        public override Color MenuItemSelectedGradientBegin    => Color.FromArgb(50, 50, 70);
        public override Color MenuItemSelectedGradientEnd      => Color.FromArgb(50, 50, 70);
        public override Color MenuItemSelected                 => Color.FromArgb(50, 50, 70);
        public override Color MenuItemBorder                   => Color.FromArgb(80, 80, 110);
        public override Color ToolStripDropDownBackground      => Color.FromArgb(40, 40, 55);
        public override Color MenuBorder                       => Color.FromArgb(70, 70, 90);
        public override Color SeparatorDark                    => Color.FromArgb(70, 70, 90);
        public override Color SeparatorLight                   => Color.FromArgb(60, 60, 80);
        public override Color ImageMarginGradientBegin         => Color.FromArgb(35, 35, 50);
        public override Color ImageMarginGradientMiddle        => Color.FromArgb(35, 35, 50);
        public override Color ImageMarginGradientEnd           => Color.FromArgb(35, 35, 50);
    }

    // ─────────────────────────────────────────────────────────────
    private void ApplyDarkTheme()
    {
        var cBg      = Color.FromArgb(18,  18,  28);   // 거의 검정 (약간 남색)
        var cSurface = Color.FromArgb(30,  30,  45);   // 카드 표면
        var cSurf2   = Color.FromArgb(38,  38,  55);   // 약간 밝은 표면
        var cText    = Color.FromArgb(224, 224, 235);
        var cSub     = Color.FromArgb(140, 140, 165);
        var cBorder  = Color.FromArgb(55,  55,  75);
        var cAccent  = Color.FromArgb(66,  133, 244);  // 구글 블루 느낌

        BackColor = cBg; ForeColor = cText;

        menuStrip.Renderer = new DarkMenuRenderer();
        menuStrip.BackColor = Color.FromArgb(30, 30, 40);
        menuStrip.ForeColor = cText;
        foreach (ToolStripMenuItem m in menuStrip.Items.OfType<ToolStripMenuItem>())
        { m.ForeColor = cText; }

        pnlSearch.BackColor = tblToolbar.BackColor = cSurface;
        pnlOptions.BackColor = cSurf2;

        txtSearch.BackColor = cSurf2; txtSearch.ForeColor = cText;
        txtSearch.BorderStyle = BorderStyle.FixedSingle;

        btnClear.BackColor = Color.Transparent;
        btnClear.FlatAppearance.BorderSize = 0;
        btnClear.ForeColor = cSub;
        btnSearch.BackColor = cAccent;
        btnSearch.ForeColor = Color.White;
        btnSearch.FlatAppearance.BorderColor = cAccent;
        btnSearch.FlatAppearance.BorderSize  = 0;

        chkContent.ForeColor = cText; lblFilter.ForeColor = cSub;
        chkTypeDoc.ForeColor = chkTypePdf.ForeColor =
            chkTypeImage.ForeColor = chkTypeCode.ForeColor = cText;
        lblDbInfo.ForeColor = cSub;
        lblAiNotice.ForeColor = Color.FromArgb(80, 100, 140);

        splitPreview.BackColor = cBorder;

        listResults.BackColor = cSurface; listResults.ForeColor = cText;

        pnlSnippet.BackColor    = cSurface;
        pnlSnippetBar.BackColor = cSurf2;
        lblSnippetIcon.ForeColor = cAccent; lblSnippetPath.ForeColor = cSub;
        btnSnippetClose.BackColor = Color.Transparent;
        btnSnippetClose.FlatAppearance.BorderSize = 0;
        btnSnippetClose.ForeColor = cSub;
        rtbSnippet.BackColor = cSurface; rtbSnippet.ForeColor = cText;

        statusStrip.BackColor = Color.FromArgb(24, 24, 36); statusStrip.ForeColor = cSub;
        foreach (ToolStripItem item in statusStrip.Items) item.ForeColor = cSub;
        _sslPrevPage.ForeColor = _sslNextPage.ForeColor = cSub;

        _recentBox.BackColor = cSurf2; _recentBox.ForeColor = cText;

        // 오버레이
        pnlOverlay.BackColor         = Color.FromArgb(18, 22, 38);
        lblOverlayTitle.ForeColor    = Color.FromArgb(30, 35, 50);
        lblOverlayDesc.ForeColor     = Color.FromArgb(100, 116, 139);
        lblOverlayProgress.ForeColor = Color.FromArgb(120, 130, 150);
    }

    private void ApplyLightTheme()
    {
        var cPrimary = Color.FromArgb(13,  71, 161);   // #0D47A1 딥 블루
        var cAccent  = Color.FromArgb(25, 118, 210);   // #1976D2 버튼 강조
        var cBg      = Color.FromArgb(245, 246, 250);  // 배경 (아주 연한 회색)
        var cSurface = Color.White;
        var cSurf2   = Color.FromArgb(240, 244, 252);  // 패널 서브 표면 (연한 블루-회색)
        var cText    = Color.FromArgb(26,  32,  44);   // 진한 텍스트
        var cSub     = Color.FromArgb(100, 116, 139);  // 서브 텍스트
        var cBorder  = Color.FromArgb(203, 213, 225);  // 테두리

        BackColor = cBg; ForeColor = cText;

        menuStrip.Renderer  = new BlueMenuRenderer();
        menuStrip.BackColor = Color.FromArgb(13, 71, 161);
        menuStrip.ForeColor = Color.White;
        foreach (ToolStripMenuItem m in menuStrip.Items.OfType<ToolStripMenuItem>())
            m.ForeColor = Color.White;

        // 검색 패널 — 흰색 카드 느낌
        pnlSearch.BackColor = tblToolbar.BackColor = cSurface;
        pnlOptions.BackColor = cSurf2;

        txtSearch.BackColor = cSurface; txtSearch.ForeColor = cText;
        txtSearch.BorderStyle = BorderStyle.FixedSingle;

        btnClear.BackColor = Color.Transparent;
        btnClear.FlatAppearance.BorderSize = 0;
        btnClear.ForeColor = cSub;
        btnSearch.BackColor = cAccent;
        btnSearch.ForeColor = Color.White;
        btnSearch.FlatAppearance.BorderColor = cAccent;
        btnSearch.FlatAppearance.BorderSize  = 0;

        chkContent.ForeColor = cText; lblFilter.ForeColor = cSub;
        chkTypeDoc.ForeColor = chkTypePdf.ForeColor =
            chkTypeImage.ForeColor = chkTypeCode.ForeColor = cText;
        lblDbInfo.ForeColor = cSub;
        lblAiNotice.ForeColor = Color.FromArgb(100, 130, 180);

        splitPreview.BackColor = cBorder;   // 스플리터 선 색상

        listResults.BackColor = cSurface; listResults.ForeColor = cText;

        // 미리보기 — 연한 블루 헤더 바
        pnlSnippet.BackColor    = cSurface;
        pnlSnippetBar.BackColor = cSurf2;
        lblSnippetIcon.ForeColor = cAccent; lblSnippetPath.ForeColor = cSub;
        btnSnippetClose.BackColor = Color.Transparent;
        btnSnippetClose.FlatAppearance.BorderSize = 0;
        btnSnippetClose.ForeColor = cSub;
        rtbSnippet.BackColor = cSurface; rtbSnippet.ForeColor = cText;

        // 상태 바 — 흰색 바닥
        statusStrip.BackColor = cSurface; statusStrip.ForeColor = cSub;
        foreach (ToolStripItem item in statusStrip.Items) item.ForeColor = cSub;
        _sslPrevPage.ForeColor = _sslNextPage.ForeColor = cSub;

        _recentBox.BackColor = cSurface; _recentBox.ForeColor = cText;

        // 오버레이
        pnlOverlay.BackColor         = Color.FromArgb(30, 35, 50);
        lblOverlayTitle.ForeColor    = Color.FromArgb(26, 32, 44);
        lblOverlayDesc.ForeColor     = Color.FromArgb(100, 116, 139);
        lblOverlayProgress.ForeColor = Color.FromArgb(120, 130, 150);
    }

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
        var lvi = new ListViewItem(r.FileName, GetShellIconIndex(r.Extension));
        lvi.SubItems.Add(Path.GetDirectoryName(r.FilePath) ?? r.FilePath);
        lvi.SubItems.Add(FormatSize(r.FileSize));
        lvi.SubItems.Add(r.ModifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        lvi.SubItems.Add(FormatFileType(r.Extension));
        lvi.ToolTipText = r.FilePath;
        return lvi;
    }

    // ─────────────────────────────────────────────────────────
    // 컬럼 정렬
    // ─────────────────────────────────────────────────────────
    private void OnColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (_results.Count == 0) return;
        if (_sortColumn == e.Column) _sortAscending = !_sortAscending;
        else { _sortColumn = e.Column; _sortAscending = true; }
        _results = SortResults(_results, _sortColumn, _sortAscending);
        for (int i = 0; i < listResults.Columns.Count; i++)
            listResults.Columns[i].Text = i == _sortColumn
                ? _colHeaders[i] + (_sortAscending ? "  ▲" : "  ▼") : _colHeaders[i];
        listResults.Invalidate();
    }

    private static List<SearchResult> SortResults(List<SearchResult> src, int col, bool asc)
    {
        IOrderedEnumerable<SearchResult> o = col switch
        {
            0 => asc ? src.OrderBy(r => r.FileName, StringComparer.OrdinalIgnoreCase)
                     : src.OrderByDescending(r => r.FileName, StringComparer.OrdinalIgnoreCase),
            1 => asc ? src.OrderBy(r => Path.GetDirectoryName(r.FilePath) ?? "", StringComparer.OrdinalIgnoreCase)
                     : src.OrderByDescending(r => Path.GetDirectoryName(r.FilePath) ?? "", StringComparer.OrdinalIgnoreCase),
            2 => asc ? src.OrderBy(r => r.FileSize) : src.OrderByDescending(r => r.FileSize),
            3 => asc ? src.OrderBy(r => r.ModifiedAt) : src.OrderByDescending(r => r.ModifiedAt),
            _ => asc ? src.OrderBy(r => r.Extension ?? "", StringComparer.OrdinalIgnoreCase)
                     : src.OrderByDescending(r => r.Extension ?? "", StringComparer.OrdinalIgnoreCase),
        };
        return o.ToList();
    }

    // ─────────────────────────────────────────────────────────
    // 스니펫 패널
    // ─────────────────────────────────────────────────────────
    private void OnListSelectionChanged(object? sender, EventArgs e)
    {
        if (splitPreview.Panel2Collapsed) return;
        UpdateSnippetPanel(GetSelectedResult());
    }

    private void UpdateSnippetPanel(SearchResult? r)
    {
        // 이전 비동기 로딩 취소
        _snippetCts?.Cancel();
        _snippetCts?.Dispose();
        _snippetCts = null;

        if (r is null)
        {
            lblSnippetPath.Text = "항목을 선택하면 미리보기가 표시됩니다";
            rtbSnippet.Clear();
            return;
        }

        lblSnippetPath.Text = r.FilePath;

        // 테마별 색상
        var clrText = _settings.IsDarkMode ? Color.FromArgb(224, 224, 235) : Color.FromArgb(26, 32, 44);
        var clrSub  = _settings.IsDarkMode ? Color.FromArgb(140, 140, 165) : SystemColors.GrayText;

        // 로딩 표시
        rtbSnippet.Clear();
        rtbSnippet.ForeColor = clrSub;
        rtbSnippet.Text = "내용 불러오는 중…";

        var cts      = new CancellationTokenSource();
        _snippetCts  = cts;
        var keyword         = txtSearch.Text.Trim();
        var highlightWord   = Searcher.StripWildcards(keyword);   // 와일드카드 제거 후 하이라이트
        var filePath        = r.FilePath;

        _ = Task.Run(async () =>
        {
            try
            {
                // 파일 전체 내용 파싱 (최대 1 MB 기준, 초과 시 500 KB 제한)
                var content = await _parser.ParseFileContent(filePath, maxSizeMB: 1);

                if (cts.IsCancellationRequested) return;

                // 미리보기 최대 100,000자 표시
                const int PreviewLimit = 100_000;
                bool truncated = content.Length > PreviewLimit;
                if (truncated) content = content[..PreviewLimit];

                Invoke(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    rtbSnippet.Clear();

                    if (string.IsNullOrEmpty(content))
                    {
                        rtbSnippet.ForeColor = clrSub;
                        rtbSnippet.Text = "(미리보기를 지원하지 않는 파일 형식입니다)";
                    }
                    else
                    {
                        rtbSnippet.ForeColor = clrText;
                        rtbSnippet.Text = content + (truncated ? "\n\n[…이하 내용 생략…]" : string.Empty);
                        if (!string.IsNullOrEmpty(highlightWord))
                            HighlightKeywordInSnippet(highlightWord);
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch
            {
                if (!cts.IsCancellationRequested)
                    Invoke(() => { rtbSnippet.ForeColor = clrSub; rtbSnippet.Text = "(파일을 읽을 수 없습니다)"; });
            }
        }, cts.Token);
    }

    private void HighlightKeywordInSnippet(string keyword)
    {
        if (string.IsNullOrEmpty(keyword) || string.IsNullOrEmpty(rtbSnippet.Text)) return;
        var text     = rtbSnippet.Text;
        int start    = 0;
        int firstIdx = -1;

        while (true)
        {
            int idx = text.IndexOf(keyword, start, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) break;
            if (firstIdx < 0) firstIdx = idx;   // 첫 번째 위치 기억
            rtbSnippet.Select(idx, keyword.Length);
            rtbSnippet.SelectionBackColor = Color.FromArgb(255, 240, 0);
            rtbSnippet.SelectionColor     = Color.Black;
            start = idx + keyword.Length;
        }

        // 첫 번째 매칭 위치로 스크롤
        if (firstIdx >= 0)
        {
            rtbSnippet.Select(firstIdx, keyword.Length);
            rtbSnippet.ScrollToCaret();
        }
        rtbSnippet.SelectionLength = 0;
    }

    // ─────────────────────────────────────────────────────────
    // 검색
    // ─────────────────────────────────────────────────────────
    private void OnDebounceTimerTick(object? sender, EventArgs e) { _debounceTimer.Stop(); _ = RunSearchAsync(); }

    private async Task RunSearchAsync()
    {
        var keyword = txtSearch.Text.Trim();
        if (keyword.Length < 2)
        {
            _results = []; _currentPage = 0; _lastKeyword = string.Empty;
            listResults.VirtualListSize = 0; sslResultCount.Text = "준비";
            _sslPrevPage.Enabled = _sslNextPage.Enabled = false;
            ResetSortHeaders(); UpdateSnippetPanel(null);
            Text = "SFM — Smart File Manager"; return;
        }
        if (keyword != _lastKeyword) { _currentPage = 0; _lastKeyword = keyword; ResetSortHeaders(); }

        var query = new SearchQuery
        {
            Keyword         = keyword,
            MaxResults      = _settings.MaxSearchResults,
            SearchInContent = chkContent.Checked,
            Extensions      = GetSelectedExtensions(),
            Page            = _currentPage,
        };
        sslResultCount.Text = "검색 중…"; _sslPrevPage.Enabled = _sslNextPage.Enabled = false;

        try
        {
            var sw      = Stopwatch.StartNew();
            var results = await _searcher.SearchAsync(query);
            sw.Stop();
            if (txtSearch.Text.Trim() != keyword) return;

            _results = results; _sortColumn = -1;
            listResults.VirtualListSize = _results.Count;
            listResults.Invalidate();

            _sslPrevPage.Enabled = _currentPage > 0;
            _sslNextPage.Enabled = _results.Count >= _settings.MaxSearchResults;

            var elapsed = sw.Elapsed.TotalSeconds;
            sslResultCount.Text = _results.Count == 0
                ? $"검색 완료.  결과 없음.  경과 시간: {elapsed:F3} 초"
                : $"검색 완료.  {_results.Count:N0} 파일/폴더 발견.  경과 시간: {elapsed:F3} 초";

            if (_results.Count > 0)
            {
                if (tsmiViewAutoCol.Checked) AutoResizeColumns();
                if (!splitPreview.Panel2Collapsed) UpdateSnippetPanel(_results[0]);
            }
        }
        catch (Microsoft.Data.Sqlite.SqliteException sqEx) when (sqEx.SqliteErrorCode == 11)
        {
            // 오류코드 11 = SQLITE_CORRUPT — 손상 DB 자동 복구
            sslResultCount.Text = "DB 손상 감지 — 자동 복구 중…";
            try
            {
                _db.RecoverAndReinit();
                sslResultCount.Text = "DB 복구 완료. [도구 → 전체 재인덱싱(F5)]을 실행하세요.";
            }
            catch (Exception rex)
            {
                sslResultCount.Text = $"DB 복구 실패: {rex.Message}";
                Debug.WriteLine($"[DB Recovery] {rex}");
            }
        }
        catch (Exception ex)
        {
            sslResultCount.Text = $"검색 오류: {ex.Message}";
            Debug.WriteLine($"[RunSearchAsync] {ex}");
        }
    }

    private void ResetSortHeaders()
    {
        _sortColumn = -1; _sortAscending = true;
        for (int i = 0; i < listResults.Columns.Count; i++) listResults.Columns[i].Text = _colHeaders[i];
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
                e.Handled = true; break;
            // Keys.Escape 는 ProcessCmdKey 에서 폼 레벨로 처리
            case Keys.Enter:
                _debounceTimer.Stop(); _ = RunSearchAsync(); e.Handled = true; break;
        }
    }

    private void OnRecentBoxClick(object? sender, MouseEventArgs e)
    {
        if (_recentBox.SelectedItem is string sel) { txtSearch.Text = sel; txtSearch.SelectionStart = sel.Length; }
        _recentBox.Visible = false; txtSearch.Focus();
    }

    private void OnRecentBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && _recentBox.SelectedItem is string sel)
        { txtSearch.Text = sel; _recentBox.Visible = false; txtSearch.Focus(); _ = RunSearchAsync(); e.Handled = true; }
        else if (e.KeyCode == Keys.Escape) { _recentBox.Visible = false; txtSearch.Focus(); e.Handled = true; }
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
        _recentBox.BringToFront(); _recentBox.Visible = true;
    }

    // ─────────────────────────────────────────────────────────
    // 키보드 단축키
    // ─────────────────────────────────────────────────────────
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
                // 폼 전체 ESC — 어떤 컨트롤에 포커스가 있어도 동작
                if (_recentBox.Visible)
                {
                    _recentBox.Visible = false;
                }
                else if (txtSearch.Text.Length > 0)
                {
                    txtSearch.Clear();                           // 1차 ESC: 검색어 삭제
                }
                else
                {
                    // 2차 ESC: 결과 전체 초기화
                    _debounceTimer.Stop();
                    _results = []; _currentPage = 0; _lastKeyword = string.Empty;
                    listResults.VirtualListSize = 0;
                    sslResultCount.Text = "준비";
                    _sslPrevPage.Enabled = _sslNextPage.Enabled = false;
                    ResetSortHeaders();
                    UpdateSnippetPanel(null);
                    Text = "SFM — Smart File Manager";
                }
                txtSearch.Focus();                               // 항상 검색창으로 포커스
                return true;

            case Keys.Enter when txtSearch.Focused:
                if (_recentBox.Visible) { if (_recentBox.SelectedItem is string sel) txtSearch.Text = sel; _recentBox.Visible = false; }
                _debounceTimer.Stop(); _ = RunSearchAsync(); return true;
            case Keys.Control | Keys.F:
                txtSearch.Focus(); txtSearch.SelectAll(); return true;
            case Keys.Control | Keys.A when listResults.ContainsFocus || listResults.Focused:
                SelectAll(); return true;
            case Keys.Control | Keys.C when listResults.Focused:
                CopySelectedPaths(); return true;
            case Keys.Alt | Keys.Left when _sslPrevPage.Enabled:
                _currentPage--; _ = RunSearchAsync(); return true;
            case Keys.Alt | Keys.Right when _sslNextPage.Enabled:
                _currentPage++; _ = RunSearchAsync(); return true;
            case Keys.F4:
                chkContent.Checked = !chkContent.Checked;
                tsmiSearchContent.Checked = chkContent.Checked; return true;
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
            case Keys.Enter or Keys.Return: OpenSelectedFile(); e.Handled = true; break;
            case Keys.Delete: OnDeleteFromIndex(sender, e); e.Handled = true; break;
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
        var r = GetSelectedResult(); if (r is null) return;
        try { Process.Start(new ProcessStartInfo(r.FilePath) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"파일을 열 수 없습니다.\n{ex.Message}", "SFM", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void OpenSelectedFolder()
    {
        var r = GetSelectedResult(); if (r is null) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{r.FilePath}\"") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"폴더를 열 수 없습니다.\n{ex.Message}", "SFM", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void MoveSelectedFiles()
    {
        var selected = GetSelectedResults().ToList(); if (selected.Count == 0) return;
        using var dlg = new FolderBrowserDialog { Description = $"{selected.Count}개 파일을 이동할 폴더", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var dest = dlg.SelectedPath; var errors = new List<string>();
        foreach (var r in selected)
        {
            try
            {
                var target = Path.Combine(dest, r.FileName);
                if (File.Exists(target))
                {
                    var ans = MessageBox.Show($"'{r.FileName}' 이(가) 이미 존재합니다. 덮어쓸까요?", "이동 확인", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (ans == DialogResult.Cancel) break;
                    if (ans == DialogResult.No) continue;
                }
                File.Move(r.FilePath, target, overwrite: true);
                _indexer.RemoveFile(r.FilePath);
            }
            catch (Exception ex) { errors.Add($"{r.FileName}: {ex.Message}"); }
        }
        if (errors.Count > 0) MessageBox.Show("이동 실패:\n" + string.Join('\n', errors.Take(10)), "이동 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        _ = RunSearchAsync();
    }

    private void DeleteSelectedFiles()
    {
        var selected = GetSelectedResults().ToList(); if (selected.Count == 0) return;
        var msg = selected.Count == 1 ? $"'{selected[0].FileName}'\n파일을 휴지통으로 이동할까요?" : $"{selected.Count}개 파일을 휴지통으로 이동할까요?";
        if (MessageBox.Show(msg, "파일 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var errors = new List<string>();
        foreach (var r in selected)
        {
            try { RecycleFile(r.FilePath); _indexer.RemoveFile(r.FilePath); }
            catch (Exception ex) { errors.Add($"{r.FileName}: {ex.Message}"); }
        }
        if (errors.Count > 0) MessageBox.Show("삭제 실패:\n" + string.Join('\n', errors.Take(10)), "삭제 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        _ = RunSearchAsync();
    }

    private static void RecycleFile(string path)
    {
        var op = new NativeShFileOp { wFunc = 3, pFrom = path + '\0' + '\0', fFlags = (short)(0x0040 | 0x0010 | 0x0400) };
        NativeMethods.SHFileOperation(ref op);
    }

    private void ShowProperties()
    {
        var r = GetSelectedResult(); if (r is null) return;
        try { Process.Start(new ProcessStartInfo(r.FilePath) { Verb = "properties", UseShellExecute = true }); }
        catch
        {
            var fi = new FileInfo(r.FilePath);
            MessageBox.Show($"이름: {fi.Name}\n경로: {fi.DirectoryName}\n크기: {FormatSize(fi.Length)}\n수정: {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}", "파일 속성", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void SelectAll()
    {
        if (_results.Count == 0) return;
        listResults.BeginUpdate();
        for (int i = 0; i < _results.Count; i++) listResults.SelectedIndices.Add(i);
        listResults.EndUpdate();
    }

    private void InvertSelection()
    {
        if (_results.Count == 0) return;
        var sel = new HashSet<int>(listResults.SelectedIndices.Cast<int>());
        listResults.BeginUpdate();
        listResults.SelectedIndices.Clear();
        for (int i = 0; i < _results.Count; i++) if (!sel.Contains(i)) listResults.SelectedIndices.Add(i);
        listResults.EndUpdate();
    }

    private void CopySelectedPaths() { var t = string.Join(Environment.NewLine, GetSelectedResults().Select(r => r.FilePath)); if (!string.IsNullOrEmpty(t)) try { Clipboard.SetText(t); } catch { } }
    private void CopySelectedDirs()  { var t = string.Join(Environment.NewLine, GetSelectedResults().Select(r => Path.GetDirectoryName(r.FilePath) ?? r.FilePath).Distinct()); if (!string.IsNullOrEmpty(t)) try { Clipboard.SetText(t); } catch { } }
    private void CopySelectedNames() { var t = string.Join(Environment.NewLine, GetSelectedResults().Select(r => r.FileName)); if (!string.IsNullOrEmpty(t)) try { Clipboard.SetText(t); } catch { } }


    private void OnDeleteFromIndex(object? sender, EventArgs e)
    {
        var selected = GetSelectedResults().ToList(); if (selected.Count == 0) return;
        var msg = selected.Count == 1 ? $"'{selected[0].FileName}'\n인덱스에서 제거하시겠습니까?" : $"{selected.Count}개 항목을 인덱스에서 제거하시겠습니까?";
        if (MessageBox.Show(msg, "SFM", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        foreach (var r in selected) try { _indexer.RemoveFile(r.FilePath); } catch { }
        _ = RunSearchAsync();
    }

    // ─────────────────────────────────────────────────────────
    // 도구 메뉴
    // ─────────────────────────────────────────────────────────
    private void OnReindexClick(object? sender, EventArgs e)
    {
        if (MessageBox.Show("전체 재인덱싱을 시작하시겠습니까?", "SFM", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        sslIndexStatus.Text = "재인덱싱 준비 중…";
        _ = _indexer.BuildIndexAsync(_settings.IndexRootPaths);
    }
    private void OnVacuumClick(object? sender, EventArgs e)
    {
        sslIndexStatus.Text = "DB 최적화 중…";
        Task.Run(() =>
        {
            try { _db.Vacuum(); Invoke(() => { sslIndexStatus.Text = "DB 최적화 완료"; RefreshDbStats(); }); }
            catch (Exception ex) { Invoke(() => sslIndexStatus.Text = $"최적화 실패: {ex.Message}"); }
        });
    }

    // ─────────────────────────────────────────────────────────
    // DB 통계
    // ─────────────────────────────────────────────────────────
    private void RefreshDbStats()
    {
        try
        {
            var s = _db.GetIndexStats();
            lblDbInfo.Text  = $"인덱스: {s.TotalFiles:N0}개 파일  ·  {s.DatabaseSizeMb:F1} MB";
            sslDbStats.Text = $"인덱스: {s.TotalFiles:N0}개 파일 ({s.DatabaseSizeMb:F1} MB)";
        }
        catch { }
    }

    private void AutoResizeColumns()
    {
        if (_results.Count == 0) return;

        // ① 내용 기준 1차 조정 (VirtualMode에서도 동작)
        listResults.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);

        // ② 각 열마다 헤더 너비도 측정 → 내용·헤더 중 큰 값 적용
        foreach (ColumnHeader col in listResults.Columns)
        {
            int contentWidth = col.Width;
            listResults.AutoResizeColumn(col.Index, ColumnHeaderAutoResizeStyle.HeaderSize);
            col.Width = Math.Max(contentWidth, col.Width);
        }

        // ③ 절대 최솟값 보장
        if (colFileName.Width < 180) colFileName.Width = 180;
        if (colPath.Width     < 200) colPath.Width     = 200;
        if (colSize.Width     < 105) colSize.Width     = 105;
        if (colModified.Width < 130) colModified.Width = 130;
        if (colMatch.Width    <  70) colMatch.Width    =  70;
    }

    // ─────────────────────────────────────────────────────────
    // 첫 실행 오버레이
    // ─────────────────────────────────────────────────────────

    private void StartFirstRunIndex()
    {
        // IndexRootPaths 가 비어 있으면 로컬 고정 드라이브 전체를 기본값으로 설정
        if (_settings.IndexRootPaths.Length == 0)
        {
            _settings.IndexRootPaths = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName)
                .ToArray();
            _settings.Save();
            _watcher.StopWatching();
            _watcher.StartWatching(_settings.IndexRootPaths);
        }

        ShowFirstRunOverlay();
        _isIndexing = true;
        _ = _indexer.BuildIndexAsync(_settings.IndexRootPaths);
    }

    private void ShowFirstRunOverlay()
    {
        pnlOverlay.SetBounds(0, 0, ClientSize.Width, ClientSize.Height);
        CenterOverlayCard();
        pnlOverlay.BringToFront();
        pnlOverlay.Visible = true;
    }

    private void HideFirstRunOverlay()
    {
        if (pnlOverlay.Visible) pnlOverlay.Visible = false;
    }

    private void CenterOverlayCard()
    {
        pnlOverlayCard.Location = new Point(
            (pnlOverlay.Width  - pnlOverlayCard.Width)  / 2,
            (pnlOverlay.Height - pnlOverlayCard.Height) / 2);
    }

    private static void OnOverlayCardPaint(object? sender, PaintEventArgs e)
    {
        var g  = e.Graphics;
        var rc = ((Panel)sender!).ClientRectangle;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // 그림자 (약간 아래-오른쪽 오프셋)
        var shadowRc = rc; shadowRc.Inflate(-2, -2); shadowRc.Offset(3, 4);
        using var shadowBr = new SolidBrush(Color.FromArgb(40, 0, 0, 0));
        using var shadowPath = RoundedRectPath(shadowRc, 14);
        g.FillPath(shadowBr, shadowPath);

        // 카드 배경
        using var cardBr   = new SolidBrush(Color.White);
        using var cardPath = RoundedRectPath(new Rectangle(rc.X, rc.Y, rc.Width - 4, rc.Height - 4), 14);
        g.FillPath(cardBr, cardPath);

        // 카드 테두리
        using var borderPen = new Pen(Color.FromArgb(220, 228, 240), 1.2f);
        g.DrawPath(borderPen, cardPath);
    }

    private static GraphicsPath RoundedRectPath(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        path.AddArc(r.X,              r.Y,               radius * 2, radius * 2, 180, 90);
        path.AddArc(r.Right - radius * 2, r.Y,           radius * 2, radius * 2, 270, 90);
        path.AddArc(r.Right - radius * 2, r.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(r.X,              r.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ─────────────────────────────────────────────────────────
    // 5분 자동 증분 인덱싱
    // ─────────────────────────────────────────────────────────

    private void SetupAutoIndexTimer()
    {
        _autoIndexTimer = new System.Windows.Forms.Timer { Interval = 5 * 60 * 1000 }; // 5분
        _autoIndexTimer.Tick += OnAutoIndexTick;
        _autoIndexTimer.Start();
    }

    private void OnAutoIndexTick(object? sender, EventArgs e)
    {
        // 이미 인덱싱 중이거나 경로 미설정이면 스킵
        if (_isIndexing || _settings.IndexRootPaths.Length == 0) return;

        // ① FileWatcher 가 변경을 감지했거나
        // ② 마지막 인덱싱 후 30분 이상 경과(앱 종료 중 발생한 변경 보정용)
        //    일 때만 실행 — 변경이 없으면 조용히 스킵
        bool hasPending  = _watcher.HasPendingChanges;
        bool longElapsed = !_settings.LastIndexedAt.HasValue ||
                           (DateTime.Now - _settings.LastIndexedAt.Value).TotalMinutes >= 30;

        if (!hasPending && !longElapsed) return;

        _watcher.ResetPendingChanges();   // 플래그 초기화 (이 주기의 변경은 이번에 처리)
        _isIndexing = true;
        _autoIndexCycle++;
        sslIndexStatus.Text = $"자동 인덱싱 중… ({DateTime.Now:HH:mm})";

        _ = Task.Run(async () =>
        {
            try   { await _indexer.UpdateIndexAsync(); }
            catch { Invoke(() => { _isIndexing = false; sslIndexStatus.Text = string.Empty; }); }
        });
    }

    // ─────────────────────────────────────────────────────────
    // 포맷 헬퍼
    // ─────────────────────────────────────────────────────────
    private static string FormatSize(long bytes) => bytes switch
    {
        < 1_024         => $"{bytes} Bytes",
        < 1_048_576     => $"{bytes / 1_024.0:F1} KB",
        < 1_073_741_824 => $"{bytes / 1_048_576.0:F1} MB",
        _               => $"{bytes / 1_073_741_824.0:F1} GB",
    };

    private static string FormatFileType(string? ext) => ext?.ToLowerInvariant() switch
    {
        ".docx" or ".doc" => "Word 문서",  ".xlsx" or ".xls" => "Excel",
        ".pptx" or ".ppt" => "PowerPoint", ".pdf" => "PDF",
        ".txt" => "텍스트",               ".md"  => "Markdown",
        ".jpg" or ".jpeg" => "JPEG 이미지", ".png" => "PNG 이미지",
        ".gif" => "GIF",                  ".bmp" => "비트맵",
        ".cs"  => "C# 소스",              ".py"  => "Python",
        ".js"  => "JavaScript",           ".ts"  => "TypeScript",
        ".html" or ".htm" => "HTML",      ".css" => "CSS",
        ".sql" => "SQL",                  ".json" => "JSON",
        ".xml" => "XML",                  ".zip" => "ZIP",
        ".exe" => "실행 파일",            ".lnk" => "바로 가기",
        null or "" => "파일",
        var e => $"{e.TrimStart('.').ToUpperInvariant()} 파일",
    };

    // ─────────────────────────────────────────────────────────
    // 폼 종료
    // ─────────────────────────────────────────────────────────
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _debounceTimer.Stop(); _debounceTimer.Dispose();
        _autoIndexTimer?.Stop(); _autoIndexTimer?.Dispose();
        _snippetCts?.Cancel(); _snippetCts?.Dispose();
        _watcher.Dispose(); _db.Dispose();
        _settings.WindowMaximized = (WindowState == FormWindowState.Maximized);
        if (WindowState == FormWindowState.Normal)
        { _settings.WindowX = Location.X; _settings.WindowY = Location.Y; _settings.WindowWidth = Size.Width; _settings.WindowHeight = Size.Height; }
        else
        { var rb = RestoreBounds; _settings.WindowX = rb.X; _settings.WindowY = rb.Y; _settings.WindowWidth = rb.Width; _settings.WindowHeight = rb.Height; }
        _settings.Save();
        base.OnFormClosed(e);
    }
}
