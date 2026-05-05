namespace GestureRslApp.Pages;

public partial class SettingsPage : ContentPage
{
    // Флаг: не запускать handler при программной установке Switch.IsToggled
    private bool _loaded;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Восстанавливаем состояние переключателя из Preferences
        bool highContrast = Preferences.Get(App.HighContrastKey, false);
        _loaded = false;
        HighContrastSwitch.IsToggled = highContrast;
        _loaded = true;

        UpdateThemeDisplay(highContrast);
    }

    /// <summary>
    /// Обработчик переключения режима для слабовидящих.
    /// Применяет цвета немедленно и перезапускает Shell.
    /// </summary>
    private void OnHighContrastToggled(object sender, ToggledEventArgs e)
    {
        // Пропускаем вызов при программной инициализации в OnAppearing
        if (!_loaded) return;

        App.ApplyTheme(e.Value);
        UpdateThemeDisplay(e.Value);
    }

    private void UpdateThemeDisplay(bool highContrast)
    {
        ThemeLabel.Text = highContrast ? "Высокий контраст" : "Светлая тема";
        ThemeIcon.Text  = highContrast ? "🔲" : "☀️";
    }
}
