using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LearnSmartManufacturing.Models.Mvvm;

namespace LearnSmartManufacturing.ViewModels
{
    /// <summary>
    /// Independent subscriber ViewModel inheriting <see cref="ObservableRecipient"/>.
    /// Holds zero direct references to <see cref="RecipeEditorViewModel"/>; receives updates
    /// exclusively through <see cref="IMessenger"/> while <see cref="ObservableRecipient.IsActive"/> is true.
    /// </summary>
    public partial class ProductionMonitorViewModel : ObservableRecipient, IRecipient<RecipeDeployedMessage>
    {
        private readonly Action<string, string, string> _trace;

        public ProductionMonitorViewModel(IMessenger messenger, Action<string, string, string> trace)
            : base(messenger)
        {
            _trace = trace;
            ActiveRecipe = new ProcessRecipe("SMT-2026", 225, 250, 45, 1, DateTime.Now);
            IsActive = true;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActiveRecipeCode), nameof(ActiveVersionText), nameof(ActiveTempText), nameof(ActiveAlarmTempText), nameof(ActiveSpeedText), nameof(LastSyncText))]
        public partial ProcessRecipe ActiveRecipe { get; private set; }

        [ObservableProperty]
        public partial int ReceivedMessageCount { get; private set; }

        public string SubscriptionStateText => IsActive
            ? "已激活 (IsActive = True · 自动接收消息)"
            : "已停用 (IsActive = False · 自动退订防泄漏)";

        public string ActiveRecipeCode => ActiveRecipe.RecipeCode;
        public string ActiveVersionText => $"v{ActiveRecipe.Version}";
        public string ActiveTempText => $"{ActiveRecipe.TargetTempC:F0} °C";
        public string ActiveAlarmTempText => $"{ActiveRecipe.AlarmTempC:F0} °C";
        public string ActiveSpeedText => $"{ActiveRecipe.ConveyorSpeedMmPerSec:F0} mm/s";
        public string LastSyncText => $"同步时间 {ActiveRecipe.DeployedAt:HH:mm:ss} · 累计接收 {ReceivedMessageCount} 次";

        public void Receive(RecipeDeployedMessage message)
        {
            ActiveRecipe = message.Recipe;
            ReceivedMessageCount++;
            OnPropertyChanged(nameof(LastSyncText));
            _trace("Messenger", "ProductionMonitorVM",
                $"Receive(RecipeDeployedMessage)：看板已同步 v{message.Recipe.Version} · {message.Recipe.RecipeCode}");
        }

        [RelayCommand]
        private void BroadcastOverTempAlarm()
        {
            var measured = ActiveRecipe.AlarmTempC + 6.5;
            _trace("Messenger", "ProductionMonitorVM", $"反向广播 LineAlarmMessage (实测 {measured:F1}°C > 阈值 {ActiveRecipe.AlarmTempC:F0}°C)");
            Messenger.Send(new LineAlarmMessage("AL-201", $"回流焊炉温超限 ({ActiveRecipe.RecipeCode})", measured));
        }

        protected override void OnActivated()
        {
            base.OnActivated();
            OnPropertyChanged(nameof(SubscriptionStateText));
            _trace("Messenger", "ObservableRecipient", "ProductionMonitorVM.IsActive = True → 已向 WeakReferenceMessenger 注册订阅。");
        }

        protected override void OnDeactivated()
        {
            base.OnDeactivated();
            OnPropertyChanged(nameof(SubscriptionStateText));
            _trace("Messenger", "ObservableRecipient", "ProductionMonitorVM.IsActive = False → 已自动退订，不再接收新下发的配方。");
        }
    }
}
