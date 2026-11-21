using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace LearnSmartManufacturing.Services
{
    public sealed partial class NavigationService : ObservableObject, INavigationService, IDisposable
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private IServiceScope? _currentScope;
        private bool _disposed;

        public NavigationService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        [ObservableProperty]
        public partial ObservableObject? CurrentViewModel { get; private set; }

        public void NavigateTo(Type viewModelType)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!typeof(ObservableObject).IsAssignableFrom(viewModelType))
            {
                throw new ArgumentException($"{viewModelType.Name} is not an ObservableObject.", nameof(viewModelType));
            }

            if (CurrentViewModel?.GetType() == viewModelType)
            {
                return;
            }

            // Resolve inside a per-page scope so Transient/Scoped IDisposable ViewModels and their
            // dependencies are disposed on navigation away, while Singleton ViewModels stay alive.
            var nextScope = _scopeFactory.CreateScope();
            ObservableObject nextViewModel;
            try
            {
                nextViewModel = (ObservableObject)nextScope.ServiceProvider.GetRequiredService(viewModelType);
            }
            catch
            {
                nextScope.Dispose();
                throw;
            }

            var previousScope = _currentScope;
            _currentScope = nextScope;
            CurrentViewModel = nextViewModel;
            previousScope?.Dispose();
        }

        public void NavigateTo<TViewModel>() where TViewModel : ObservableObject
            => NavigateTo(typeof(TViewModel));

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentViewModel = null;
            _currentScope?.Dispose();
            _currentScope = null;
        }
    }
}
