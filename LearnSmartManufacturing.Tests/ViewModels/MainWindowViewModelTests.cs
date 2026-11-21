using CommunityToolkit.Mvvm.ComponentModel;
using LearnSmartManufacturing.Services;
using LearnSmartManufacturing.ViewModels;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class MainWindowViewModelTests
    {
        [Fact]
        public void Constructor_SelectsHomeAndNavigatesToHomeViewModel()
        {
            var nav = new RecordingNavigationService();
            var vm = new MainWindowViewModel(nav);

            Assert.True(vm.NavigationItems[0].IsSelected);
            Assert.Equal([typeof(HomeViewModel)], nav.History);
        }

        [Fact]
        public void SelectingGroupNode_ExpandsAndNavigatesToFirstChild()
        {
            var previousContext = SynchronizationContext.Current;
            var syncContext = new ImmediateSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(syncContext);
            try
            {
                var nav = new RecordingNavigationService();
                var vm = new MainWindowViewModel(nav);

                var visionGroup = vm.NavigationItems.First(i => i.Title == "机器视觉");
                visionGroup.IsExpanded = false;
                visionGroup.IsSelected = true;

                Assert.True(visionGroup.IsExpanded);
                Assert.Equal(1, syncContext.PostCount);
                Assert.True(visionGroup.Children[0].IsSelected);
                Assert.Equal(typeof(IndustrialCameraViewModel), nav.History[^1]);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }

        [Fact]
        public void SelectingSettingsAndTreeLeaf_KeepsSelectionMutuallyExclusive()
        {
            var nav = new RecordingNavigationService();
            var vm = new MainWindowViewModel(nav);

            var home = vm.NavigationItems[0];
            var settings = vm.BottomItems[0];

            settings.IsSelected = true;
            Assert.False(home.IsSelected);
            Assert.Equal(typeof(SettingsViewModel), nav.History[^1]);

            var motion = vm.NavigationItems.First(i => i.Title == "运动控制");
            motion.IsSelected = true;
            Assert.False(settings.IsSelected);
            Assert.Equal(typeof(MotionControlViewModel), nav.History[^1]);
        }

        private sealed class RecordingNavigationService : ObservableObject, INavigationService
        {
            public List<Type> History { get; } = [];

            public ObservableObject? CurrentViewModel => null;

            public void NavigateTo(Type viewModelType) => History.Add(viewModelType);

            public void NavigateTo<TViewModel>() where TViewModel : ObservableObject => NavigateTo(typeof(TViewModel));
        }

        private sealed class ImmediateSynchronizationContext : SynchronizationContext
        {
            public int PostCount { get; private set; }

            public override void Post(SendOrPostCallback d, object? state)
            {
                PostCount++;
                d(state);
            }
        }
    }
}
