using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace WallpaperEngine.App;

/// <summary>
/// 使用说明独立页面：全屏深色，纵向滚动。
/// 返回通道：右上角关闭按钮 / 右滑手势（露出下层主界面）/ 系统返回键。
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

        // 标题行：标题 + 右侧关闭按钮
        var titleRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        titleRow.SetGravity(GravityFlags.CenterVertical);

        var title = new TextView(this) { Text = "使用说明", TextSize = 24f };
        title.SetTextColor(Ui.TextPrimary);
        title.Typeface = Typeface.Create(Typeface.DefaultBold, TypefaceStyle.Bold);
        title.LayoutParameters = new LinearLayout.LayoutParams(0, -2, 1f);
        titleRow.AddView(title);

        var closeBtn = new TextView(this) { Text = "✕", TextSize = 16f, Gravity = GravityFlags.Center };
        closeBtn.SetTextColor(Ui.TextSecondary);
        closeBtn.SetBackgroundDrawable(Ui.RoundBg(Ui.Card, this, 12, Ui.CardStroke, 1));
        closeBtn.LayoutParameters = new LinearLayout.LayoutParams(Ui.Dp(this, 42), Ui.Dp(this, 42));
        closeBtn.Click += (_, _) => Finish();
        titleRow.AddView(closeBtn);

        _content.AddView(titleRow);
        _content.AddView(Spacer(8));

        var subtitle = Ui.Caption(this, "视频与图片 · 桌面与锁屏分别设置");
        _content.AddView(subtitle);
        _content.AddView(Spacer(22));

        AddSection("基本用法",
            "主界面的两张卡片分别对应桌面和锁屏。「选择桌面壁纸」「选择锁屏壁纸」从手机中挑视频或图片，选完立即生效：视频动态循环播放，图片为静态画面。");
        AddSection("系统确认页",
            "壁纸尚未激活（或所在位置是静态画面）时，应用会先打开系统确认页，在页面中确认即完成绑定。确认时可选择只设桌面、只设锁屏，或两者都设。");
        AddSection("壁纸库",
            "右上角 ☰ 打开。收藏与历史各容纳 25 个，标题标注当前数量。点击缩略图后选「设置为」：桌面壁纸、锁屏壁纸或两者。长按缩略图可设默认、收藏或删除；带 ★ 的是收藏。");
        AddSection("桌面默认与锁屏默认",
            "两个位置各有独立的默认壁纸。长按缩略图设为「桌面默认」「锁屏默认」，「恢复桌面默认」「恢复锁屏默认」随时回到各自默认。没有设置默认时，恢复会把该位置还原成系统壁纸。");
        AddSection("播放设置",
            "卡片顶部的编辑对象开关决定当前设置的是桌面还是锁屏，音量、画面比例、循环区间分别保存、互不影响。某位置显示静态画面时，播放设置不适用。");
        AddSection("耗电与暂停",
            "熄屏、锁屏、被其他应用完全覆盖时自动暂停，回到桌面自动恢复。视频使用硬件解码，常驻内存约 78MB。");

        // 挂载链：内容 → 滚动容器 → 根（曾遗漏导致页面空白，勿再删）
        scroll.AddView(_content);
        root.AddView(scroll);

        // 右滑返回：整页跟手平移，松手超阈值淡出退出（半透明窗口下露出下层主界面）
        scroll.SetOnTouchListener(new BackSwipeListener(root, () =>
        {
            root.Animate()
                .Alpha(0.2f)
                .SetDuration(120)
                .WithEndAction(new Java.Lang.Runnable(Finish))
                .Start();
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
