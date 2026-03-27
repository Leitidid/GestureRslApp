using Android.App;
using Android.Runtime;

namespace GestureRslApp;

/// <summary>
/// Application-класс Android.
/// Регистрирует Camera.MAUI и инициализирует MAUI-хост.
/// </summary>
[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
