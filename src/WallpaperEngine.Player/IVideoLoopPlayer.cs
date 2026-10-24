using Android.Content;
using Android.Views;

namespace WallpaperEngine.Player;

/// <summary>
/// 播放内核抽象（方案 3.1）：壁纸服务与管理界面共用。
/// 实现约束：Surface 在 Destroy 后不得再持有引用（壁纸 Engine 的硬性要求）。
/// </summary>
public interface IVideoLoopPlayer : IDisposable
{
    string Name { get; }

    /// <summary>true = 播放器原生循环（V1 默认）；false = 播完手动重启</summary>
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

    /// <summary>音量 0~1（壁纸场景通常为 0 静音）</summary>
    void SetVolume(float volume);

    /// <summary>true=保持宽高比裁剪铺满（Cover，V1 默认）；false=拉伸填满（变形）</summary>
    void SetScaleMode(bool cover);

    /// <summary>循环区间起点（毫秒），下次 SetDataSource 生效</summary>
    long TrimStartMs { get; set; }

    /// <summary>循环区间终点（毫秒），0=到片尾；下次 SetDataSource 生效</summary>
    long TrimEndMs { get; set; }

    event EventHandler? Prepared;

    /// <summary>手动循环模式下每次重启触发，参数为重启控制耗时（毫秒）</summary>
    event EventHandler<long>? ManualRestart;

    event EventHandler<string>? ErrorOccurred;
}
