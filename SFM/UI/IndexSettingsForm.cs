using SFM.Core;
using SFM.Data;
using SFM.Models;

namespace SFM.UI;

/// <summary>
/// 인덱싱 경로 / 검색 옵션 / 인덱싱 실행을 통합 관리하는 설정 다이얼로그.
/// </summary>
public partial class IndexSettingsForm : Form
{
    // ─────────────────────────────────────────────────────────
    // 의존성
    // ─────────────────────────────────────────────────────────
    private readonly Indexer         _indexer;
    private readonly DatabaseManager _db;
    private readonly AppSettings     _settings;

    // ─────────────────────────────────────────────────────────
    // 상태
    // ─────────────────────────────────────────────────────────

    /// <summary>진행 중인 인덱싱 작업 취소 토큰.</summary>
    private CancellationTokenSource? _cts;

    /// <summary>폼을 열 때 EnableContentIndexing 원본값 — 변경 여부 추적에 사용.</summary>
    private bool _originalContentIndexing;

    // ─────────────────────────────────────────────────────────
    // 생성자
    // ─────────────────────────────────────────────────────────
    public IndexSettingsForm(Indexer indexer, DatabaseManager db, AppSettings settings)
    {
        InitializeComponent();

        _indexer  = indexer;
        _db       = db;
        _settings = settings;
    }

