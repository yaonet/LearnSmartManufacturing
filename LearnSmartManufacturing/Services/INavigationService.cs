using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace LearnSmartManufacturing.Services
{
    public interface INavigationService : INotifyPropertyChanged
    {
        ObservableObject? CurrentViewModel { get; }

        void NavigateTo(Type viewModelType);

        void NavigateTo<TViewModel>() where TViewModel : ObservableObject;
    }
}
