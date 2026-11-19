using CommunityToolkit.Mvvm.ComponentModel;

namespace LearnSmartManufacturing.ViewModels
{
    /// <summary>
    /// A menu node. A node with children is a group; a node without children
    /// navigates to <see cref="ViewModelType"/>.
    /// </summary>
    public partial class NavigationItem : ObservableObject
    {
        public NavigationItem(string title, Type? viewModelType = null, params NavigationItem[] children)
        {
            Title = title;
            ViewModelType = viewModelType;
            Children = children;
        }

        public string Title { get; }

        public Type? ViewModelType { get; }

        public IReadOnlyList<NavigationItem> Children { get; }

        [ObservableProperty]
        public partial bool IsSelected { get; set; }

        [ObservableProperty]
        public partial bool IsExpanded { get; set; } = true;
    }
}
