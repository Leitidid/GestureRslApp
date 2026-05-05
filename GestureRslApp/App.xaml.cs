namespace GestureRslApp;

public partial class App : Application
{
    public const string HighContrastKey = "high_contrast_mode";

    public App()
    {
        InitializeComponent();
        MainPage = new AppShell();
        ApplySavedTheme();
    }

    /// <summary>При запуске восстанавливает тему без пересоздания Shell.</summary>
    public static void ApplySavedTheme()
    {
        bool highContrast = Preferences.Get(HighContrastKey, false);
        ApplyResourceValues(highContrast);
    }

    /// <summary>
    /// Переключает тему. Напрямую меняет значения в ResourceDictionary,
    /// затем пересоздаёт AppShell чтобы все StaticResource биндинги обновились.
    ///
    /// Почему не MergedDictionaries.Clear():
    ///   На Android динамическое изменение MergedDictionaries через Uri
    ///   не работает после инициализации — вызывает исключение.
    ///   Прямая мутация словаря работает на всех платформах.
    /// </summary>
    public static void ApplyTheme(bool highContrast)
    {
        Preferences.Set(HighContrastKey, highContrast);
        ApplyResourceValues(highContrast);
        // Пересоздаём Shell — страницы пересоздадутся и заберут новые значения
        if (Application.Current != null)
            Application.Current.MainPage = new AppShell();
    }

    private static void ApplyResourceValues(bool highContrast)
    {
        var res = Application.Current?.Resources;
        if (res == null) return;

        if (highContrast)
        {
            res["Primary"] = Color.FromArgb("#000000");
            res["PrimaryLight"] = Color.FromArgb("#E0E0E0");
            res["PageBackground"] = Color.FromArgb("#FFFFFF");
            res["CardBackground"] = Color.FromArgb("#F0F0F0");
            res["PrimaryText"] = Color.FromArgb("#000000");
            res["SecondaryText"] = Color.FromArgb("#333333");
            res["DividerColor"] = Color.FromArgb("#000000");
            res["SuccessColor"] = Color.FromArgb("#006600");
            res["WarningColor"] = Color.FromArgb("#CC6600");
            res["FontSizeSmall"] = 16.0;
            res["FontSizeMedium"] = 20.0;
            res["FontSizeLarge"] = 28.0;
            res["FontSizeLetter"] = 96.0;
        }
        else
        {
            res["Primary"] = Color.FromArgb("#6750A4");
            res["PrimaryLight"] = Color.FromArgb("#E8DEF8");
            res["PageBackground"] = Color.FromArgb("#FFFBFE");
            res["CardBackground"] = Color.FromArgb("#FFF8F7");
            res["PrimaryText"] = Color.FromArgb("#1C1B1F");
            res["SecondaryText"] = Color.FromArgb("#49454F");
            res["DividerColor"] = Color.FromArgb("#E0E0E0");
            res["SuccessColor"] = Color.FromArgb("#4CAF50");
            res["WarningColor"] = Color.FromArgb("#FF9800");
            res["FontSizeSmall"] = 14.0;
            res["FontSizeMedium"] = 16.0;
            res["FontSizeLarge"] = 20.0;
            res["FontSizeLetter"] = 72.0;
        }
    }
}
