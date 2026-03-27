//namespace GestureRslApp.Pages;

//public partial class SettingsPage : ContentPage
//{
//    // Флаг предотвращает срабатывание Toggled при инициализации
//    private bool _isLoaded;

//    public SettingsPage()
//    {
//        InitializeComponent();
//    }

    //protected override void OnAppearing()
    //{
    //    base.OnAppearing();

    //    // Восстанавливаем сохранённое состояние переключателя
    //    bool saved = Preferences.Get(App.HighContrastKey, false);
    //    _isLoaded = false;
    //    HighContrastSwitch.IsToggled = saved;
    //    _isLoaded = true;
    //}

    /// <summary>
    /// Обработчик переключения темы.
    /// Применяет выбранную тему немедленно.
    /// </summary>
    //private void OnHighContrastToggled(object sender, ToggledEventArgs e)
    //{
    //    // Пропускаем вызов при программной инициализации в OnAppearing
    //    if (!_isLoaded) return;

    //    App.ApplyTheme(e.Value);
    
//}
