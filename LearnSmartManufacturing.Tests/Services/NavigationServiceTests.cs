using CommunityToolkit.Mvvm.ComponentModel;
using LearnSmartManufacturing.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LearnSmartManufacturing.Tests.Services
{
    public class NavigationServiceTests
    {
        [Fact]
        public void NavigateTo_DisposesPreviousTransientViewModelButKeepsSingletonAlive()
        {
            var services = new ServiceCollection();
            services.AddSingleton<NavigationService>();
            services.AddTransient<DisposableTransientViewModel>();
            services.AddSingleton<DisposableSingletonViewModel>();

            using var provider = services.BuildServiceProvider();
            var nav = provider.GetRequiredService<NavigationService>();

            nav.NavigateTo<DisposableTransientViewModel>();
            var transientVm = Assert.IsType<DisposableTransientViewModel>(nav.CurrentViewModel);
            Assert.False(transientVm.IsDisposed);

            nav.NavigateTo<DisposableSingletonViewModel>();
            var singletonVm = Assert.IsType<DisposableSingletonViewModel>(nav.CurrentViewModel);
            Assert.True(transientVm.IsDisposed);
            Assert.False(singletonVm.IsDisposed);

            nav.NavigateTo<DisposableTransientViewModel>();
            var transientVm2 = Assert.IsType<DisposableTransientViewModel>(nav.CurrentViewModel);
            Assert.NotSame(transientVm, transientVm2);
            Assert.False(singletonVm.IsDisposed);

            nav.Dispose();
            Assert.True(transientVm2.IsDisposed);
            Assert.Null(nav.CurrentViewModel);
            Assert.False(singletonVm.IsDisposed);
        }

        [Fact]
        public void NavigateTo_SameType_IsNoOp()
        {
            var services = new ServiceCollection();
            services.AddSingleton<NavigationService>();
            services.AddTransient<DisposableTransientViewModel>();

            using var provider = services.BuildServiceProvider();
            var nav = provider.GetRequiredService<NavigationService>();

            nav.NavigateTo<DisposableTransientViewModel>();
            var first = nav.CurrentViewModel;

            nav.NavigateTo<DisposableTransientViewModel>();
            Assert.Same(first, nav.CurrentViewModel);
            Assert.False(((DisposableTransientViewModel)first!).IsDisposed);
        }

        [Fact]
        public void NavigateTo_NonObservableObject_ThrowsArgumentException()
        {
            var services = new ServiceCollection();
            services.AddSingleton<NavigationService>();

            using var provider = services.BuildServiceProvider();
            var nav = provider.GetRequiredService<NavigationService>();

            Assert.Throws<ArgumentException>(() => nav.NavigateTo(typeof(string)));
        }

        [Fact]
        public void NavigateTo_UnregisteredViewModel_PreservesCurrentPage()
        {
            var services = new ServiceCollection();
            services.AddSingleton<NavigationService>();
            services.AddTransient<DisposableTransientViewModel>();

            using var provider = services.BuildServiceProvider();
            var nav = provider.GetRequiredService<NavigationService>();

            nav.NavigateTo<DisposableTransientViewModel>();
            var current = Assert.IsType<DisposableTransientViewModel>(nav.CurrentViewModel);

            Assert.Throws<InvalidOperationException>(() => nav.NavigateTo<UnregisteredViewModel>());
            Assert.Same(current, nav.CurrentViewModel);
            Assert.False(current.IsDisposed);
        }

        private sealed class DisposableTransientViewModel : ObservableObject, IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose() => IsDisposed = true;
        }

        private sealed class DisposableSingletonViewModel : ObservableObject, IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose() => IsDisposed = true;
        }

        private sealed class UnregisteredViewModel : ObservableObject;
    }
}
