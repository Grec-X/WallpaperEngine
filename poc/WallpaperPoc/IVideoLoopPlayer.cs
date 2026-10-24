using Android.Content;
using Android.Views;

namespace WallpaperPoc;

/// <summary>
/// 播放内核抽象：PoC 与正式工程共用同一抽象，验证方案 3.1 节的隔离设计。
/// 实现类必须保证：Surface 在 Destroy 后不得再持有引用（壁纸 Engine 的硬性约束）。
/// </summary>
public interface IVideoLoopPlayer : IDisposable
{
    string Name { get; }

    /// <summary>true = 播放器原生循环；false = 播完手动重启（用于对比接缝）</summary>
    bool NativeLoop { get; set; }

    bool IsPlaying { get; }
    long PositionMs { get; }
    long DurationMs { get; }

    /// <summary>绑定渲染目标；传 null 立即解除引用（必须在 SurfaceDestroyed 返回前调用）</summary>
    void SetSurface(Surface? surface);

    void SetDataSource(Context context, Android.Net.Uri uri);

    /// <summary>停止并清空数据源，保留实例与 Surface 绑定</summary>
    void Reset();

    /// <summary>异步准备；就绪后触发 Prepared 事件</summary>
    void Prepare();

    void Play();
    void Pause();
    void Release();

    event EventHandler? Prepared;

    /// <summary>手动循环模式下每次重启触发，参数为重启控制耗时（毫秒）</summary>
    event EventHandler<long>? ManualRestart;

    event EventHandler<string>? ErrorOccurred;
}
