using System.Windows;
using LearnSmartManufacturing.Services;
using LearnSmartManufacturing.ViewModels;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class SettingsViewModelTests
    {
        [Fact]
        public void Constructor_SelectsCurrentThemeWithoutReapplying()
        {
            var themeService = new FakeThemeService(ThemeMode.Dark);

            var vm = new SettingsViewModel(themeService);

            Assert.NotNull(vm.SelectedTheme);
            Assert.Equal(ThemeMode.Dark, vm.SelectedTheme.Value);
            Assert.Equal(0, themeService.SetCount);
        }

        [Fact]
        public void SelectingNewTheme_AppliesThroughThemeService()
        {
            var themeService = new FakeThemeService(ThemeMode.System);
            var vm = new SettingsViewModel(themeService);

            vm.SelectedTheme = vm.ThemeOptions.First(o => o.Value == ThemeMode.Light);

            Assert.Equal(ThemeMode.Light, themeService.CurrentTheme);
            Assert.Equal(1, themeService.SetCount);
        }

        private sealed class FakeThemeService(ThemeMode initial) : IThemeService
        {
            public ThemeMode CurrentTheme { get; private set; } = initial;

            public int SetCount { get; private set; }

            public void SetTheme(ThemeMode mode)
            {
                CurrentTheme = mode;
                SetCount++;
            }
        }
    }
}
