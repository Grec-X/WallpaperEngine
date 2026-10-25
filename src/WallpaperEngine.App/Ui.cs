using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;

namespace WallpaperEngine.App;

/// <summary>圆角轮廓提供者：配合 ClipToOutline 实现任意 View 的圆角裁剪</summary>
public class RoundOutlineProvider : ViewOutlineProvider
{
    readonly int _radiusPx;
    public RoundOutlineProvider(int radiusPx) => _radiusPx = radiusPx;
    public override void GetOutline(View? view, Outline? outline)
    {
        if (view != null && outline != null)
            outline.SetRoundRect(0, 0, view.Width, view.Height, _radiusPx);
    }
}

/// <summary>
/// 极简样式工厂：深色主题 + 圆角卡片 + 渐变主按钮，全部原生 API（无第三方依赖）。
/// </summary>
static class Ui
{
    // 配色（深色主题）
    public static readonly Color Bg = new(14, 15, 19);          // 页面背景
    public static readonly Color Card = new(28, 31, 40);        // 卡片底
    public static readonly Color CardStroke = new(48, 52, 64);  // 卡片描边
    public static readonly Color BtnSecondary = new(38, 42, 54);
    public static readonly Color TextPrimary = new(235, 238, 245);
    public static readonly Color TextSecondary = new(150, 156, 170);
    public static readonly Color Accent = new(64, 196, 255);    // 主色（青）
    public static readonly Color AccentDark = new(26, 128, 190);
    public static readonly Color Ok = new(46, 160, 90);
    public static readonly Color OkDark = new(20, 96, 60);
    public static readonly Color Warn = new(214, 138, 26);
    public static readonly Color WarnDark = new(140, 84, 10);
    public static readonly Color Gold = new(255, 213, 79);       // 默认标记（星）
    public static readonly Color Destructive = new(232, 88, 88); // 删除/危险操作

    public static int Dp(Context c, int dp) => (int)(dp * c.Resources!.DisplayMetrics!.Density);

    public static GradientDrawable RoundBg(Color color, Context c, int radiusDp = 18, Color stroke = default, int strokeDp = 0)
    {
        var d = new GradientDrawable();
        d.SetColor(color);
        d.SetCornerRadius(Dp(c, radiusDp));
        if (strokeDp > 0) d.SetStroke(Dp(c, strokeDp), stroke);
        return d;
    }

    public static GradientDrawable GradientBg(Color from, Color to, Context c, int radiusDp = 18)
    {
        var d = new GradientDrawable(GradientDrawable.Orientation.TlBr, new[] { (int)from, (int)to });
        d.SetCornerRadius(Dp(c, radiusDp));
        return d;
    }

    /// <summary>圆角卡片容器（纵向），外边距水平 16dp</summary>
    public static LinearLayout MakeCard(Context c)
    {
        var card = new LinearLayout(c) { Orientation = Orientation.Vertical };
        card.SetBackgroundDrawable(RoundBg(Card, c, 20, CardStroke, 1));
        card.SetPadding(Dp(c, 20), Dp(c, 16), Dp(c, 20), Dp(c, 16));
        var lp = new LinearLayout.LayoutParams(-1, -2);
        lp.SetMargins(Dp(c, 16), Dp(c, 10), Dp(c, 16), Dp(c, 10));
        card.LayoutParameters = lp;
        return card;
    }

    public static TextView Label(Context c, string text)
    {
        var t = new TextView(c) { Text = text, TextSize = 15f };
        t.SetTextColor(TextPrimary);
        t.Typeface = Typeface.Create(Typeface.DefaultBold, TypefaceStyle.Bold);
        return t;
    }

    public static TextView Caption(Context c, string text)
    {
        var t = new TextView(c) { Text = text, TextSize = 12.5f };
        t.SetTextColor(TextSecondary);
        return t;
    }

    public static Button PrimaryButton(Context c, string text)
    {
        var b = new Button(c) { Text = text, TextSize = 15.5f };
        b.SetAllCaps(false);
        b.SetTextColor(Color.White);
        b.Typeface = Typeface.Create(Typeface.DefaultBold, TypefaceStyle.Bold);
        b.SetBackgroundDrawable(GradientBg(AccentDark, Accent, c, 16));
        b.StateListAnimator = null!;
        var lp = new LinearLayout.LayoutParams(-1, Dp(c, 52));
        lp.SetMargins(0, Dp(c, 10), 0, Dp(c, 6));
        b.LayoutParameters = lp;
        return b;
    }

    public static Button SecondaryButton(Context c, string text)
    {
        var b = new Button(c) { Text = text, TextSize = 14.5f };
        b.SetAllCaps(false);
        b.SetTextColor(TextPrimary);
        b.SetBackgroundDrawable(RoundBg(BtnSecondary, c, 16, CardStroke, 1));
        b.StateListAnimator = null!;
        var lp = new LinearLayout.LayoutParams(-1, Dp(c, 48));
        lp.SetMargins(0, Dp(c, 6), 0, Dp(c, 6));
        b.LayoutParameters = lp;
        return b;
    }

    /// <summary>深色风格 SeekBar 滑轨/滑块着色</summary>
    public static void StyleSeek(SeekBar bar)
    {
        bar.ProgressTintList = Android.Content.Res.ColorStateList.ValueOf(Accent);
        bar.ThumbTintList = Android.Content.Res.ColorStateList.ValueOf(Accent);
        bar.ProgressBackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(new Color(55, 60, 75));
    }

    /// <summary>深色风格 Switch</summary>
    public static void StyleSwitch(Switch s)
    {
        s.TrackTintList = Android.Content.Res.ColorStateList.ValueOf(new Color(55, 60, 75));
        s.ThumbTintList = Android.Content.Res.ColorStateList.ValueOf(Accent);
        s.SetTextColor(TextPrimary);
        s.TextSize = 14f;
    }
}
