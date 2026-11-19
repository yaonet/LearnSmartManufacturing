using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace LearnSmartManufacturing.Services
{
    public partial class NavigationService : ObservableObject, INavigationService
    {
        private readonly IServiceProvider _serviceProvider;

        public NavigationService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [ObservableProperty]
        public partial ObservableObject? CurrentViewModel { get; private set; }

        public void NavigateTo(Type viewModelType)
        {
            if (!typeof(ObservableObject).IsAssignableFrom(viewModelType))
            {
                throw new ArgumentException($"{viewModelType.Name} is not an ObservableObject.", nameof(viewModelType));
            }

            if (CurrentViewModel?.GetType() == viewModelType)
            {
                return;
            }

            CurrentViewModel = (ObservableObject)_serviceProvider.GetRequiredService(viewModelType);
        }

        public void NavigateTo<TViewModel>() where TViewModel : ObservableObject
            => NavigateTo(typeof(TViewModel));
    }
}
