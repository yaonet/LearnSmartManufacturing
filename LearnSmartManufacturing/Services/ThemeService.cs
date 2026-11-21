using System.Windows;

namespace LearnSmartManufacturing.Services
{
    public sealed class ThemeService : IThemeService
    {
        public ThemeMode CurrentTheme => Application.Current?.ThemeMode ?? ThemeMode.System;

        public void SetTheme(ThemeMode mode)
        {
            if (Application.Current is not { } app)
            {
                return;
            }

            app.ThemeMode = mode;
            foreach (Window window in app.Windows)
            {
                window.ThemeMode = mode;
            }
        }
    }
}
