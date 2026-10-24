using Android.Graphics;
using Android.OS;
using Android.Views;

namespace WallpaperEngine.App;

/// <summary>
/// 系统栏内边距监听：Android 15+ 强制 edge-to-edge（内容画到状态栏/导航栏底下），
/// 需把系统栏高度转为容器 padding；旧版本 insets 为 0，行为不变。
/// </summary>
public class SystemBarsInsetListener : Java.Lang.Object, View.IOnApplyWindowInsetsListener
{
    public WindowInsets? OnApplyWindowInsets(View? v, WindowInsets? insets)
    {
        if (v == null || insets == null) return insets;

        int top, bottom;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            var sys = insets.SystemWindowInsets!;
            top = sys.Top;
            bottom = sys.Bottom;
        }
        else
        {
            top = insets.SystemWindowInsetTop;
            bottom = insets.SystemWindowInsetBottom;
        }

        v.SetPadding(v.PaddingLeft, top, v.PaddingRight, bottom);
        return insets;
    }
}
