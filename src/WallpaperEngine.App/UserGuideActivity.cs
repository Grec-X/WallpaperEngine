using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace WallpaperEngine.App;

/// <summary>
/// 使用说明独立页面：全屏深色，纵向滚动。
/// 返回通道：右上角返回按钮 / 右滑手势（露出下层主界面）/ 系统返回键。
/// 窗口为半透明主题：滑动平移时露出的是主界面而非窗口底色。
/// </summary>
[Activity(Label = "使用说明", Theme = "@style/TransparentPageTheme")]
public class UserGuideActivity : Activity
{
    LinearLayout _content = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var root = new FrameLayout(this);
        root.SetBackgroundColor(new Color(14, 15, 19));
        root.SetOnApplyWindowInsetsListener(new SystemBarsInsetListener());

        var scroll = new ScrollView(this);
        _content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _content.SetPadding(Ui.Dp(this, 24), Ui.Dp(this, 28), Ui.Dp(this, 24), Ui.Dp(this, 32));

        // 标题行（页面标题 + 右侧返回按钮）
        var titleRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        titleRow.SetGravity(GravityFlags.CenterVertical);

        var title = new TextView(this) { Text = "使用说明", TextSize = 22f };
        title.SetTextColor(Ui.TextPrimary);
        title.Typeface = Typeface.Create(Typeface.DefaultBold, TypefaceStyle.Bold);
        title.LayoutParameters = new LinearLayout.LayoutParams(0, -2, 1f);
        titleRow.AddView(title);

        var backBtn = new TextView(this) { Text = "✕", TextSize = 16f, Gravity = GravityFlags.Center };
        backBtn.SetTextColor(Ui.TextSecondary);
        backBtn.SetBackgroundDrawable(Ui.RoundBg(Ui.Card, this, 12, Ui.CardStroke, 1));
        backBtn.LayoutParameters = new LinearLayout.LayoutParams(Ui.Dp(this, 42), Ui.Dp(this, 42));
        backBtn.Click += (_, _) => Finish();
        titleRow.AddView(backBtn);
        _content.AddView(titleRow);
        _content.AddView(Spacer(8));

        var subtitle = Ui.Caption(this, "视频动态壁纸 · 无缝循环播放");
        _content.AddView(subtitle);
        _content.AddView(Spacer(22));

        AddSection("快速开始",
            "1. 点主界面上方的横幅 → 在系统页面中确认设为壁纸（可选桌面/锁屏/两者）；\n" +
            "2. 点「选择壁纸」挑选本地视频，选完即自动应用；\n" +
            "3. 回到桌面即可看到视频壁纸循环播放。");
        AddSection("壁纸库（右上角 ☰）",
            "· 收藏：长按任意壁纸 →「收藏」，精选视频常驻收藏区，不会被自动清理；\n" +
            "· 历史：最近使用过的壁纸自动记录（最多 10 个，内容相同不重复）；\n" +
            "· 切换：点击任意缩略图即可切换壁纸；\n" +
            "· 更多操作：长按缩略图 → 设为默认壁纸 / 收藏 / 删除。");
        AddSection("默认壁纸",
            "长按历史壁纸设为「默认」后，「恢复默认」一键回到它；\n" +
            "未设置默认时，点「恢复默认」会还原系统原生壁纸（引擎退出）。");
        AddSection("播放设置",
            "· 音量：壁纸默认静音，可自行调整；\n" +
            "· 保持比例裁剪铺满：关闭后画面拉伸变形（一般保持开启）；\n" +
            "· 循环区间：裁掉片头片尾，只循环想要的片段。");
        AddSection("省电说明",
            "灭屏、锁屏、被其他应用完全遮挡时壁纸自动暂停，回到桌面自动恢复；\n" +
            "视频解码使用硬件加速，壁纸常驻内存占用实测约 78MB。");

        scroll.AddView(_content);
        root.AddView(scroll, new FrameLayout.LayoutParams(-1, -1));

        // 右滑退出：滑动时页面平移 + 透明度渐暗（露出下层主界面，窗口本身半透明）
        scroll.SetOnTouchListener(new BackSwipeListener(root, () =>
        {
            root.Animate().Alpha(0.25f).SetDuration(120).WithEndAction(new Java.Lang.Runnable(Finish)).Start();
        }));

        SetContentView(root);
    }

    void AddSection(string heading, string body)
    {
        var h = new TextView(this) { Text = heading, TextSize = 15.5f };
        h.SetTextColor(Ui.Accent);
        h.Typeface = Typeface.Create(Typeface.DefaultBold, TypefaceStyle.Bold);
        _content.AddView(h);

        var b = new TextView(this) { Text = body, TextSize = 13.5f };
        b.SetTextColor(Ui.TextSecondary);
        b.SetPadding(0, Ui.Dp(this, 6), 0, 0);
        b.SetLineSpacing(0, 1.15f);
        _content.AddView(b);
        _content.AddView(Spacer(20));
    }

    View Spacer(int dp)
    {
        var v = new View(this);
        v.LayoutParameters = new LinearLayout.LayoutParams(1, Ui.Dp(this, dp));
        return v;
    }
}
