using System.Windows;

namespace LearnSmartManufacturing.Services
{
    public interface IThemeService
    {
        ThemeMode CurrentTheme { get; }

        void SetTheme(ThemeMode mode);
    }
}
