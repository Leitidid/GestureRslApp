using Camera.MAUI;
using CommunityToolkit.Maui;
using GestureRslApp.Pages;
using GestureRslApp.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;

namespace GestureRslApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            // Подключаем Camera.MAUI для работы с камерой
            .UseMauiCameraView()
            // Подключаем CommunityToolkit для дополнительных компонентов UI
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // ─── Регистрация сервисов (Dependency Injection) ───────────────────

        // 🔥 ДОБАВЛЕНО: Регистрация HistoryService
        builder.Services.AddSingleton<HistoryService>();

        builder.Services.AddSingleton<DynamicGestureRecognizer>();

        // Сервис распознавания жестов — один экземпляр на всё приложение
        builder.Services.AddSingleton<HandGestureRecognizer>();
       
        // Страницы — создаются заново при каждом переходе
        builder.Services.AddTransient<CameraPage>();
        builder.Services.AddTransient<HistoryPage>();
        //builder.Services.AddTransient<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}