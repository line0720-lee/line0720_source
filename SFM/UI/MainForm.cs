using System.Diagnostics;
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

    // 트리 상태
    private const string TreePlaceholder = "\0placeholder";
    private bool _treeCheckSuppressed = false;   // 프로그래밍 체크 시 이벤트 억제

    // ─────────────────────────────────────────────────────────
    // 파일유형 필터
    // ─────────────────────────────────────────────────────────
    private static readonly string[] ExtDoc   = [".docx",".doc",".xlsx",".xls",".pptx",".ppt",".txt",".md",".rtf"];
    private static readonly string[] ExtPdf   = [".pdf"];
    private static readonly string[] ExtImage = [".jpg",".jpeg",".png",".gif",".bmp",".svg",".webp",".ico"];
    private static readonly string[] ExtCode  = [".cs",".py",".js",".ts",".java",".cpp",".c",".h",
                                                  ".go",".rs",".rb",".php",".swift",".kt",
                                                  ".html",".htm",".css",".scss",".sql",".sh",".bat",".ps1"];

    private static readonly Dictionary<string, int> ExtImageIndex =
        new(StringComparer.OrdinalIgnoreCase)
        {
            {".docx",1},{".doc",1},{".xlsx",1},{".xls",1},{".pptx",1},{".ppt",1},
            {".txt",1},{".md",1},{".rtf",1},{".pdf",2},
            {".jpg",3},{".jpeg",3},{".png",3},{".gif",3},{".bmp",3},{".svg",3},{".webp",3},{".ico",3},
            {".cs",4},{".py",4},{".js",4},{".ts",4},{".java",4},{".cpp",4},{".c",4},{".h",4},
            {".go",4},{".rs",4},{".rb",4},{".php",4},{".swift",4},{".kt",4},
            {".html",4},{".htm",4},{".css",4},{".scss",4},{".sql",4},{".sh",4},{".bat",4},{".ps1",4},
        };

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
        InitFolderTree();

        // ── 이벤트 연결 전에 초기 UI 상태를 설정 (CheckedChanged 이벤트 오발 방지) ──
        chkContent.Checked         = _settings.EnableContentSearch;
        tsmiSearchContent.Checked  = _settings.EnableContentSearch;

        WireEvents();
        ApplyTheme();
        RestoreWindowBounds();
        RefreshDbStats();

        _watcher.StartWatching(_settings.IndexRootPaths);
        ActiveControl = txtSearch;

        // 폼 크기 확정 후 SplitterDistance 설정 (InitializeComponent 시점에 설정하면 크기 제약 오류)
        Load += (_, _) =>
        {
            try { splitMain.SplitterDistance = 220; }
            catch { /* 폼 너비가 너무 좁은 경우 무시 */ }
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
    // 폴더 트리 초기화
    // ─────────────────────────────────────────────────────────
    private void InitFolderTree()
    {
        _treeCheckSuppressed = true;
        tvFolders.BeginUpdate();
        tvFolders.Nodes.Clear();

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            try
            {
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.Name.TrimEnd('\\')
                    : $"{drive.Name.TrimEnd('\\')}  [{drive.VolumeLabel}]";

                var node = new TreeNode(label)
                {
                    Tag                = drive.RootDirectory.FullName,
                    ImageIndex         = 0,
                    SelectedImageIndex = 0,
                    ToolTipText        = drive.RootDirectory.FullName,
                };
                // 드라이브 내부 탐색을 위한 플레이스홀더
                node.Nodes.Add(new TreeNode(TreePlaceholder));
                tvFolders.Nodes.Add(node);
            }
            catch { /* 접근 불가 드라이브 무시 */ }
        }

        tvFolders.EndUpdate();
        _treeCheckSuppressed = false;
        UpdateFolderButtonText();
    }

    /// <summary>노드 확장 시 하위 폴더를 지연 로드한다.</summary>
    private void OnTreeBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        var node = e.Node!;
        if (node.Tag is not string path) return;
        if (node.Nodes.Count == 1 && node.Nodes[0].Text == TreePlaceholder)
        {
            node.Nodes.Clear();
            LoadSubfolders(node, path);
        }
    }

    private void LoadSubfolders(TreeNode parent, string path)
    {
        try
        {
            var dirs = Directory.GetDirectories(path)
                .Where(d => !IsHiddenOrSystem(d))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var dir in dirs)
            {
                var name  = Path.GetFileName(dir);
                var child = new TreeNode(name)
                {
                    Tag                = dir,
                    ImageIndex         = 1,
                    SelectedImageIndex = 2,
                    ToolTipText        = dir,
                };
                // 자식 폴더 유무 확인 (성능을 위해 Any() 사용)
                try
                {
                    if (Directory.EnumerateDirectories(dir).Any())
                        child.Nodes.Add(new TreeNode(TreePlaceholder));
                }
                catch { /* access denied */ }

                parent.Nodes.Add(child);
            }
        }
        catch { /* 접근 거부 폴더 무시 */ }
    }

    private static bool IsHiddenOrSystem(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            return (attrs & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
        catch { return true; }
    }

    /// <summary>체크된 폴더 경로 목록을 반환한다. 부모가 체크된 경우 자식은 수집하지 않는다.</summary>
    private List<string> GetCheckedFolders()
    {
        var paths = new List<string>();
        CollectCheckedNodes(tvFolders.Nodes, paths);
        return paths;
    }

    private static void CollectCheckedNodes(TreeNodeCollection nodes, List<string> result)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Checked && node.Tag is string path)
                result.Add(path);           // 부모 체크 → 자식 수집 불필요
            else
                CollectCheckedNodes(node.Nodes, result);
        }
    }

    private void UpdateFolderButtonText()
    {
        var count = GetCheckedFolders().Count;
        btnFolderPanel.Text = count == 0
            ? "📂 모든 위치"
            : $"📂 {count}개 선택";
    }

    // ─────────────────────────────────────────────────────────
    // 이미지 리스트 초기화 (파일 아이콘 + 트리 아이콘)
    // ─────────────────────────────────────────────────────────
    private void InitImageList()
    {
        // 파일 목록 아이콘 (imgListSmall)
        Color[] palette =
        [
            Color.FromArgb(130, 130, 130),
            Color.FromArgb( 60, 120, 200),
            Color.FromArgb(200,  60,  60),
            Color.FromArgb(140,  70, 200),
            Color.FromArgb( 50, 160,  90),
        ];
        foreach (var clr in palette)
            imgListSmall.Images.Add(MakeFileIcon(clr));

        // 트리 아이콘 (imgListTree)
        imgListTree.Images.Add(MakeDriveIcon());              // 0: 드라이브
        imgListTree.Images.Add(MakeFolderIcon(false));        // 1: 폴더 (닫힘)
        imgListTree.Images.Add(MakeFolderIcon(true));         // 2: 폴더 (열림)
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
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddLines(new PointF[] { new(2,1), new(10,1), new(13,4), new(13,14), new(2,14), new(2,1) });
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
        using var fold = new SolidBrush(Color.FromArgb(60, Color.White));
        g.FillPolygon(fold, new PointF[] { new(10,1), new(13,4), new(10,4) });
        g.DrawLine(line, 4, 7, 11, 7);
        g.DrawLine(line, 4, 9, 11, 9);
        g.DrawLine(line, 4, 11, 9, 11);
        return bmp;
    }

    private static Bitmap MakeDriveIcon()
    {
        var bmp = new Bitmap(16, 16);
        using var g    = Graphics.FromImage(bmp);
        using var fill = new SolidBrush(Color.FromArgb(90, 100, 120));
        using var pen  = new Pen(Color.FromArgb(60, 70, 90));
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        // 디스크 모양
        g.FillEllipse(fill, 1, 4, 14, 9);
        g.DrawEllipse(pen,  1, 4, 14, 9);
        g.FillEllipse(fill, 1, 2, 14, 5);
        g.DrawEllipse(pen,  1, 2, 14, 5);
        using var hl = new SolidBrush(Color.FromArgb(80, Color.White));
        g.FillEllipse(hl, 2, 2, 12, 3);
        return bmp;
    }

    private static Bitmap MakeFolderIcon(bool open)
    {
        var bmp  = new Bitmap(16, 16);
        using var g    = Graphics.FromImage(bmp);
        var body = open ? Color.FromArgb(255, 200, 40) : Color.FromArgb(255, 185, 15);
        var tab  = open ? Color.FromArgb(230, 170, 30) : Color.FromArgb(220, 160, 10);
        using var bodyBrush = new SolidBrush(body);
        using var tabBrush  = new SolidBrush(tab);
        using var pen       = new Pen(Color.FromArgb(160, 120, 0));
        g.SmoothingMode     = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        // 탭
        g.FillRectangle(tabBrush, 1, 4, 6, 3);
        // 폴더 본체
        g.FillRectangle(bodyBrush, 1, 6, 14, 8);
        g.DrawRectangle(pen, 1, 6, 13, 7);
        g.DrawRectangle(pen, 1, 4,  5, 2);
        if (open)
        {
            // 열린 폴더: 약간 그림자
            using var dark = new SolidBrush(Color.FromArgb(40, Color.Black));
            g.FillRectangle(dark, 1, 13, 14, 1);
        }
        return bmp;
    }

    private static int GetImageIndex(string? ext) =>
        !string.IsNullOrEmpty(ext) && ExtImageIndex.TryGetValue(ext, out var idx) ? idx : 0;

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
        // ── 폴더 패널 토글 ─────────────────────────────────────
        btnFolderPanel.Click += (_, _) =>
        {
            splitMain.Panel1Collapsed = !splitMain.Panel1Collapsed;
            tsmiViewFolderTree.Checked = !splitMain.Panel1Collapsed;
        };

        // ── 트리 이벤트 ─────────────────────────────────────────
        tvFolders.BeforeExpand += OnTreeBeforeExpand;
        tvFolders.AfterCheck   += OnTreeAfterCheck;

        btnClearTree.Click += (_, _) =>
        {
            _treeCheckSuppressed = true;
            ClearAllChecks(tvFolders.Nodes);
            _treeCheckSuppressed = false;
            UpdateFolderButtonText();
            if (txtSearch.Text.Trim().Length >= 2) _ = RunSearchAsync();
        };

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
            tsmiOpenFile.Enabled = tsmiOpenFolder.Enabled = tsmiMoveFile.Enabled =
                tsmiDeleteFile.Enabled = tsmiProperties.Enabled =
                tsmiCopyPath.Enabled = tsmiCopyDir.Enabled =
                tsmiCopyName.Enabled = tsmiDeleteFromIndex.Enabled = hasSel;
            tsmiSelectAll.Enabled = tsmiInvertSelect.Enabled = tsmiSaveAsText.Enabled = hasRes;
            tsmiSelectNone.Enabled = hasSel;
            if (!hasRes) e.Cancel = true;
        };
        tsmiOpenFile.Click        += (_, _) => OpenSelectedFile();
        tsmiOpenFolder.Click      += (_, _) => OpenSelectedFolder();
        tsmiMoveFile.Click        += (_, _) => MoveSelectedFiles();
        tsmiDeleteFile.Click      += (_, _) => DeleteSelectedFiles();
        tsmiProperties.Click      += (_, _) => ShowProperties();
        tsmiSelectAll.Click       += (_, _) => SelectAll();
        tsmiSelectNone.Click      += (_, _) => listResults.SelectedIndices.Clear();
        tsmiInvertSelect.Click    += (_, _) => InvertSelection();
        tsmiCopyPath.Click        += (_, _) => CopySelectedPaths();
        tsmiCopyDir.Click         += (_, _) => CopySelectedDirs();
        tsmiCopyName.Click        += (_, _) => CopySelectedNames();
        tsmiSaveAsText.Click      += (_, _) => SaveResultsAsText();
        tsmiDeleteFromIndex.Click += OnDeleteFromIndex;

        // ── 스니펫 닫기 ──────────────────────────────────────────
        btnSnippetClose.Click += (_, _) =>
        { pnlSnippet.Visible = false; tsmiViewSnippet.Checked = false; };

        // ── 메뉴 ─────────────────────────────────────────────────
        tsmiFileNewWindow.Click += (_, _) => new MainForm().Show();
        tsmiFileExit.Click      += (_, _) => Close();

        tsmiViewDark.Click += (_, _) => { _settings.IsDarkMode = tsmiViewDark.Checked; ApplyTheme(); };
        tsmiViewSnippet.Click     += (_, _) => pnlSnippet.Visible = tsmiViewSnippet.Checked;
        tsmiViewFolderTree.Click  += (_, _) =>
        {
            splitMain.Panel1Collapsed = !tsmiViewFolderTree.Checked;
        };
        tsmiViewAutoCol.Click += (_, _) => AutoResizeColumns();

        tsmiSearchContent.Click += (_, _) => chkContent.Checked = tsmiSearchContent.Checked;
        tsmiSearchClear.Click   += (_, _) => { txtSearch.Clear(); txtSearch.Focus(); };

        tsmiToolsIndex.Click   += OnSettingsClick;
        tsmiToolsReindex.Click += OnReindexClick;
        tsmiToolsContent.Click += OnBuildContentClick;
        tsmiToolsVacuum.Click  += OnVacuumClick;

        tsmiHelpAbout.Click += (_, _) => MessageBox.Show(
            "SFM — Smart File Manager v1.0\n\n로컬 파일 전문 검색 도구\n" +
            "파일명 + 내용 인덱싱 · 한국어 초성 검색 지원\n\n단축키\n" +
            "  Ctrl+F     검색창 포커스\n  Ctrl+T     폴더 트리 토글\n" +
            "  F4         내용 검색 토글\n  F5         전체 재인덱싱\n" +
            "  Enter      파일 열기 / 검색\n  Ctrl+C     경로 복사\n" +
            "  Ctrl+A     전체 선택\n  Alt+←/→   이전/다음 페이지\n  Esc        검색어 지우기",
            "SFM 정보", MessageBoxButtons.OK, MessageBoxIcon.Information);

        // ── Indexer 이벤트 ───────────────────────────────────────
        _indexer.IndexProgressChanged += p => Invoke(() =>
            sslIndexStatus.Text = $"[{p.Phase}] {p.Processed:N0}/{p.Total:N0}  {Path.GetFileName(p.FilePath)}");

        _indexer.IndexCompleted += r => Invoke(() =>
        {
            sslIndexStatus.Text = $"완료 {r.TotalIndexed:N0}개  +{r.Added} ~{r.Updated} -{r.Deleted}  {r.Elapsed.TotalSeconds:F1}s";
            sslLastIndexed.Text = $"인덱싱: {DateTime.Now:HH:mm}";
            RefreshDbStats();
            if (!string.IsNullOrWhiteSpace(txtSearch.Text)) _ = RunSearchAsync();
        });
        _indexer.IndexErrorOccurred += err => Debug.WriteLine($"[IndexError] {err.FilePath}: {err.Message}");
    }

    private static void ClearAllChecks(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            node.Checked = false;
            if (node.Nodes.Count > 0) ClearAllChecks(node.Nodes);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 트리 AfterCheck
    // ─────────────────────────────────────────────────────────
    private void OnTreeAfterCheck(object? sender, TreeViewEventArgs e)
    {
        if (_treeCheckSuppressed) return;
        if (e.Action == TreeViewAction.Unknown) return;  // 프로그래밍 조작 무시

        UpdateFolderButtonText();
        if (txtSearch.Text.Trim().Length >= 2) _ = RunSearchAsync();
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

        BackColor = bg; ForeColor = text;
        menuStrip.BackColor = Color.FromArgb(37, 37, 38); menuStrip.ForeColor = text;
        foreach (ToolStripMenuItem m in menuStrip.Items.OfType<ToolStripMenuItem>()) m.ForeColor = text;

        pnlSearch.BackColor = tblToolbar.BackColor = Color.FromArgb(37, 37, 38);
        pnlOptions.BackColor = Color.FromArgb(37, 37, 38);

        btnFolderPanel.BackColor = Color.FromArgb(55, 55, 58);
        btnFolderPanel.ForeColor = text;
        btnFolderPanel.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 75);

        txtSearch.BackColor = surf; txtSearch.ForeColor = text; txtSearch.BorderStyle = BorderStyle.FixedSingle;
        btnClear.BackColor = Color.FromArgb(37, 37, 38); btnClear.ForeColor = gray;
        btnSearch.BackColor = Color.FromArgb(0, 100, 180); btnSearch.ForeColor = Color.White;

        chkContent.ForeColor = text; lblFilter.ForeColor = gray;
        chkTypeDoc.ForeColor = chkTypePdf.ForeColor = chkTypeImage.ForeColor = chkTypeCode.ForeColor = text;
        lblDbInfo.ForeColor = gray;

        // 트리 패널
        pnlTreeHeader.BackColor = Color.FromArgb(37, 37, 38);
        lblTreeTitle.ForeColor  = text;
        btnClearTree.BackColor  = Color.FromArgb(50, 50, 55);
        btnClearTree.ForeColor  = gray;
        tvFolders.BackColor     = Color.FromArgb(30, 30, 32);
        tvFolders.ForeColor     = text;

        listResults.BackColor = Color.FromArgb(25, 25, 25); listResults.ForeColor = text;

        pnlSnippet.BackColor = Color.FromArgb(35, 35, 35);
        pnlSnippetBar.BackColor = Color.FromArgb(50, 50, 50);
        lblSnippetIcon.ForeColor = text; lblSnippetPath.ForeColor = gray;
        btnSnippetClose.BackColor = Color.FromArgb(50, 50, 50); btnSnippetClose.ForeColor = gray;
        rtbSnippet.BackColor = Color.FromArgb(25, 25, 25); rtbSnippet.ForeColor = text;

        statusStrip.BackColor = surf; statusStrip.ForeColor = text;
        foreach (ToolStripItem item in statusStrip.Items) item.ForeColor = text;
        _sslPrevPage.ForeColor = _sslNextPage.ForeColor = text;

        _recentBox.BackColor = surf; _recentBox.ForeColor = text;
    }

    private void ApplyLightTheme()
    {
        var toolbar = Color.FromArgb(240, 240, 240);
        BackColor = SystemColors.Window; ForeColor = SystemColors.WindowText;
        menuStrip.BackColor = SystemColors.MenuBar; menuStrip.ForeColor = SystemColors.MenuText;

        pnlSearch.BackColor = tblToolbar.BackColor = toolbar;
        pnlOptions.BackColor = Color.FromArgb(248, 248, 248);

        btnFolderPanel.BackColor = SystemColors.Window;
        btnFolderPanel.ForeColor = SystemColors.WindowText;
        btnFolderPanel.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);

        txtSearch.BackColor = SystemColors.Window; txtSearch.ForeColor = SystemColors.WindowText;
        txtSearch.BorderStyle = BorderStyle.FixedSingle;
        btnClear.BackColor = toolbar; btnClear.ForeColor = Color.FromArgb(100, 100, 100);
        btnSearch.BackColor = Color.FromArgb(0, 120, 212); btnSearch.ForeColor = Color.White;

        chkContent.ForeColor = SystemColors.WindowText; lblFilter.ForeColor = SystemColors.GrayText;
        chkTypeDoc.ForeColor = chkTypePdf.ForeColor = chkTypeImage.ForeColor = chkTypeCode.ForeColor = SystemColors.WindowText;
        lblDbInfo.ForeColor = SystemColors.GrayText;

        // 트리 패널
        pnlTreeHeader.BackColor = Color.FromArgb(235, 235, 235);
        lblTreeTitle.ForeColor  = SystemColors.WindowText;
        btnClearTree.BackColor  = Color.FromArgb(235, 235, 235);
        btnClearTree.ForeColor  = SystemColors.GrayText;
        tvFolders.BackColor     = SystemColors.Window;
        tvFolders.ForeColor     = SystemColors.WindowText;

        listResults.BackColor = SystemColors.Window; listResults.ForeColor = SystemColors.WindowText;

        pnlSnippet.BackColor = Color.FromArgb(252, 252, 252);
        pnlSnippetBar.BackColor = Color.FromArgb(235, 235, 235);
        lblSnippetIcon.ForeColor = SystemColors.WindowText; lblSnippetPath.ForeColor = SystemColors.GrayText;
        btnSnippetClose.BackColor = Color.FromArgb(235, 235, 235); btnSnippetClose.ForeColor = SystemColors.GrayText;
        rtbSnippet.BackColor = Color.FromArgb(252, 252, 252); rtbSnippet.ForeColor = SystemColors.WindowText;

        statusStrip.BackColor = toolbar; statusStrip.ForeColor = SystemColors.WindowText;
        foreach (ToolStripItem item in statusStrip.Items) item.ForeColor = SystemColors.WindowText;
        _sslPrevPage.ForeColor = _sslNextPage.ForeColor = SystemColors.WindowText;

        _recentBox.BackColor = SystemColors.Window; _recentBox.ForeColor = SystemColors.WindowText;
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
        var lvi = new ListViewItem(r.FileName, GetImageIndex(r.Extension));
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
        if (!pnlSnippet.Visible) return;
        UpdateSnippetPanel(GetSelectedResult());
    }

    private void UpdateSnippetPanel(SearchResult? r)
    {
        if (r is null) { lblSnippetPath.Text = "항목을 선택하면 미리보기가 표시됩니다"; rtbSnippet.Clear(); return; }
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
            rtbSnippet.Text = r.MatchedInContent ? "(내용 미리보기를 가져오는 중…)" : "(파일명으로 일치)";
        }
    }

    private void HighlightKeywordInSnippet(string keyword)
    {
        if (string.IsNullOrEmpty(keyword) || string.IsNullOrEmpty(rtbSnippet.Text)) return;
        var text = rtbSnippet.Text;
        int start = 0;
        while (true)
        {
            int idx = text.IndexOf(keyword, start, StringComparison.OrdinalIgnoreCase);
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

            // ── 폴더 트리 선택 필터 ───────────────────────────────
            var checkedFolders = GetCheckedFolders();
            if (checkedFolders.Count > 0)
            {
                results = results
                    .Where(r => checkedFolders.Any(f =>
                        r.FilePath.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            _results = results; _sortColumn = -1;
            listResults.VirtualListSize = _results.Count;
            listResults.Invalidate();

            _sslPrevPage.Enabled = _currentPage > 0;
            _sslNextPage.Enabled = _results.Count >= _settings.MaxSearchResults;

            var elapsed = sw.Elapsed.TotalSeconds;
            sslResultCount.Text = _results.Count == 0
                ? $"검색 완료.  결과 없음.  경과 시간: {elapsed:F3} 초"
                : $"검색 완료.  {_results.Count:N0} 파일/폴더 발견.  경과 시간: {elapsed:F3} 초";

            Text = _results.Count > 0 ? $"SFM — \"{keyword}\" ({_results.Count:N0}개)" : "SFM — Smart File Manager";
            if (_results.Count > 0 && pnlSnippet.Visible) UpdateSnippetPanel(_results[0]);
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
            case Keys.Escape:
                if (_recentBox.Visible) _recentBox.Visible = false; else txtSearch.Clear();
                e.Handled = true; break;
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
            case Keys.Enter when txtSearch.Focused:
                if (_recentBox.Visible) { if (_recentBox.SelectedItem is string sel) txtSearch.Text = sel; _recentBox.Visible = false; }
                _debounceTimer.Stop(); _ = RunSearchAsync(); return true;
            case Keys.Control | Keys.F:
                txtSearch.Focus(); txtSearch.SelectAll(); return true;
            case Keys.Control | Keys.T:
                splitMain.Panel1Collapsed = !splitMain.Panel1Collapsed;
                tsmiViewFolderTree.Checked = !splitMain.Panel1Collapsed;
                return true;
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

    private void SaveResultsAsText()
    {
        if (_results.Count == 0) return;
        using var dlg = new SaveFileDialog { Title = "결과를 텍스트로 저장", Filter = "텍스트 (*.txt)|*.txt|*.*|*.*", FileName = $"SFM_결과_{DateTime.Now:yyyyMMdd_HHmmss}.txt" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"SFM 검색 결과 — \"{txtSearch.Text.Trim()}\"  ({DateTime.Now:yyyy-MM-dd HH:mm:ss})");
            sb.AppendLine($"총 {_results.Count:N0}개"); sb.AppendLine(new string('─', 80));
            foreach (var r in _results) sb.AppendLine($"{r.FilePath}  [{FormatSize(r.FileSize)}  {r.ModifiedAt.ToLocalTime():yyyy-MM-dd HH:mm}]");
            File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
            sslResultCount.Text = $"저장 완료: {dlg.FileName}";
        }
        catch (Exception ex) { MessageBox.Show($"저장 실패: {ex.Message}", "SFM", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

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
    private void OnSettingsClick(object? sender, EventArgs e)
    {
        using var dlg = new IndexSettingsForm(_indexer, _db, _settings);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        chkContent.Checked = _settings.EnableContentSearch;
        _watcher.StopWatching(); _watcher.StartWatching(_settings.IndexRootPaths);
        _ = RunSearchAsync();
    }
    private void OnReindexClick(object? sender, EventArgs e)
    {
        if (MessageBox.Show("전체 재인덱싱을 시작하시겠습니까?", "SFM", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        sslIndexStatus.Text = "재인덱싱 준비 중…";
        _ = _indexer.BuildIndexAsync(_settings.IndexRootPaths);
    }
    private void OnBuildContentClick(object? sender, EventArgs e) { sslIndexStatus.Text = "내용 인덱싱 시작…"; _ = _indexer.BuildMissingContentAsync(); }
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
        listResults.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
        if (colFileName.Width < 160) colFileName.Width = 160;
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