    // ─────────────────────────────────────────────────────────
    // 폼 로드
    // ─────────────────────────────────────────────────────────
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        LoadSettings();
        WireEvents();
        RefreshContentStats();
        RefreshIndexStats();
    }

    /// <summary>AppSettings 값을 모든 컨트롤에 채운다.</summary>
    private void LoadSettings()
    {
        // Tab 1: 경로 목록
        lstPaths.Items.Clear();
        foreach (var p in _settings.IndexRootPaths)
            lstPaths.Items.Add(p);

        // Tab 2: 내용 인덱싱
        _originalContentIndexing      = _settings.EnableContentIndexing;
        chkEnableContent.Checked      = _settings.EnableContentIndexing;
        nudMaxResults.Value           = Math.Clamp(_settings.MaxSearchResults, 50, 1000);
        chkShowSnippet.Checked        = _settings.ShowSnippetInResult;

        UpdateContentIndexingControls();
    }

    // ─────────────────────────────────────────────────────────
    // 이벤트 연결
    // ─────────────────────────────────────────────────────────
    private void WireEvents()
    {
        // ── Tab 1 ────────────────────────────────────────────
        btnAddPath.Click    += OnAddPath;
        btnRemovePath.Click += OnRemovePath;
        lstPaths.KeyDown    += (_, e) => { if (e.KeyCode == Keys.Delete) OnRemovePath(null, e); };

        // ── Tab 2 ────────────────────────────────────────────
        chkEnableContent.CheckedChanged += OnContentIndexingChanged;
        btnRunContent.Click             += OnRunContentIndexAsync;
        btnClearContent.Click           += OnClearContentIndex;

        // ── Tab 3 ────────────────────────────────────────────
        btnStartIndex.Click  += OnStartIndexAsync;
        btnCancelIndex.Click += OnCancelIndex;
        btnResetAll.Click    += OnResetAll;

        // ── 저장 ─────────────────────────────────────────────
        btnSave.Click += OnSave;

        // ── Indexer 이벤트 (백그라운드 → Invoke) ─────────────
        _indexer.IndexProgressChanged += OnIndexProgress;
        _indexer.IndexCompleted       += OnIndexCompleted;
        _indexer.IndexErrorOccurred   += OnIndexError;
    }

    // ─────────────────────────────────────────────────────────
    // Tab 1: 인덱싱 경로
    // ─────────────────────────────────────────────────────────
    private void OnAddPath(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description            = "인덱싱할 폴더를 선택하세요",
            UseDescriptionForTitle = true,
            ShowNewFolderButton    = false,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var path = dlg.SelectedPath;
        if (!lstPaths.Items.Contains(path))
            lstPaths.Items.Add(path);
    }

    private void OnRemovePath(object? sender, EventArgs e)
    {
        if (lstPaths.SelectedIndex < 0) return;
        lstPaths.Items.RemoveAt(lstPaths.SelectedIndex);
    }

    // ─────────────────────────────────────────────────────────
    // Tab 2: 검색 옵션
    // ─────────────────────────────────────────────────────────
    private void OnContentIndexingChanged(object? sender, EventArgs e)
    {
        bool changed = chkEnableContent.Checked != _originalContentIndexing;
        lblReindexWarning.Visible = changed;
        UpdateContentIndexingControls();
    }

    /// <summary>내용 인덱싱 체크박스 상태에 따라 관련 버튼 활성화 여부를 조정한다.</summary>
    private void UpdateContentIndexingControls()
    {
        bool enabled        = chkEnableContent.Checked;
        btnRunContent.Enabled   = enabled;
        // 내용 인덱스 초기화는 내용 인덱싱이 꺼져 있을 때도 실행 가능 (용량 회수)
        btnClearContent.Enabled = true;
    }

    /// <summary>내용 인덱싱 현황 (총 파일 수 / 내용 인덱싱 완료 수 / 비율)을 갱신한다.</summary>
    private void RefreshContentStats()
    {
        try
        {
            var stats = _db.GetIndexStats();
            int total   = stats.TotalFiles;
            int content = stats.ContentIndexedFiles;
            int pct     = total > 0 ? (int)(content * 100L / total) : 0;

            lblTotalFiles.Text     = $"전체 파일: {total:N0}개";
            lblContentIndexed.Text = $"내용 인덱싱 완료: {content:N0}개 ({pct}%)";
            pbContentRatio.Value   = pct;
        }
        catch
        {
            lblTotalFiles.Text     = "전체 파일: —";
            lblContentIndexed.Text = "내용 인덱싱 완료: —";
        }
    }

    private async void OnRunContentIndexAsync(object? sender, EventArgs e)
    {
        if (_settings.IndexRootPaths.Length == 0)
        {
            MessageBox.Show("먼저 인덱싱 경로를 설정하고 저장하세요.",
                "SFM", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetRunningState(running: true);

        _cts = new CancellationTokenSource();
        tabCtrl.SelectedTab = tabRun;   // 진행 상황을 Tab 3 에서 확인 가능

        try
        {
            await _indexer.BuildMissingContentAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            SetPhaseLabel("취소됨");
        }
        finally
        {
            SetRunningState(running: false);
            RefreshContentStats();
            RefreshIndexStats();
        }
    }

    private void OnClearContentIndex(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "파일 목록(메타)은 유지하고 내용 인덱스만 삭제합니다.\n" +
            "내용 검색이 비활성화되며, 재인덱싱으로 복원 가능합니다.\n\n" +
            "계속하시겠습니까?",
            "내용 인덱스 초기화",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (result != DialogResult.Yes) return;

        try
        {
            _db.ClearContentIndex();
            _db.Vacuum();
            MessageBox.Show("내용 인덱스가 초기화되었습니다.",
                "SFM", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"초기화 중 오류가 발생했습니다.\n{ex.Message}",
                "SFM", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            RefreshContentStats();
            RefreshIndexStats();
        }
    }

    // ─────────────────────────────────────────────────────────
    // Tab 3: 인덱싱 실행
    // ─────────────────────────────────────────────────────────
    private async void OnStartIndexAsync(object? sender, EventArgs e)
    {
        var paths = GetCurrentPaths();
        if (paths.Length == 0)
        {
            MessageBox.Show("인덱싱할 경로가 없습니다.\n먼저 '인덱싱 경로' 탭에서 폴더를 추가하세요.",
                "SFM", MessageBoxButtons.OK, MessageBoxIcon.Information);
            tabCtrl.SelectedTab = tabPaths;
            return;
        }

        SetRunningState(running: true);
        SetPhaseLabel("인덱싱 준비 중…");
        pbIndexProgress.Value = 0;

        _cts = new CancellationTokenSource();

        try
        {
            await _indexer.BuildIndexAsync(paths, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            SetPhaseLabel("취소됨");
        }
        finally
        {
            SetRunningState(running: false);
            RefreshContentStats();
            RefreshIndexStats();
        }
    }

    private void OnCancelIndex(object? sender, EventArgs e)
    {
        _cts?.Cancel();
        btnCancelIndex.Enabled = false;
        SetPhaseLabel("취소 중…");
    }

    private void OnResetAll(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "파일 목록, 내용 인덱스, 검색 히스토리를 모두 삭제합니다.\n" +
            "이 작업은 되돌릴 수 없습니다.\n\n" +
            "계속하시겠습니까?",
            "인덱스 전체 초기화",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);  // 기본값: 아니오

        if (result != DialogResult.Yes) return;

        try
        {
            _db.ClearAllIndexes();
            _db.Vacuum();
            SetPhaseLabel("인덱스가 초기화되었습니다. 인덱싱을 다시 실행하세요.");
            pbIndexProgress.Value = 0;
            lblCurrentFile.Text   = string.Empty;
            MessageBox.Show("인덱스가 초기화되었습니다.",
                "SFM", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"초기화 중 오류가 발생했습니다.\n{ex.Message}",
                "SFM", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            RefreshContentStats();
            RefreshIndexStats();
        }
    }

    /// <summary>인덱스 통계 레이블을 DB 에서 읽어 갱신한다.</summary>
    private void RefreshIndexStats()
    {
        try
        {
            var stats = _db.GetIndexStats();
            lblStatTotal.Text       = $"총 파일 수:           {stats.TotalFiles:N0}개";
            lblStatContent.Text     = $"내용 인덱싱 파일 수:  {stats.ContentIndexedFiles:N0}개";
            lblStatDbSize.Text      = $"DB 크기:              {stats.DatabaseSizeMb:F2} MB";
            lblStatLastUpdate.Text  = $"마지막 업데이트:      {DateTime.Now:yyyy-MM-dd HH:mm}";
        }
        catch
        {
            lblStatTotal.Text      = "총 파일 수:           —";
            lblStatContent.Text    = "내용 인덱싱 파일 수:  —";
            lblStatDbSize.Text     = "DB 크기:              —";
            lblStatLastUpdate.Text = "마지막 업데이트:      —";
        }
    }

    // ─────────────────────────────────────────────────────────
    // Indexer 이벤트 (백그라운드 → UI 스레드)
    // ─────────────────────────────────────────────────────────
    private void OnIndexProgress(Indexer.IndexProgress p)
    {
        if (!IsHandleCreated || IsDisposed) return;
        try
        {
            Invoke(() =>
            {
                int pct = p.Total > 0 ? (int)(p.Processed * 100L / p.Total) : 0;
                pbIndexProgress.Value = Math.Clamp(pct, 0, 100);
                SetPhaseLabel($"[{p.Phase}] {p.Processed:N0} / {p.Total:N0}");
                lblCurrentFile.Text = p.FilePath;
            });
        }
        catch (ObjectDisposedException) { }
    }

    private void OnIndexCompleted(Indexer.IndexResult r)
    {
        if (!IsHandleCreated || IsDisposed) return;
        try
        {
            Invoke(() =>
            {
                pbIndexProgress.Value = 100;
                SetPhaseLabel(
                    $"완료: {r.TotalIndexed:N0}개  " +
                    $"+{r.Added} ~{r.Updated} -{r.Deleted}  " +
                    $"{r.Elapsed.TotalSeconds:F1}s");
                lblCurrentFile.Text = string.Empty;
                RefreshContentStats();
                RefreshIndexStats();
            });
        }
        catch (ObjectDisposedException) { }
    }

    private void OnIndexError(Indexer.IndexError err)
    {
        // 개별 파일 오류는 로그 수준 처리 (Dialog 팝업 금지)
        System.Diagnostics.Debug.WriteLine($"[IndexError] {err.FilePath}: {err.Message}");
    }

    // ─────────────────────────────────────────────────────────
    // 저장
    // ─────────────────────────────────────────────────────────
    private void OnSave(object? sender, EventArgs e)
    {
        // Tab 1: 경로
        _settings.IndexRootPaths = GetCurrentPaths();

        // Tab 2: 검색 옵션
        // EnableContentIndexing: 인덱싱 시 내용을 파싱·저장할지 여부
        // EnableContentSearch:   검색 쿼리에 FTS5 내용 검색을 포함할지 여부
        // → 두 값을 같은 체크박스로 함께 제어한다.
        _settings.EnableContentIndexing = chkEnableContent.Checked;
        _settings.EnableContentSearch   = chkEnableContent.Checked;
        _settings.MaxSearchResults      = (int)nudMaxResults.Value;
        _settings.ShowSnippetInResult   = chkShowSnippet.Checked;

        try
        {
            _settings.Save();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"설정 저장 중 오류가 발생했습니다.\n{ex.Message}",
                "SFM", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ─────────────────────────────────────────────────────────
    // 헬퍼
    // ─────────────────────────────────────────────────────────

    /// <summary>lstPaths 에서 현재 경로 배열을 반환한다.</summary>
    private string[] GetCurrentPaths() =>
        lstPaths.Items.Cast<string>().ToArray();

    /// <summary>인덱싱 진행 중 버튼 활성화 상태를 전환한다.</summary>
    private void SetRunningState(bool running)
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (InvokeRequired) { Invoke(() => SetRunningState(running)); return; }

        btnStartIndex.Enabled  = !running;
        btnCancelIndex.Enabled =  running;
        btnRunContent.Enabled  = !running && chkEnableContent.Checked;
        btnResetAll.Enabled    = !running;
        btnSave.Enabled        = !running;
    }

    private void SetPhaseLabel(string text)
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (InvokeRequired) { Invoke(() => SetPhaseLabel(text)); return; }
        lblIndexPhase.Text = text;
    }

    // ─────────────────────────────────────────────────────────
    // 폼 종료 — Indexer 이벤트 구독 해제
    // ─────────────────────────────────────────────────────────
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 인덱싱 진행 중이면 취소 확인
        if (_cts is { IsCancellationRequested: false } && btnCancelIndex.Enabled)
        {
            var r = MessageBox.Show(
                "인덱싱이 진행 중입니다. 취소하고 닫으시겠습니까?",
                "SFM", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.No)
            {
                e.Cancel = true;
                return;
            }
            _cts.Cancel();
        }

        // 이벤트 구독 해제 (MainForm 의 Indexer 이벤트와 중복 방지)
        _indexer.IndexProgressChanged -= OnIndexProgress;
        _indexer.IndexCompleted       -= OnIndexCompleted;
        _indexer.IndexErrorOccurred   -= OnIndexError;

        base.OnFormClosing(e);
    }
}
