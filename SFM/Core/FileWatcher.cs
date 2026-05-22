using SFM.Models;

namespace SFM.Core;

/// <summary>
/// System.IO.FileSystemWatcher 기반 파일 변경 감시기.
/// 등록된 경로에서 파일 생성·수정·삭제·이름 변경을 감지하여
/// 300 ms 디바운싱 후 Indexer 에 직접 전달한다.
/// </summary>
public sealed class FileWatcher : IDisposable
{
    // 디바운스 지연 시간 (ms)
    private const int DebounceMs = 300;

    private readonly Indexer     _indexer;
    private readonly AppSettings _settings;

    // 감시 경로 → FileSystemWatcher
    private readonly Dictionary<string, FileSystemWatcher> _watchers
        = new(StringComparer.OrdinalIgnoreCase);

    // 파일 경로 → 현재 대기 중인 CancellationTokenSource
    private readonly Dictionary<string, CancellationTokenSource> _debounce
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lock    = new();
    private          bool   _disposed;

    /// <summary>
    /// FileSystemWatcher 가 마지막 리셋 이후 파일 변경(생성·수정·삭제·이름변경)을
    /// 하나라도 감지했으면 true. 타이머 기반 증분 인덱싱의 실행 조건으로 사용한다.
    /// </summary>
    public bool HasPendingChanges { get; private set; }

    /// <summary>증분 인덱싱 완료 후 플래그를 초기화한다.</summary>
    public void ResetPendingChanges() => HasPendingChanges = false;

    public FileWatcher(Indexer indexer, AppSettings settings)
    {
        _indexer  = indexer;
        _settings = settings;
    }

    // ─────────────────────────────────────────────────────────
    // 공개 API
    // ─────────────────────────────────────────────────────────

    /// <summary>지정한 경로 배열을 한 번에 감시 시작한다.</summary>
    public void StartWatching(string[] paths)
    {
        foreach (var p in paths)
            AddPath(p);
    }

    /// <summary>모든 감시자를 중지하고 보류 중인 디바운스 작업을 취소한다.</summary>
    public void StopWatching()
    {
        lock (_lock)
        {
            foreach (var w in _watchers.Values)
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            _watchers.Clear();

            foreach (var cts in _debounce.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _debounce.Clear();
        }
    }

    /// <summary>
    /// 단일 경로를 감시 목록에 추가한다.
    /// 존재하지 않거나 이미 감시 중인 경로는 무시한다.
    /// </summary>
    public void AddPath(string path)
    {
        if (!Directory.Exists(path)) return;

        lock (_lock)
        {
            if (_watchers.ContainsKey(path)) return;

            try
            {
                _watchers[path] = CreateWatcher(path);
            }
            catch (Exception)
            {
                // 접근 불가 경로, 지원되지 않는 드라이브 등 → 조용히 스킵
            }
        }
    }

    /// <summary>단일 경로를 감시 목록에서 제거하고 Watcher 를 해제한다.</summary>
    public void RemovePath(string path)
    {
        lock (_lock)
        {
            if (!_watchers.TryGetValue(path, out var w)) return;
            w.EnableRaisingEvents = false;
            w.Dispose();
            _watchers.Remove(path);
        }
    }

    // ─────────────────────────────────────────────────────────
    // FileSystemWatcher 생성
    // ─────────────────────────────────────────────────────────

    private FileSystemWatcher CreateWatcher(string path)
    {
        var w = new FileSystemWatcher(path)
        {
            NotifyFilter          = NotifyFilters.FileName   // 생성·삭제·이름 변경
                                  | NotifyFilters.LastWrite,  // 내용 수정
            IncludeSubdirectories = true,
            InternalBufferSize    = 65_536,  // 64 KB (기본 4 KB × 16)
            EnableRaisingEvents   = true,
        };

        w.Created += OnCreated;
        w.Changed += OnChanged;
        w.Deleted += OnDeleted;
        w.Renamed += OnRenamed;
        w.Error   += OnError;

        return w;
    }

    // ─────────────────────────────────────────────────────────
    // OS 이벤트 핸들러
    // ─────────────────────────────────────────────────────────

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        if (_disposed || IsDirectory(e.FullPath)) return;
        HasPendingChanges = true;
        Debounce(e.FullPath,
            () => _indexer.IndexSingleFileAsync(e.FullPath));
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed || IsDirectory(e.FullPath)) return;
        HasPendingChanges = true;
        Debounce(e.FullPath,
            () => _indexer.IndexSingleFileAsync(e.FullPath));
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        // 삭제 시점엔 경로가 사라졌으므로 IsDirectory 검사 생략
        if (_disposed) return;
        HasPendingChanges = true;
        Debounce(e.FullPath, () =>
        {
            _indexer.RemoveFile(e.FullPath);
            return Task.CompletedTask;
        });
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (_disposed) return;
        HasPendingChanges = true;
        Debounce(e.FullPath, async () =>
        {
            // 이전 경로 제거 후 새 경로 인덱싱 (디렉터리 이름 변경이면 인덱싱 스킵)
            _indexer.RemoveFile(e.OldFullPath);
            if (!IsDirectory(e.FullPath))
                await _indexer.IndexSingleFileAsync(e.FullPath);
        });
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        if (_disposed) return;

