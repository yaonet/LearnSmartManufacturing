using CommunityToolkit.Mvvm.ComponentModel;
using LearnSmartManufacturing.Services;
using System.Windows;

namespace LearnSmartManufacturing.ViewModels
{
    public record ThemeOption(string Name, ThemeMode Value);

    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IThemeService _themeService;

        public SettingsViewModel(IThemeService themeService)
        {
            _themeService = themeService;
            ThemeOptions =
            [
                new ThemeOption("跟随系统", ThemeMode.System),
                new ThemeOption("浅色", ThemeMode.Light),
                new ThemeOption("深色", ThemeMode.Dark),
            ];

            var current = _themeService.CurrentTheme;
            SelectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == current) ?? ThemeOptions[0];
        }

        public string Heading => "设置";

        public IReadOnlyList<ThemeOption> ThemeOptions { get; }

        [ObservableProperty]
        public partial ThemeOption? SelectedTheme { get; set; }

        partial void OnSelectedThemeChanged(ThemeOption? value)
        {
            if (value is null || value.Value == _themeService.CurrentTheme)
            {
                return;
            }

            _themeService.SetTheme(value.Value);
        }
    }
}
