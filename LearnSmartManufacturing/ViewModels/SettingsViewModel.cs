using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;

namespace LearnSmartManufacturing.ViewModels
{
    public record ThemeOption(string Name, ThemeMode Value);

    public partial class SettingsViewModel : PageViewModelBase
    {
        public SettingsViewModel()
        {
            ThemeOptions =
            [
                new ThemeOption("跟随系统", ThemeMode.System),
                new ThemeOption("浅色", ThemeMode.Light),
                new ThemeOption("深色", ThemeMode.Dark),
            ];

            // This view model is recreated on every navigation, so read the current theme back from the app.
            var current = Application.Current.ThemeMode;
            SelectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == current) ?? ThemeOptions[0];
        }

        public override string Heading => "设置";

        public IReadOnlyList<ThemeOption> ThemeOptions { get; }

        [ObservableProperty]
        public partial ThemeOption? SelectedTheme { get; set; }

        partial void OnSelectedThemeChanged(ThemeOption? value)
        {
            if (value is null)
            {
                return;
            }

            var app = Application.Current;
            app.ThemeMode = value.Value;
            foreach (Window window in app.Windows)
            {
                window.ThemeMode = value.Value;
            }
        }
    }
}