        var ex = e.GetException();

        if (ex is InternalBufferOverflowException)
        {
            // 이벤트 버퍼 초과: 누락된 변경 사항을 전체 재동기화로 복구
            _ = TriggerFullResyncAsync();
            return;
        }

        // 네트워크 드라이브 연결 끊김 등 OS 수준 IO 오류 →
        // 해당 감시자만 비활성화(재연결은 앱 재시작 또는 AddPath 재호출로 처리)
        if (sender is FileSystemWatcher fsw)
        {
            try { fsw.EnableRaisingEvents = false; }
            catch { /* ignore */ }
        }
    }

    private async Task TriggerFullResyncAsync()
    {
        try
        {
            HasPendingChanges = true;   // 버퍼 오버플로 → 전체 재동기화 필요
            await _indexer.UpdateIndexAsync();
        }
        catch
        {
            // 재동기화 실패 → 다음 수동 인덱싱 또는 앱 재시작 시 복구
        }
    }

    // ─────────────────────────────────────────────────────────
    // 디바운싱 — 300 ms 이내 동일 경로 이벤트는 마지막 1건만 실행
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// <paramref name="key"/> 에 대한 이전 대기 작업을 취소하고,
    /// <see cref="DebounceMs"/> ms 뒤에 <paramref name="action"/> 을 실행하도록 예약한다.
    /// </summary>
    private void Debounce(string key, Func<Task> action)
    {
        if (_disposed) return;

        CancellationTokenSource? oldCts;
        CancellationTokenSource  newCts;

        lock (_lock)
        {
            _debounce.TryGetValue(key, out oldCts);
            newCts         = new CancellationTokenSource();
            _debounce[key] = newCts;
        }

        // 락 바깥에서 취소하여 잠금 경합 최소화
        oldCts?.Cancel();
        oldCts?.Dispose();

        _ = RunAfterDelayAsync(key, action, newCts);
    }

    private async Task RunAfterDelayAsync(
        string key, Func<Task> action, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(DebounceMs, cts.Token);

            // 지연 완료 후 이 CTS 가 여전히 최신인지 재확인
            // (300 ms 안에 동일 경로 이벤트가 또 발생했으면 포기)
            lock (_lock)
            {
                if (!_debounce.TryGetValue(key, out var current) ||
                    !ReferenceEquals(current, cts))
                    return;

                _debounce.Remove(key);
            }

            await action();
        }
        catch (OperationCanceledException)
        {
            // 정상 취소: 300 ms 이내 동일 경로 이벤트 재발생
        }
        catch (IOException)
        {
            // 파일 잠금·네트워크 끊김 → 다음 이벤트 때 재시도
        }
        catch (UnauthorizedAccessException)
        {
            // 권한 없는 파일 → 스킵
        }
        catch
        {
            // 그 외 예상치 못한 예외: 감시기 안정성을 위해 삼킴
        }
        finally
        {
            cts.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────
    // 헬퍼
    // ─────────────────────────────────────────────────────────

    /// <summary>경로가 실제로 존재하는 디렉터리이면 true.</summary>
    private static bool IsDirectory(string path) => Directory.Exists(path);

    // ─────────────────────────────────────────────────────────
    // IDisposable
    // ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWatching();
        GC.SuppressFinalize(this);
    }
}
