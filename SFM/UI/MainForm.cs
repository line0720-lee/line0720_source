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
    private readonly FileParser               _parser     = new();
    private          CancellationTokenSource? _snippetCts;
    private          string?                  _currentSnippetPath;   // 현재 표시 중인 파일 경로 (깜빡임 방지)

    // ─────────────────────────────────────────────────────────
    // UI 상태
    // ─────────────────────────────────────────────────────────
    private List<SearchResult> _results     = [];
    private int                _searchPage  = 0;    // 무한 스크롤 현재 페이지
    private long               _totalCount  = -1;   // 전체 검색 건수 (-1 = 미계산)
    private bool               _isLoadingMore = false;
    private string             _lastKeyword = string.Empty;

    // IndexProgressChanged 쓰로틀링 (백그라운드 내용 인덱싱 깜빡임 방지)
    private long _lastProgressTickMs = 0;

    private readonly System.Windows.Forms.Timer _debounceTimer;
    private readonly ListBox                    _recentBox;

    // ListView 셀 툴팁 (이름·경로 열 전체 텍스트 표시)
    private readonly ToolTip _cellTip = new() { AutoPopDelay = 8000, InitialDelay = 400, ReshowDelay = 200 };
    private int              _cellTipLastRow = -1;
    private int              _cellTipLastCol = -1;

    // 검색 힌트 팝업 (? 버튼 호버)
    private SearchHintPopup?            _hintPopup;
    private System.Windows.Forms.Timer? _hintHideTimer;

    // IndexCompleted 자동 재검색 쿨다운 (15초)
    private DateTime _lastAutoReSearch = DateTime.MinValue;

    // 컬럼 정렬
    private int  _sortColumn    = -1;
    private bool _sortAscending = true;
    private readonly string[] _colHeaders = ["이름", "경로", "파일 크기", "수정 날짜", "유형"];


    // ─────────────────────────────────────────────────────────
    // 파일유형 필터
    // ─────────────────────────────────────────────────────────
    // 문서 (한글 HWP/HWPX 포함)
    private static readonly string[] ExtDoc   = [".docx",".doc",".xlsx",".xls",".pptx",".ppt",
                                                  ".txt",".md",".rtf",".hwp",".hwpx"];
    private static readonly string[] ExtPdf   = [".pdf"];
    private static readonly string[] ExtImage = [".jpg",".jpeg",".png",".gif",".bmp",".svg",".webp",".ico"];
    // 소스 코드 (Pascal .pas, Pro*C .pc 포함)
    private static readonly string[] ExtCode  = [".cs",".py",".js",".ts",".java",".cpp",".c",".h",
                                                  ".go",".rs",".rb",".php",".swift",".kt",
                                                  ".pas",".pc",
                                                  ".html",".htm",".css",".scss",".sql",".sh",".bat",".ps1"];

    // 확장자 → imgListSmall 인덱스 (Windows 셸 아이콘 캐시)
    private readonly Dictionary<string, int> _shellIconCache =
        new(StringComparer.OrdinalIgnoreCase);

    // ── 자동 인덱싱 ──────────────────────────────────────────────
    private bool _isIndexing = false;
    private int  _autoIndexCycle = 0;          // 5분 주기 카운터 (6회 = 30분마다 Vacuum)
    private System.Windows.Forms.Timer? _autoIndexTimer;

    /// <summary>인덱서 백그라운드 작업 전체를 취소하는 토큰. 폼 종료 시 Cancel.</summary>
    private readonly CancellationTokenSource _indexCts = new();

    // 첫 실행 인덱싱 상태 추적
    private bool _isFirstRunStage1  = false;   // 1단계: 파일명+메타 인덱싱 중
    private bool _isFirstRunStage2  = false;   // 2단계: 문서 내용 학습 중 (백그라운드)
    private bool _firstRunCompleted = false;   // 완료 상태 — true 면 HideFirstRunOverlay 자동 호출 차단
    private bool _isClosing         = false;   // OnFormClosed 이중 실행 방지

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
    private readonly SplashForm? _splash;

    public MainForm(SplashForm? splash = null)
    {
        _splash = splash;
        InitializeComponent();
        EnableAntiFlicker();          // ← 깜빡임 방지 (InitializeComponent 직후)
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

        // ── 이벤트 연결 전에 내용 검색 UI 초기화 ────────────────────────────────
        // EnableContentIndexing = true  → 학습 완료: 체크박스 활성(사용자가 켤 수 있음)
        // EnableContentIndexing = false → 학습 전/리셋: 체크박스 비활성(회색)
        // EnableContentSearch   = 사용자 체크 상태 (학습 완료 후 사용자가 직접 선택)

        // 내용 검색 UI — 항상 표시하되, 내용 학습 완료 전까지 비활성(회색)
        chkContent.Checked         = _settings.EnableContentSearch;
        tsmiSearchContent.Checked  = _settings.EnableContentSearch;
        chkContent.Visible         = true;
        tsmiSearchContent.Visible  = true;
        chkContent.Enabled         = _settings.EnableContentIndexing;   // 학습 완료 여부로 활성/비활성
        tsmiSearchContent.Enabled  = _settings.EnableContentIndexing;

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
        };

        // 스플래시 닫기 + 첫 실행 오버레이는 Shown 이벤트에서 처리.
        // Load 단계에서는 폼이 아직 화면에 표시되지 않아 오버레이가 렌더링되지 않는다.
        Shown += (_, _) =>
        {
            // 스플래시 닫기 — MainForm이 화면에 완전히 표시된 후
            try { _splash?.Close(); } catch { /* 무시 */ }

            // 열 너비 초기화 — 폼이 완전히 표시된 후 ClientSize 가 확정되므로 여기서 실행
            if (_settings.AutoResizeColumns) AutoResizeColumns();

            // 첫 실행 감지: LastIndexedAt == null → 자동 인덱싱 시작
            if (_settings.LastIndexedAt == null)
            {
                StartFirstRunIndex();
            }
        };
    }

    // ─────────────────────────────────────────────────────────
    // 창 위치 복원
    // ─────────────────────────────────────────────────────────
    // 깜빡임 방지 — 더블 버퍼링 활성화
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Windows 10에서 발생하는 ListView / 패널 깜빡임을 제거한다.
    /// · ListView: 네이티브 LVS_EX_DOUBLEBUFFER (가장 효과적)
    /// · Panel류: 리플렉션으로 DoubleBuffered=true
    /// </summary>
    private void EnableAntiFlicker()
    {
        // ── ListView 네이티브 더블 버퍼링 ─────────────────────
        // Handle을 사용하려면 InitializeComponent 이후여야 함
        NativeMethods.SendMessage(
            listResults.Handle,
            NativeMethods.LVM_SETEXTENDEDLISTVIEWSTYLE,
            NativeMethods.LVS_EX_DOUBLEBUFFER,
            NativeMethods.LVS_EX_DOUBLEBUFFER);

        // ── 관리 코드 DoubleBuffered (Panel / SplitterPanel) ──
        SetDoubleBuffered(listResults);
        SetDoubleBuffered(pnlSearch);
        SetDoubleBuffered(pnlOptions);
        SetDoubleBuffered(pnlSnippet);
        SetDoubleBuffered(pnlSnippetBar);
        SetDoubleBuffered(pnlOverlay);
        SetDoubleBuffered(pnlOverlayCard);
        SetDoubleBuffered(splitPreview.Panel1);
        SetDoubleBuffered(splitPreview.Panel2);
    }

    /// <summary>리플렉션으로 내부 DoubleBuffered 속성을 true 로 설정한다.</summary>
    private static void SetDoubleBuffered(Control ctrl)
    {
        typeof(Control)
            .GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)
            ?.SetValue(ctrl, true);
    }

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
    // 페이지네이션 (무한 스크롤로 대체 — 버튼 없음)
    // ─────────────────────────────────────────────────────────
    private static void InitPagination() { /* ◀▶ 버튼 제거. 무한 스크롤은 ExtListView.NearBottom 으로 처리. */ }

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
            _settings.Save();   // 체크 상태를 즉시 저장 — 재시작 후에도 유지
            _ = RunSearchAsync();
        };

        // 내용 검색 비활성(학습 중) 상태에서 체크박스 영역 클릭 → 안내 팝업
        // Enabled=false 인 컨트롤은 클릭 이벤트가 부모로 버블링된다.
        pnlOptions.MouseDown += (_, e) =>
        {
            if (chkContent.Enabled) return;
            var bounds = new Rectangle(chkContent.Location, chkContent.Size);
            if (bounds.Contains(e.Location))
                ShowContentSearchNotReadyMessage();
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
        listResults.MouseMove            += OnListMouseMove;
        listResults.MouseLeave           += (_, _) => { _cellTip.SetToolTip(listResults, null); _cellTipLastRow = -1; _cellTipLastCol = -1; };

        // ── 무한 스크롤 (ExtListView.NearBottom) ─────────────────
        listResults.NearBottom += (_, _) =>
        {
            // 더 불러올 결과가 있고 현재 로딩 중이 아닌 경우에만 추가 로드
            if (!_isLoadingMore && (_totalCount < 0 || _results.Count < _totalCount))
                _ = LoadMoreResultsAsync();
        };

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

        // ── 검색 힌트 (?) 버튼 ──────────────────────────────────────
        btnSearchHint.MouseEnter += OnSearchHintMouseEnter;
        btnSearchHint.MouseLeave += OnSearchHintMouseLeave;

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

        // ── 창 크기 변경 / 스플리터 이동 → 열 너비 자동 재조정 ─────
        SizeChanged += (_, _) =>
        {
            if (tsmiViewAutoCol.Checked) BeginInvoke(AutoResizeColumns);
        };
        splitPreview.SplitterMoved += (_, _) =>
        {
            if (tsmiViewAutoCol.Checked) AutoResizeColumns();
        };

        tsmiSearchContent.Click += (_, _) => chkContent.Checked = tsmiSearchContent.Checked;
        tsmiSearchClear.Click   += (_, _) => { txtSearch.Clear(); txtSearch.Focus(); };

        tsmiToolsReindex.Click    += OnReindexClick;
        tsmiToolsVacuum.Click     += OnVacuumClick;
        tsmiToolsResetIndex.Click += OnResetIndexClick;

        // ── 오버레이 닫기 버튼 ───────────────────────────────────
        // 사용자가 직접 닫는 경우: 플래그 해제 후 강제 숨김
        btnOverlayClose.Click += (_, _) =>
        {
            _firstRunCompleted      = false;
            btnOverlayClose.Visible = false;
            pnlOverlay.Visible      = false;
            txtSearch.Focus();
        };

        tsmiHelpAbout.Click += (_, _) => new AboutForm().ShowDialog(this);

        // ── Indexer 이벤트 ───────────────────────────────────────
        // IsDisposed / IsHandleCreated 가드: 폼이 닫힌 뒤 백그라운드 스레드가
        // Invoke를 호출하면 ObjectDisposedException 발생 → 사전 차단
        _indexer.IndexProgressChanged += p =>
        {
            if (IsDisposed || !IsHandleCreated) return;

            // ── 쓰로틀링 ─────────────────────────────────────────────
            // · 오버레이 표시 중(1단계 파일명 인덱싱): 항상 업데이트 (사용자에게 진행 보여줌)
            // · 백그라운드 내용 인덱싱(2단계): 200 ms 마다만 업데이트 → 깜빡임 방지
            var now = Environment.TickCount64;
            if (!_isFirstRunStage1 && now - _lastProgressTickMs < 200) return;
            _lastProgressTickMs = now;

            try
            {
                // BeginInvoke: 백그라운드 스레드를 UI 완료까지 블로킹하지 않음
                BeginInvoke(() =>
                {
                    sslIndexStatus.Text = $"[{p.Phase}] {p.Processed:N0}/{p.Total:N0}  {Path.GetFileName(p.FilePath)}";
                    // 오버레이 진행 표시: 1단계(파일명 인덱싱) 중에만 업데이트.
                    // 2단계(내용 학습) 중에는 오버레이가 완료 상태이므로 건드리지 않는다.
                    if (pnlOverlay.Visible && _isFirstRunStage1)
                    {
                        if (p.Total > 0)
                        {
                            // 총 파일 수 확정 → Marquee → Continuous 퍼센트 진행 바로 전환
                            if (pbOverlay.Style != ProgressBarStyle.Continuous)
                                pbOverlay.Style = ProgressBarStyle.Continuous;

                            int pct = Math.Clamp((int)((double)p.Processed / p.Total * 100), 0, 100);
                            pbOverlay.Value = pct;

                            // 진행 텍스트: 수집 완료 직후(Processed=0)와 인덱싱 중 구분
                            lblOverlayProgress.Text = p.Processed == 0
                                ? $"총 {p.Total:N0}개 파일 발견.  분석을 시작합니다..."
                                : $"{p.Processed:N0} / {p.Total:N0} 파일 처리 중... ({pct}%)";

                            // 단계별 설명 문구
                            lblOverlayDesc.Text = p.Phase switch
                            {
                                "수집 완료" => "파일 분석을 시작합니다...",
                                "인덱싱"   => "파일 이름과 정보를 인덱싱하고 있습니다...",
                                var s when s.Contains("내용") => "파일 내용을 분석하고 있습니다...",
                                _ => "검색 인덱스를 구축하고 있습니다..."
                            };
                        }
                        else
                        {
                            // Total=0: 아직 파일 목록 수집 중 (Marquee 유지)
                            lblOverlayProgress.Text = $"파일 목록 수집 중...  {p.Processed:N0}개 발견";
                            lblOverlayDesc.Text     = "파일 목록을 수집하고 있습니다...";
                        }
                    }
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        };

        _indexer.IndexCompleted += r =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                Invoke(() =>
                {
                    _isIndexing = false;

                    // ── 첫 실행 1단계(파일명 인덱싱) 완료 ────────────────────
                    if (_isFirstRunStage1)
                    {
                        _isFirstRunStage1 = false;

                        var now1 = DateTime.Now;
                        _settings.LastIndexedAt = now1;
                        _settings.Save();
                        sslLastIndexed.Text = $"인덱싱: {now1:yyyy-MM-dd HH:mm}";
                        RefreshDbStats();

                        // 빌드 완료 후 FileWatcher 재시작 (빌드 중에는 중지했었음)
                        _watcher.StartWatching(_settings.IndexRootPaths);

                        // ── 2단계: 문서 유형 내용 학습 백그라운드 시작 ────────
                        _isIndexing       = true;
                        _isFirstRunStage2 = true;

                        // Task.Run 으로 UI 스레드에서 완전히 분리
                        // → 내용 인덱싱 중에도 UI/파일 검색 완전히 사용 가능
                        var ct2 = _indexCts.Token;
                        _ = Task.Run(async () =>
                        {
                            try   { await _indexer.BuildMissingContentAsync(ct2); }
                            catch (OperationCanceledException) { /* 정상 종료 */ }
                            catch (Exception ex)               { Debug.WriteLine($"[ContentIndex] {ex}"); }
                        }, ct2);

                        sslIndexStatus.Text = $"✅ 인덱싱 완료 ({r.TotalIndexed:N0}개)  —  📄 문서 내용 학습 중…";

                        // ── 오버레이를 완료 상태로 전환 ──────────────────────
                        ShowFirstRunOverlayCompleted(r.TotalIndexed, r.Elapsed);
                        return;
                    }

                    // ── 첫 실행 2단계(문서 내용 학습) 완료 ──────────────────
                    if (_isFirstRunStage2)
                    {
                        _isFirstRunStage2 = false;

                        // 내용 인덱싱 준비 완료 저장 (검색 ON/OFF 는 사용자가 직접 선택)
                        _settings.EnableContentIndexing = true;
                        _settings.Save();

                        // 체크박스 활성화 — Checked 는 false 유지 (사용자가 선택)
                        chkContent.Enabled         = true;
                        tsmiSearchContent.Enabled  = true;
                        chkContent.Checked         = false;
                        tsmiSearchContent.Checked  = false;
                        // 글자색 정상 복구 (회색 → 검정/흰색)
                        chkContent.ForeColor = _settings.IsDarkMode
                            ? Color.FromArgb(224, 224, 235)
                            : Color.FromArgb(26,  32,  44);

                        sslIndexStatus.Text = r.Added > 0
                            ? $"📄 내용 학습 완료 ({r.Added:N0}개 문서)  —  F4 로 내용 검색을 켤 수 있습니다"
                            : "📄 내용 학습 완료  —  F4 로 내용 검색을 켤 수 있습니다";
                        RefreshDbStats();
                        return;
                    }

                    HideFirstRunOverlay();

                    bool hasRealChanges = r.Added + r.Updated + r.Deleted > 0;

                    if (hasRealChanges)
                    {
                        sslIndexStatus.Text = $"완료 {r.TotalIndexed:N0}개  +{r.Added} ~{r.Updated} -{r.Deleted}  {r.Elapsed.TotalSeconds:F1}s";

                        var now = DateTime.Now;
                        _settings.LastIndexedAt = now;
                        _settings.Save();
                        sslLastIndexed.Text = $"인덱싱: {now:yyyy-MM-dd HH:mm}";

                        RefreshDbStats();

                        if (r.Deleted > 0)
                            Task.Run(() => { try { _db.Vacuum(); } catch { } });

                        // 재검색: 마지막 자동 재검색으로부터 15초 이상 지난 경우만 실행
                        // (FileWatcher/자동 인덱스가 짧은 간격으로 완료되어도 반복 노출 방지)
                        if (!string.IsNullOrWhiteSpace(txtSearch.Text) &&
                            (DateTime.Now - _lastAutoReSearch).TotalSeconds >= 15)
                        {
                            _lastAutoReSearch = DateTime.Now;
                            _ = RunSearchAsync();
                        }
                    }
                    else
                    {
                        sslIndexStatus.Text = string.Empty;
                    }
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            catch (Exception ex) { Debug.WriteLine($"[IndexCompleted] {ex}"); }   // 배경 스레드로 탈출 방지
        };

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

        btnSearchHint.BackColor                         = cSurface;
        btnSearchHint.ForeColor                         = Color.FromArgb(100, 160, 240);
        btnSearchHint.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 100, 160, 240);
        btnSearchHint.FlatAppearance.MouseDownBackColor = Color.FromArgb(55, 100, 160, 240);
        _hintPopup?.SetDark(true);

        chkContent.ForeColor = chkContent.Enabled ? cText : Color.FromArgb(85, 85, 110);
        lblFilter.ForeColor = cSub;
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

        _recentBox.BackColor = cSurf2; _recentBox.ForeColor = cText;

        // 오버레이
        pnlOverlay.BackColor         = Color.FromArgb(18, 22, 38);
        lblOverlayTitle.ForeColor    = Color.FromArgb(30, 35, 50);
        lblOverlayDesc.ForeColor     = Color.FromArgb(80, 100, 130);
        lblOverlayProgress.ForeColor = Color.FromArgb(110, 125, 148);
        lblOverlayNote.ForeColor     = Color.FromArgb(140, 160, 190);
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

        btnSearchHint.BackColor                         = cSurface;
        btnSearchHint.ForeColor                         = cAccent;
        btnSearchHint.FlatAppearance.MouseOverBackColor = cSurf2;
        btnSearchHint.FlatAppearance.MouseDownBackColor = Color.FromArgb(220, 235, 255);
        _hintPopup?.SetDark(false);

        chkContent.ForeColor = chkContent.Enabled ? cText : Color.FromArgb(160, 160, 170);
        lblFilter.ForeColor = cSub;
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

        _recentBox.BackColor = cSurface; _recentBox.ForeColor = cText;

        // 오버레이
        pnlOverlay.BackColor         = Color.FromArgb(30, 35, 50);
        lblOverlayTitle.ForeColor    = Color.FromArgb(26, 32, 44);
        lblOverlayDesc.ForeColor     = Color.FromArgb(80, 100, 130);
        lblOverlayProgress.ForeColor = Color.FromArgb(110, 125, 148);
        lblOverlayNote.ForeColor     = Color.FromArgb(140, 160, 190);
    }

    // ─────────────────────────────────────────────────────────
    // ListView 셀 툴팁 (이름·경로 열 전체 내용 표시)
    // ─────────────────────────────────────────────────────────
    private void OnListMouseMove(object? sender, MouseEventArgs e)
    {
        var hit = listResults.HitTest(e.X, e.Y);
        if (hit.Item == null) { _cellTip.SetToolTip(listResults, null); _cellTipLastRow = -1; _cellTipLastCol = -1; return; }

        int rowIdx = hit.Item.Index;
        if (rowIdx >= _results.Count) return;

        // 마우스 X 위치로 어느 컬럼인지 계산
        int colIdx = 0, cumX = 0;
        foreach (ColumnHeader col in listResults.Columns)
        {
            if (e.X < cumX + col.Width) { colIdx = col.Index; break; }
            cumX += col.Width;
        }

        // 동일 셀이면 재설정 불필요
        if (rowIdx == _cellTipLastRow && colIdx == _cellTipLastCol) return;
        _cellTipLastRow = rowIdx;
        _cellTipLastCol = colIdx;

        var r = _results[rowIdx];
        string tipText = colIdx switch
        {
            0 => r.FileName,                                          // 이름 열: 파일명
            1 => r.FilePath,                                          // 경로 열: 전체 경로
            _ => string.Empty
        };

        _cellTip.SetToolTip(listResults, tipText);
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
        // ※ Dispose() 금지: 실행 중인 Task 가 같은 CTS 를 참조 중이므로
        //   Cancel() 만 호출하고 GC 에 회수를 맡긴다.
        _snippetCts?.Cancel();
        _snippetCts = null;

        if (r is null)
        {
            _currentSnippetPath = null;
            lblSnippetPath.Text = "항목을 선택하면 미리보기가 표시됩니다";
            rtbSnippet.Clear();
            return;
        }

        lblSnippetPath.Text = r.FilePath;

        // 테마별 색상
        var clrText = _settings.IsDarkMode ? Color.FromArgb(224, 224, 235) : Color.FromArgb(26, 32, 44);
        var clrSub  = _settings.IsDarkMode ? Color.FromArgb(140, 140, 165) : SystemColors.GrayText;

        // ── 깜빡임 방지 ────────────────────────────────────────────
        // 동일 파일 재로딩(자동 재검색 등)이면 "로딩 중…" 표시를 생략하고
        // 기존 내용을 유지한 채 백그라운드에서 조용히 갱신한다.
        bool isSamePath = r.FilePath.Equals(_currentSnippetPath, StringComparison.OrdinalIgnoreCase);
        if (!isSamePath)
        {
            _currentSnippetPath = r.FilePath;
            rtbSnippet.Clear();
            rtbSnippet.ForeColor = clrSub;
            rtbSnippet.Text = "내용 불러오는 중…";
        }

        var cts           = new CancellationTokenSource();
        _snippetCts       = cts;
        var keyword       = txtSearch.Text.Trim();
        var highlightWord = Searcher.StripWildcards(keyword);   // 와일드카드 제거 후 하이라이트
        var filePath      = r.FilePath;

        _ = Task.Run(async () =>
        {
            try
            {
                // 파일 전체 내용 파싱 (최대 1 MB 기준, 초과 시 500 KB 제한)
                var content = await _parser.ParseFileContent(filePath, maxSizeMB: 1);

                if (cts.IsCancellationRequested || IsDisposed || !IsHandleCreated) return;

                // 미리보기 최대 100,000자 표시
                const int PreviewLimit = 100_000;
                bool truncated = content.Length > PreviewLimit;
                if (truncated) content = content[..PreviewLimit];

                try
                {
                    Invoke(() =>
                    {
                        if (cts.IsCancellationRequested) return;

                        // WM_SETREDRAW(false) 로 텍스트 설정 + 하이라이트 적용을 한 번에 그림
                        // → Select() 루프마다 리페인트 하던 깜빡임 제거
                        NativeMethods.SendMessage(rtbSnippet.Handle, NativeMethods.WM_SETREDRAW, 0, 0);
                        try
                        {
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
                        }
                        finally
                        {
                            NativeMethods.SendMessage(rtbSnippet.Handle, NativeMethods.WM_SETREDRAW, 1, 0);
                            rtbSnippet.Invalidate();
                            rtbSnippet.Update();   // 즉시 단일 페인트 — 중간 상태 노출 없음
                        }
                    });
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch
            {
                if (!cts.IsCancellationRequested && !IsDisposed && IsHandleCreated)
                {
                    try { Invoke(() => { rtbSnippet.ForeColor = clrSub; rtbSnippet.Text = "(파일을 읽을 수 없습니다)"; }); }
                    catch { }
                }
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
            _results = []; _searchPage = 0; _totalCount = -1; _lastKeyword = string.Empty;
            listResults.VirtualListSize = 0; sslResultCount.Text = "준비";
            ResetSortHeaders(); UpdateSnippetPanel(null);
            Text = "SFM — Smart File Manager"; return;
        }

        // 새 키워드 검색: 상태 초기화
        // 동일 키워드 재검색(자동 재인덱싱 등): 현재 미리보기 파일 경로 기억
        bool isNewSearch = keyword != _lastKeyword;
        string? prevSelectedPath = isNewSearch ? null : GetSelectedResult()?.FilePath;

        if (isNewSearch)
        {
            _searchPage = 0; _totalCount = -1; _lastKeyword = keyword;
            ResetSortHeaders();
        }

        var query = new SearchQuery
        {
            Keyword         = keyword,
            MaxResults      = _settings.MaxSearchResults,
            SearchInContent = _settings.EnableContentSearch && chkContent.Checked,
            Extensions      = GetSelectedExtensions(),
            Page            = 0,   // 항상 처음부터
        };
        sslResultCount.Text = "검색 중…";

        try
        {
            var sw = Stopwatch.StartNew();

            // 검색 결과(첫 페이지) + 전체 건수를 병렬로 조회
            var searchTask = _searcher.SearchAsync(query);
            var countTask  = _searcher.CountAsync(query);
            await Task.WhenAll(searchTask, countTask);
            sw.Stop();

            if (txtSearch.Text.Trim() != keyword) return;

            _results    = searchTask.Result;
            _totalCount = countTask.Result;
            _searchPage = 0;
            _sortColumn = -1;

            listResults.VirtualListSize = _results.Count;
            listResults.Invalidate();

            UpdateStatusCountText(sw.Elapsed.TotalSeconds);

            if (_results.Count > 0)
            {
                if (tsmiViewAutoCol.Checked) AutoResizeColumns();
                if (!splitPreview.Panel2Collapsed)
                {
                    if (prevSelectedPath is not null)
                    {
                        // 동일 키워드 재검색(자동 인덱싱 등):
                        // 이전에 보던 파일이 결과에 있으면 그 파일 미리보기 유지,
                        // 결과에서 사라졌으면 첫 번째 결과로 대체.
                        int idx = _results.FindIndex(r =>
                            r.FilePath.Equals(prevSelectedPath, StringComparison.OrdinalIgnoreCase));
                        UpdateSnippetPanel(idx >= 0 ? _results[idx] : _results[0]);
                    }
                    else
                    {
                        // 새 키워드 검색 또는 이전 선택 없음: 첫 번째 결과 미리보기
                        UpdateSnippetPanel(_results[0]);
                    }
                }
            }
        }
        catch (Microsoft.Data.Sqlite.SqliteException sqEx) when (sqEx.SqliteErrorCode == 11)
        {
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

    /// <summary>
    /// 스크롤 하단 도달 시 다음 500개를 추가로 로드하여 _results 에 누적한다.
    /// </summary>
    private async Task LoadMoreResultsAsync()
    {
        if (_isLoadingMore) return;
        var keyword = _lastKeyword;
        if (string.IsNullOrEmpty(keyword)) return;

        // 이미 전체 로드 완료된 경우
        if (_totalCount >= 0 && _results.Count >= _totalCount) return;

        _isLoadingMore = true;
        _searchPage++;

        var query = new SearchQuery
        {
            Keyword         = keyword,
            MaxResults      = _settings.MaxSearchResults,
            SearchInContent = _settings.EnableContentSearch && chkContent.Checked,
            Extensions      = GetSelectedExtensions(),
            Page            = _searchPage,
        };

        sslResultCount.Text = $"추가 로딩 중…  ({_results.Count:N0}개 표시)";

        try
        {
            var more = await _searcher.SearchAsync(query);
            if (txtSearch.Text.Trim() != keyword) return;
            if (more.Count == 0) { UpdateStatusCountText(); return; }

            _results.AddRange(more);
            listResults.VirtualListSize = _results.Count;
            listResults.Invalidate();
            UpdateStatusCountText();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoadMoreResultsAsync] {ex}");
            _searchPage--;   // 실패 시 페이지 롤백
        }
        finally
        {
            _isLoadingMore = false;
        }
    }

    /// <summary>상태 표시줄 결과 건수 텍스트를 현재 _results / _totalCount 기준으로 갱신한다.</summary>
    private void UpdateStatusCountText(double? elapsedSec = null)
    {
        var timeStr = elapsedSec.HasValue ? $"  ({elapsedSec.Value:F3}초)" : string.Empty;

        if (_results.Count == 0)
        {
            sslResultCount.Text = $"결과 없음{timeStr}";
            return;
        }

        bool hasMore = _totalCount > 0 && _results.Count < _totalCount;

        if (hasMore)
            sslResultCount.Text = $"표시: {_results.Count:N0} / 전체: {_totalCount:N0}개{timeStr}   ↓ 스크롤하면 더 보기";
        else if (_totalCount > 0)
            sslResultCount.Text = $"전체: {_totalCount:N0}개{timeStr}";
        else
            sslResultCount.Text = $"{_results.Count:N0}개{timeStr}";
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
        // ReadLock 으로 _readConn 에서 읽기 — 내용 인덱싱(_writeConn) 과 완전 독립.
        _ = Task.Run(async () =>
        {
            await _db.ReadLock.WaitAsync().ConfigureAwait(false);
            List<string> recents;
            try { recents = _db.GetRecentSearches(10); }
            finally { _db.ReadLock.Release(); }

            if (recents.Count == 0 || IsDisposed || !IsHandleCreated) return;
            BeginInvoke(() =>
            {
                _recentBox.Items.Clear();
                foreach (var s in recents) _recentBox.Items.Add(s);
                var screenPt = txtSearch.Parent!.PointToScreen(txtSearch.Location);
                var formPt   = PointToClient(screenPt);
                _recentBox.Location = new Point(formPt.X, pnlSearch.Bottom);
                _recentBox.Width    = txtSearch.Width;
                _recentBox.Height   = Math.Min(recents.Count * 22 + 4, 180);
                _recentBox.BringToFront(); _recentBox.Visible = true;
            });
        });
    }

    /// <summary>
    /// 내용 검색 기능이 아직 준비되지 않았을 때 (학습 중) 사용자에게 안내 팝업을 표시한다.
    /// F4 단축키 또는 비활성 상태 체크박스 클릭 시 호출된다.
    /// </summary>
    private void ShowContentSearchNotReadyMessage()
    {
        MessageBox.Show(
            "내용 학습이 완료되면 해당 기능이 활성화됩니다.\n\n" +
            "문서 내용 학습이 백그라운드에서 진행 중입니다.\n" +
            "학습 완료 시 자동으로 내용 검색(F4)이 활성화됩니다.",
            "내용 검색 — 준비 중",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
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
                    _results = []; _searchPage = 0; _totalCount = -1; _lastKeyword = string.Empty;
                    listResults.VirtualListSize = 0;
                    sslResultCount.Text = "준비";
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
            case Keys.F4:
                if (!chkContent.Enabled)
                { ShowContentSearchNotReadyMessage(); return true; }
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
        _isIndexing = true;
        var rootPaths = _settings.IndexRootPaths;
        var ct        = _indexCts.Token;
        _ = Task.Run(async () =>
        {
            try   { await _indexer.BuildIndexAsync(rootPaths, ct); }
            catch (OperationCanceledException) { /* 정상 종료 */ }
            catch (Exception ex)               { Debug.WriteLine($"[Reindex] {ex}"); }
        }, ct);
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

    private void OnResetIndexClick(object? sender, EventArgs e)
    {
        var msg = "인덱스를 완전히 초기화하고 처음부터 다시 학습합니다.\n\n"
                + "기존 인덱스 데이터가 모두 삭제되며,\n"
                + "전체 파일 학습이 다시 진행됩니다.\n\n"
                + "계속하시겠습니까?";
        if (MessageBox.Show(msg, "인덱스 초기화", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        // ① 진행 중인 인덱싱 취소 플래그 (새 BuildIndexAsync 호출 전 정리)
        _isFirstRunStage1 = false;
        _isFirstRunStage2 = false;
        _isIndexing       = false;

        // ② DB 데이터 전체 삭제
        try
        {
            _db.ClearAllIndexes();
            _db.Vacuum();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"인덱스 삭제 중 오류가 발생했습니다.\n{ex.Message}",
                "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // ③ 설정 초기화: LastIndexedAt = null → 첫 실행으로 인식
        _settings.LastIndexedAt         = null;
        _settings.EnableContentSearch   = false;   // 내용 인덱스 삭제 → 내용 검색 비활성화
        _settings.EnableContentIndexing = false;
        _settings.Save();

        // 내용 검색 체크박스 리셋 (DB 초기화로 내용 인덱스가 없으므로 비활성화)
        chkContent.Checked         = false;
        tsmiSearchContent.Checked  = false;
        chkContent.Enabled         = false;
        tsmiSearchContent.Enabled  = false;
        // 글자색 회색으로 (비활성 상태 시각적 표시)
        chkContent.ForeColor = _settings.IsDarkMode
            ? Color.FromArgb(85, 85, 110)
            : Color.FromArgb(160, 160, 170);

        // ④ 검색 결과 초기화
        _results = []; _searchPage = 0; _totalCount = -1; _lastKeyword = string.Empty;
        listResults.VirtualListSize = 0;
        sslResultCount.Text = "준비";
        sslLastIndexed.Text = string.Empty;
        RefreshDbStats();

        // ⑤ 첫 실행 인덱싱 재시작
        StartFirstRunIndex();
    }

    // ─────────────────────────────────────────────────────────
    // DB 통계
    // ─────────────────────────────────────────────────────────
    /// <summary>
    /// DB 통계를 백그라운드에서 읽어 UI 를 갱신한다 (fire-and-forget).
    /// ReadLock 을 사용하여 _readConn 전용으로 읽기 — 내용 인덱싱(_writeConn) 과 완전 독립.
    /// </summary>
    private void RefreshDbStats()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (IsDisposed) return;
                await _db.ReadLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    var s = _db.GetIndexStats();
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(() =>
                        {
                            lblDbInfo.Text  = $"인덱스: {s.TotalFiles:N0}개 파일  ·  {s.DatabaseSizeMb:F1} MB";
                            sslDbStats.Text = $"인덱스: {s.TotalFiles:N0}개 파일 ({s.DatabaseSizeMb:F1} MB)";
                        });
                }
                finally { _db.ReadLock.Release(); }
            }
            catch { }
        });
    }

    // ─────────────────────────────────────────────────────────
    // 검색 힌트 팝업 (? 버튼)
    // ─────────────────────────────────────────────────────────
    private void OnSearchHintMouseEnter(object? sender, EventArgs e)
    {
        _hintHideTimer?.Stop();
        EnsureHintPopup();
        _hintPopup!.SetDark(_settings.IsDarkMode);
        _hintPopup.ShowBelow(btnSearchHint);
    }

    private void OnSearchHintMouseLeave(object? sender, EventArgs e)
    {
        EnsureHintHideTimer();
        _hintHideTimer!.Start();
    }

    private void EnsureHintPopup()
    {
        if (_hintPopup != null && !_hintPopup.IsDisposed) return;
        _hintPopup = new SearchHintPopup();
        _hintPopup.Owner = this;   // 항상 메인 창 위에 표시
        _hintPopup.MouseEnter += (_, _) => _hintHideTimer?.Stop();
        _hintPopup.MouseLeave += (_, _) => _hintPopup?.Hide();
    }

    private void EnsureHintHideTimer()
    {
        if (_hintHideTimer != null) return;
        _hintHideTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _hintHideTimer.Tick += (_, _) =>
        {
            _hintHideTimer.Stop();
            _hintPopup?.Hide();
        };
    }

    /// <summary>
    /// 고정 3열(크기·날짜·유형)을 헤더 + 실제 콘텐츠 기준으로 자동 측정 후,
    /// 남은 공간을 이름 40% / 경로 60% 로 분배한다.
    /// 결과가 없을 때는 헤더 텍스트 기준으로 초기화한다.
    /// </summary>
    private void AutoResizeColumns()
    {
        if (listResults.ClientSize.Width <= 0) return;

        var font = listResults.Font;

        // ── 1. 고정 열: 헤더 텍스트로 초기 측정 ──────────────────
        int sizeW     = MeasureColW(font, colSize.Text);
        int modifiedW = MeasureColW(font, colModified.Text);
        int matchW    = MeasureColW(font, colMatch.Text);

        // ── 2. 고정 열: 샘플 콘텐츠 너비와 비교해 더 넓은 값 선택 ──
        if (_results.Count > 0)
        {
            int sample = Math.Min(_results.Count, 200);
            for (int i = 0; i < sample; i++)
            {
                var r = _results[i];
                sizeW     = Math.Max(sizeW,     MeasureColW(font, FormatSize(r.FileSize)));
                modifiedW = Math.Max(modifiedW, MeasureColW(font,
                    r.ModifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")));
                matchW    = Math.Max(matchW,    MeasureColW(font, FormatFileType(r.Extension)));
            }
        }

        colSize.Width     = sizeW;
        colModified.Width = modifiedW;
        colMatch.Width    = matchW;

        // ── 3. 가변 열: 남은 공간을 이름 40% / 경로 60% ───────────
        int scrollbarW = SystemInformation.VerticalScrollBarWidth + 2;
        int fixedTotal = sizeW + modifiedW + matchW;
        int available  = listResults.ClientSize.Width - scrollbarW - fixedTotal;
        if (available < 240) available = 240;

        colFileName.Width = Math.Max(80,  (int)(available * 0.40));
        colPath.Width     = Math.Max(140, available - colFileName.Width);
    }

    /// <summary>TextRenderer 기준 텍스트 실측 너비 + 셀 패딩.</summary>
    private static int MeasureColW(Font font, string text, int padding = 14)
        => TextRenderer.MeasureText(text, font).Width + padding;

    // ─────────────────────────────────────────────────────────
    // 첫 실행 오버레이
    // ─────────────────────────────────────────────────────────

    private void StartFirstRunIndex()
    {
        // ① 루트 경로 미설정 시 로컬 고정 드라이브 전체를 기본값으로 설정
        if (_settings.IndexRootPaths.Length == 0)
        {
            _settings.IndexRootPaths = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName)
                .ToArray();
        }

        // 파일명+메타 인덱싱 (내용 인덱싱 비활성화)
        _settings.EnableContentIndexing = false;
        _settings.Save();

        // 내용 검색 UI 강제 비활성화 — 기존 settings.json 에 EnableContentIndexing=true 가
        // 저장된 상태로 앱이 뜨면 생성자에서 chkContent.Enabled=true 가 될 수 있으므로,
        // 인덱싱을 새로 시작할 때는 반드시 UI 도 함께 초기화한다.
        chkContent.Enabled         = false;
        tsmiSearchContent.Enabled  = false;
        chkContent.Checked         = false;
        tsmiSearchContent.Checked  = false;
        // 글자색 회색으로 (비활성 상태 시각적 표시)
        chkContent.ForeColor = _settings.IsDarkMode
            ? Color.FromArgb(85, 85, 110)
            : Color.FromArgb(160, 160, 170);

        _firstRunCompleted = false;   // 새 빌드 시작 → 완료 차단 플래그 초기화

        // BuildIndexAsync 실행 중에는 FileWatcher 완전 중지.
        // IndexSingleFileAsync 도 IndexCompleted 를 발생시키므로,
        // 빌드 중 파일 변경이 감지되면 _isFirstRunStage1 을 소비해버리는 버그를 방지한다.
        // 빌드 완료 후 IndexCompleted 핸들러 내에서 감시를 재시작한다.
        _watcher.StopWatching();

        ShowFirstRunOverlay();
        _isIndexing       = true;
        _isFirstRunStage1 = true;   // 플래그: IndexCompleted 에서 완료 처리

        // Task.Run 으로 UI 스레드에서 완전히 분리 → UI 응답 유지
        var rootPaths1 = _settings.IndexRootPaths;
        var ct1        = _indexCts.Token;
        _ = Task.Run(async () =>
        {
            try   { await _indexer.BuildIndexAsync(rootPaths1, ct1); }
            catch (OperationCanceledException) { /* 정상 종료 */ }
            catch (Exception ex)               { Debug.WriteLine($"[BuildIndex] {ex}"); }
        }, ct1);
    }

    private void ShowFirstRunOverlay()
    {
        // 진행 바 초기화: Marquee 로 시작 (파일 수 파악 전)
        pbOverlay.Style = ProgressBarStyle.Marquee;
        pbOverlay.Value = 0;
        lblOverlayDesc.Text     = "파일 목록을 수집하고 있습니다...";
        lblOverlayProgress.Text = "잠시 기다려 주세요...";

        pnlOverlay.SetBounds(0, 0, ClientSize.Width, ClientSize.Height);
        CenterOverlayCard();
        pnlOverlay.BringToFront();
        pnlOverlay.Visible = true;
        pnlOverlay.Refresh();       // 즉시 그리기 강제 — Visible=true 직후 렌더링 보장
        Application.DoEvents();     // Marquee 애니메이션 첫 프레임 처리
    }

    private void HideFirstRunOverlay()
    {
        if (!pnlOverlay.Visible) return;
        // _firstRunCompleted == true 인 동안은 어떤 자동 호출도 오버레이를 닫지 않는다.
        // 오직 사용자가 [닫기] 버튼을 클릭해야만 닫힌다.
        if (_firstRunCompleted) return;
        pnlOverlay.Visible = false;
    }

    /// <summary>
    /// 인덱싱 완료 후 오버레이를 '완료 상태'로 전환한다.
    /// 진행 바→100%, 완료 문구 표시, 닫기 버튼 노출.
    /// 오버레이는 사용자가 [검색 시작 →] 버튼을 누를 때까지 유지된다.
    /// </summary>
    private void ShowFirstRunOverlayCompleted(int totalIndexed, TimeSpan elapsed)
    {
        if (!pnlOverlay.Visible) return;    // 이미 닫힌 경우 무시

        // ── 아이콘·제목 ─────────────────────────────────────────
        lblOverlayIcon.Text       = "✅";
        lblOverlayIcon.Font       = new Font("Segoe UI Emoji", 30F);

        lblOverlayTitle.Text      = "학습이 완료되었습니다!";
        lblOverlayTitle.ForeColor = Color.FromArgb(21, 128, 61);   // 초록

        // ── 설명 ─────────────────────────────────────────────────
        lblOverlayDesc.Text       = $"{totalIndexed:N0}개 파일 인덱싱 완료";
        lblOverlayDesc.Font       = new Font("Segoe UI", 10F, FontStyle.Bold);
        lblOverlayDesc.ForeColor  = Color.FromArgb(30, 80, 160);

        // ── 진행 바 → 100% (초록) ─────────────────────────────────
        pbOverlay.Style           = ProgressBarStyle.Continuous;
        pbOverlay.Value           = 100;

        // ── 소요 시간 ─────────────────────────────────────────────
        var elapsedStr = elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes}분 {elapsed.Seconds}초"
            : $"{elapsed.TotalSeconds:F1}초";
        lblOverlayProgress.Text   = $"소요 시간: {elapsedStr}  ·  이제 파일명 검색을 시작할 수 있습니다";
        lblOverlayProgress.ForeColor = Color.FromArgb(60, 120, 60);

        // ── 안내 메모 ─────────────────────────────────────────────
        lblOverlayNote.Text       = "검색창에 파일명을 입력하여 검색을 시작하세요.\r\n    📄 문서 내용 학습이 백그라운드에서 진행 중입니다.  완료 시 내용 검색이 활성화됩니다.";
        lblOverlayNote.ForeColor  = Color.FromArgb(100, 120, 150);

        // ── 닫기 버튼 표시 및 포커스 ─────────────────────────────
        _firstRunCompleted        = true;   // 이 시점부터 자동 닫기 차단
        btnOverlayClose.Visible   = true;
        btnOverlayClose.Focus();

        pnlOverlayCard.Invalidate();    // 카드 테두리 재렌더링
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

        var ct = _indexCts.Token;   // 종료 시 CancellationTokenSource 에 직접 접근하지 않도록 미리 캡처
        _ = Task.Run(async () =>
        {
            try
            {
                await _indexer.UpdateIndexAsync(ct);
            }
            catch (OperationCanceledException) { /* 폼 종료 시 정상 취소 — 무시 */ }
            catch
            {
                // 폼이 살아있을 때만 UI 업데이트
                if (!IsDisposed && IsHandleCreated)
                {
                    try { Invoke(() => { _isIndexing = false; sslIndexStatus.Text = string.Empty; }); }
                    catch { _isIndexing = false; }
                }
                else
                {
                    _isIndexing = false;
                }
            }
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
        // ThreadException 핸들러가 MessageBox 를 닫으면 WM_CLOSE 가 재전송될 수 있어
        // OnFormClosed 가 두 번 이상 호출되는 경우를 방지한다.
        if (_isClosing) { base.OnFormClosed(e); return; }
        _isClosing = true;

        // ① 인덱서 백그라운드 작업 먼저 취소 (DB Dispose 전에 실행 중인 Task 정지)
        // 이미 Dispose 된 경우를 대비해 try-catch 로 보호
        try { _indexCts.Cancel(); } catch (ObjectDisposedException) { }

        try { _debounceTimer.Stop(); _debounceTimer.Dispose(); } catch { }
        try { _autoIndexTimer?.Stop(); _autoIndexTimer?.Dispose(); } catch { }
        try { _hintHideTimer?.Stop(); _hintHideTimer?.Dispose(); _hintHideTimer = null; } catch { }
        try { _hintPopup?.Close(); _hintPopup?.Dispose(); _hintPopup = null; } catch { }
        // _snippetCts 는 Dispose 없이 Cancel 만 — 실행 중인 Task 가 token 을 참조 중일 수 있음
        _snippetCts?.Cancel();
        _snippetCts = null;

        // ② 파일 감시 중지 → 그 다음 DB 정리
        try { _watcher.Dispose(); } catch { }
        try { _indexCts.Dispose(); } catch (ObjectDisposedException) { }
        try { _db.Dispose(); } catch { }

        // ③ 창 위치·크기 저장
        _settings.WindowMaximized = (WindowState == FormWindowState.Maximized);
        if (WindowState == FormWindowState.Normal)
        { _settings.WindowX = Location.X; _settings.WindowY = Location.Y; _settings.WindowWidth = Size.Width; _settings.WindowHeight = Size.Height; }
        else
        { var rb = RestoreBounds; _settings.WindowX = rb.X; _settings.WindowY = rb.Y; _settings.WindowWidth = rb.Width; _settings.WindowHeight = rb.Height; }
        _settings.Save();
        base.OnFormClosed(e);
    }
}
