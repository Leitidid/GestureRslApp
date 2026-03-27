using Android.App;
using Android.Content.PM;

namespace GestureRslApp;

/// <summary>
/// Точка входа Android-приложения.
/// LaunchMode.SingleTop — предотвращает создание дублирующих Activity при перезапуске.
/// </summary>
[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges =
        ConfigChanges.ScreenSize |
        ConfigChanges.Orientation |
        ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout |
        ConfigChanges.SmallestScreenSize |
        ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}
