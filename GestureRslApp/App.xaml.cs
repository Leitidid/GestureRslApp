namespace GestureRslApp;

public partial class App : Application
{
    // Ключ для хранения настройки темы в Preferences
    public const string HighContrastKey = "high_contrast_mode";

    public App()
    {
        InitializeComponent();
        MainPage = new AppShell();

        // Восстанавливаем тему при запуске
        ApplySavedTheme();
    }

    /// <summary>
    /// Применяет тему, сохранённую пользователем.
    /// Вызывается при старте и при переключении в настройках.
    /// </summary>
    public static void ApplySavedTheme()
    {
        bool highContrast = Preferences.Get(HighContrastKey, false);
        //ApplyTheme(highContrast);
    }

    /// <summary>
    /// Переключает тему приложения.
    /// highContrast = true  → высокий контраст (для слабовидящих)
    /// highContrast = false → светлая тема по умолчанию
    /// </summary>
    //public static void ApplyTheme(bool highContrast)
    //{
    //    Preferences.Set(HighContrastKey, highContrast);

    //    // Меняем цвета через MergedDictionaries
    //    Application.Current!.Resources.MergedDictionaries.Clear();

    //    if (highContrast)
    //    {
    //        Application.Current.Resources.MergedDictionaries.Add(
    //            new ResourceDictionary { Source = new Uri("Resources/Styles/ColorsHighContrast.xaml", UriKind.Relative) }
    //        );
    //    }
    //    else
    //    {
    //        Application.Current.Resources.MergedDictionaries.Add(
    //            new ResourceDictionary { Source = new Uri("Resources/Styles/Colors.xaml", UriKind.Relative) }
    //        );
    //    }

    //    Application.Current.Resources.MergedDictionaries.Add(
    //        new ResourceDictionary { Source = new Uri("Resources/Styles/Styles.xaml", UriKind.Relative) }
    //    );
    //}
}
