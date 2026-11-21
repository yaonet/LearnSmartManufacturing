using CommunityToolkit.Mvvm.ComponentModel;
using LearnSmartManufacturing.Models;
using LearnSmartManufacturing.Services;
using System.ComponentModel;

namespace LearnSmartManufacturing.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
        private NavigationItem? _treeLeaf;

        public MainWindowViewModel(INavigationService navigation)
        {
            Navigation = navigation;
            NavigationItems =
            [
                new NavigationItem("首页", typeof(HomeViewModel)),
                new NavigationItem("WPF", null,
                    new NavigationItem("异步编程", typeof(AsyncProgrammingViewModel)),
                    new NavigationItem("MVVM", typeof(MvvmViewModel))),
                new NavigationItem("协议", null,
                    new NavigationItem("Modbus", typeof(ModbusViewModel)),
                    new NavigationItem("EtherCAT", typeof(EtherCatViewModel)),
                    new NavigationItem("MQTT", typeof(MqttViewModel)),
                    new NavigationItem("TCP/串口", typeof(SerialTcpViewModel))),
                new NavigationItem("机器视觉", null,
                    new NavigationItem("相机", typeof(CameraViewModel)),
                    new NavigationItem("镜头", typeof(LensViewModel)),
                    new NavigationItem("光源", typeof(LightingViewModel)),
                    new NavigationItem("仿真", typeof(SimulationViewModel)),
                    new NavigationItem("标定", typeof(CalibrationViewModel))),
                new NavigationItem("运动控制", typeof(MotionControlViewModel)),
            ];
            SettingsItem = new NavigationItem("设置", typeof(SettingsViewModel));
            BottomItems = [SettingsItem];

            foreach (var item in NavigationItems)
            {
                Subscribe(item);
            }

            Subscribe(SettingsItem);

            NavigationItems[0].IsSelected = true;
        }

        public INavigationService Navigation { get; }

        public IReadOnlyList<NavigationItem> NavigationItems { get; }

        /// <summary>Items pinned to the bottom of the navigation panel, outside the tree.</summary>
        public IReadOnlyList<NavigationItem> BottomItems { get; }

        private NavigationItem SettingsItem { get; }

        [ObservableProperty]
        public partial string? Title { get; set; } = "Learn Smart Manufacturing";

        private void Subscribe(NavigationItem item)
        {
            item.PropertyChanged += OnItemPropertyChanged;
            foreach (var child in item.Children)
            {
                Subscribe(child);
            }
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not NavigationItem { IsSelected: true } item || e.PropertyName != nameof(NavigationItem.IsSelected))
            {
                return;
            }

            if (item.Children.Count > 0)
            {
                // A group node has no page of its own: expand it and open its first child.
                // Deferred so the TreeView finishes the current selection change first;
                // changing selection re-entrantly leaves stale items highlighted.
                item.IsExpanded = true;
                var first = item.Children[0];
                if (_uiContext is null)
                {
                    first.IsSelected = true;
                }
                else
                {
                    _uiContext.Post(_ => first.IsSelected = true, null);
                }
            }
            else if (item.ViewModelType is not null)
            {
                // The tree and the bottom list are separate controls, so keep only one item highlighted.
                if (item == SettingsItem)
                {
                    if (_treeLeaf is { IsSelected: true })
                    {
                        _treeLeaf.IsSelected = false;
                    }
                }
                else
                {
                    _treeLeaf = item;
                    SettingsItem.IsSelected = false;
                }

                Navigation.NavigateTo(item.ViewModelType);
            }
        }
    }
}
