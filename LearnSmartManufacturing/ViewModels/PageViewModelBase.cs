using CommunityToolkit.Mvvm.ComponentModel;

namespace LearnSmartManufacturing.ViewModels
{
    public abstract class PageViewModelBase : ObservableObject
    {
        public abstract string Heading { get; }
    }
}
