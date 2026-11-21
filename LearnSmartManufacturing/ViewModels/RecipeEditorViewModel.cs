using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LearnSmartManufacturing.Models.Mvvm;

namespace LearnSmartManufacturing.ViewModels
{
    /// <summary>
    /// Industrial process-recipe editor demonstrating <see cref="ObservableValidator"/>,
    /// cross-property <see cref="CustomValidationAttribute"/>, dirty-state tracking,
    /// <see cref="IAsyncRelayCommand"/> with generated cancel command, and decoupled messaging.
    /// </summary>
    public partial class RecipeEditorViewModel : ObservableValidator, IRecipient<LineAlarmMessage>, IDisposable
    {
        private readonly IMessenger _messenger;
        private readonly Action<string, string, string> _trace;
        private readonly int _deployDelayMs;

        private ProcessRecipe _committed;
        private int _version = 1;
        private bool _disposed;

        public RecipeEditorViewModel(
            IMessenger messenger,
            Action<string, string, string> trace,
            int deployDelayMs = 900)
        {
            _messenger = messenger;
            _trace = trace;
            _deployDelayMs = deployDelayMs;

            _committed = new ProcessRecipe("SMT-2026", 225, 250, 45, _version, DateTime.Now);
            SavedRecipes.Add(_committed);

            ErrorsChanged += OnValidationErrorsChanged;
            PropertyChanged += OnEditorPropertyChanged;
            _messenger.Register(this);

            ValidateAllProperties();
        }

        public ObservableCollection<ProcessRecipe> SavedRecipes { get; } = [];

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Required(ErrorMessage = "配方编号不能为空")]
        [RegularExpression(@"^[A-Z]{2,4}-\d{3,4}$", ErrorMessage = "编号格式须为 2~4 位大写字母 + '-' + 3~4 位数字（如 SMT-2026）")]
        [NotifyPropertyChangedFor(nameof(IsDirty), nameof(DirtyBadgeText), nameof(ValidationStatusText))]
        [NotifyCanExecuteChangedFor(nameof(DeployRecipeCommand), nameof(RevertCommand))]
        public partial string RecipeCode { get; set; } = "SMT-2026";

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [CustomValidation(typeof(RecipeEditorViewModel), nameof(ValidateTargetTemp))]
        [NotifyPropertyChangedFor(nameof(IsDirty), nameof(DirtyBadgeText), nameof(ValidationStatusText))]
        [NotifyCanExecuteChangedFor(nameof(DeployRecipeCommand), nameof(RevertCommand))]
        public partial string TargetTempText { get; set; } = "225";

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [CustomValidation(typeof(RecipeEditorViewModel), nameof(ValidateAlarmTemp))]
        [NotifyPropertyChangedFor(nameof(IsDirty), nameof(DirtyBadgeText), nameof(ValidationStatusText))]
        [NotifyCanExecuteChangedFor(nameof(DeployRecipeCommand), nameof(RevertCommand))]
        public partial string AlarmTempText { get; set; } = "250";

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [CustomValidation(typeof(RecipeEditorViewModel), nameof(ValidateConveyorSpeed))]
        [NotifyPropertyChangedFor(nameof(IsDirty), nameof(DirtyBadgeText), nameof(ValidationStatusText))]
        [NotifyCanExecuteChangedFor(nameof(DeployRecipeCommand), nameof(RevertCommand))]
        public partial string ConveyorSpeedText { get; set; } = "45";

        // UpdateSourceTrigger comparison demo (PropertyChanged vs LostFocus)
        [ObservableProperty]
        public partial string LiveTriggerText { get; set; } = "产线 A 班次";

        [ObservableProperty]
        public partial string LostFocusTriggerText { get; set; } = "失焦后才同步到 VM";

        [ObservableProperty]
        public partial string? LastLineAlarmText { get; private set; }

        [ObservableProperty]
        public partial bool HasLineAlarm { get; private set; }

        [ObservableProperty]
        public partial string DeployProgressText { get; private set; } = "等待编辑后下发";

        public bool IsDirty =>
            !string.Equals(RecipeCode, _committed.RecipeCode, StringComparison.Ordinal)
            || !TryParseNumber(TargetTempText, out var t) || Math.Abs(t - _committed.TargetTempC) > 1e-6
            || !TryParseNumber(AlarmTempText, out var a) || Math.Abs(a - _committed.AlarmTempC) > 1e-6
            || !TryParseNumber(ConveyorSpeedText, out var s) || Math.Abs(s - _committed.ConveyorSpeedMmPerSec) > 1e-6;

        public string DirtyBadgeText => IsDirty ? "● 有未下发修改 (Dirty)" : "✓ 已与产线同步";

        public string ValidationStatusText
        {
            get
            {
                var count = GetErrors().Count();
                return count == 0 ? "✓ 全部参数校验通过" : $"✗ 存在 {count} 项校验错误（已自动禁用下发命令）";
            }
        }

        public string ErrorSummary => string.Join("\n", GetErrors().Select(e => $"• {e.ErrorMessage}"));

        public static ValidationResult? ValidateTargetTemp(string? value, ValidationContext context)
        {
            if (!TryParseNumber(value, out var temp) || temp < 150 || temp > 280)
            {
                return new ValidationResult("目标温度须在 150 ~ 280 °C 之间");
            }

            return ValidationResult.Success;
        }

        public static ValidationResult? ValidateAlarmTemp(string? value, ValidationContext context)
        {
            if (!TryParseNumber(value, out var alarm) || alarm < 160 || alarm > 320)
            {
                return new ValidationResult("超温报警阈值须在 160 ~ 320 °C 之间");
            }

            if (context.ObjectInstance is RecipeEditorViewModel vm
                && TryParseNumber(vm.TargetTempText, out var target)
                && alarm < target + 10)
            {
                return new ValidationResult($"跨属性校验失败：报警阈值 ({alarm:F0}°C) 必须高于目标温度至少 10 °C（≥ {target + 10:F0}°C）");
            }

            return ValidationResult.Success;
        }

        public static ValidationResult? ValidateConveyorSpeed(string? value, ValidationContext context)
        {
            if (!TryParseNumber(value, out var speed) || speed < 10 || speed > 120)
            {
                return new ValidationResult("传送带速度须在 10 ~ 120 mm/s 之间");
            }

            return ValidationResult.Success;
        }

        public void Receive(LineAlarmMessage message)
        {
            HasLineAlarm = true;
            LastLineAlarmText = $"[{message.Code}] {message.Message}（实测 {message.MeasuredTempC:F1} °C）";
            _trace("Messenger", "RecipeEditorVM", $"反向接收 LineAlarmMessage：{message.Code} ({message.MeasuredTempC:F1}°C)");
        }

        [RelayCommand(CanExecute = nameof(CanDeploy), IncludeCancelCommand = true)]
        private async Task DeployRecipeAsync(CancellationToken cancellationToken)
        {
            ValidateAllProperties();
            if (HasErrors)
            {
                return;
            }

            _trace("Command", "DeployRecipeCommand", $"触发 AsyncRelayCommand：开始异步下发 {RecipeCode}（自动防重入禁用按钮）");
            DeployProgressText = "正在校验并下发配方至 PLC...";

            try
            {
                if (_deployDelayMs > 0)
                {
                    await Task.Delay(_deployDelayMs, cancellationToken);
                }

                var recipe = new ProcessRecipe(
                    RecipeCode.Trim(),
                    ParseOrDefault(TargetTempText, 225),
                    ParseOrDefault(AlarmTempText, 250),
                    ParseOrDefault(ConveyorSpeedText, 45),
                    ++_version,
                    DateTime.Now);

                _committed = recipe;
                SavedRecipes.Insert(0, recipe);
                HasLineAlarm = false;
                LastLineAlarmText = null;
                DeployProgressText = $"已下发 v{recipe.Version} · {recipe.RecipeCode} ({recipe.DeployedAt:HH:mm:ss})";

                RefreshDirtyAndCommands();
                _trace("Messenger", "WeakReferenceMessenger", $"发布 RecipeDeployedMessage (v{recipe.Version} · {recipe.RecipeCode})");
                _messenger.Send(new RecipeDeployedMessage(recipe));
            }
            catch (OperationCanceledException)
            {
                DeployProgressText = "下发已通过 CancelCommand 安全取消";
                _trace("Command", "DeployRecipeCancelCommand", "用户取消了在途异步配方下发。");
            }
        }

        private bool CanDeploy() => !HasErrors && IsDirty;

        [RelayCommand(CanExecute = nameof(CanRevert))]
        private void Revert()
        {
            RecipeCode = _committed.RecipeCode;
            TargetTempText = _committed.TargetTempC.ToString("0.##", CultureInfo.CurrentCulture);
            AlarmTempText = _committed.AlarmTempC.ToString("0.##", CultureInfo.CurrentCulture);
            ConveyorSpeedText = _committed.ConveyorSpeedMmPerSec.ToString("0.##", CultureInfo.CurrentCulture);
            DeployProgressText = $"已撤销未保存修改，恢复至 v{_committed.Version}";
            _trace("Command", "RevertCommand", $"撤销编辑并恢复已同步快照 {_committed.RecipeCode} (v{_committed.Version})");
        }

        private bool CanRevert() => IsDirty;

        [RelayCommand]
        private void FillInvalidSample()
        {
            RecipeCode = "bad_code";
            TargetTempText = "310";
            AlarmTempText = "290";
            ConveyorSpeedText = "200";
            _trace("Validator", "FillInvalidSample", "已注入 4 项非法参数（格式错误、目标温超限、跨属性温差不足、带速超限），验证 CanExecute 自动置灰。");
        }

        [RelayCommand]
        private void LoadRecipe(ProcessRecipe? recipe)
        {
            if (recipe is null)
            {
                return;
            }

            RecipeCode = recipe.RecipeCode;
            TargetTempText = recipe.TargetTempC.ToString("0.##", CultureInfo.CurrentCulture);
            AlarmTempText = recipe.AlarmTempC.ToString("0.##", CultureInfo.CurrentCulture);
            ConveyorSpeedText = recipe.ConveyorSpeedMmPerSec.ToString("0.##", CultureInfo.CurrentCulture);
            _trace("Command", "LoadRecipeCommand", $"从历史配方列表载入 {recipe.RecipeCode} (v{recipe.Version})。");
        }

        [RelayCommand]
        private void DismissLineAlarm()
        {
            HasLineAlarm = false;
            LastLineAlarmText = null;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ErrorsChanged -= OnValidationErrorsChanged;
            PropertyChanged -= OnEditorPropertyChanged;
            _messenger.UnregisterAll(this);
            DeployRecipeCancelCommand.Execute(null);
        }

        partial void OnTargetTempTextChanged(string value)
        {
            // Re-run cross-property validation on AlarmTempText whenever TargetTempText changes.
            ValidateProperty(AlarmTempText, nameof(AlarmTempText));
        }

        private void OnValidationErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ValidationStatusText));
            OnPropertyChanged(nameof(ErrorSummary));
            DeployRecipeCommand.NotifyCanExecuteChanged();
            _trace("Validator", "INotifyDataErrorInfo",
                $"ErrorsChanged({e.PropertyName}) → HasErrors={HasErrors}，触发 CanExecuteChanged。");
        }

        private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(RecipeCode) or nameof(TargetTempText) or nameof(AlarmTempText)
                or nameof(ConveyorSpeedText) or nameof(LiveTriggerText) or nameof(LostFocusTriggerText))
            {
                var currentVal = e.PropertyName switch
                {
                    nameof(RecipeCode) => RecipeCode,
                    nameof(TargetTempText) => TargetTempText,
                    nameof(AlarmTempText) => AlarmTempText,
                    nameof(ConveyorSpeedText) => ConveyorSpeedText,
                    nameof(LiveTriggerText) => LiveTriggerText,
                    nameof(LostFocusTriggerText) => LostFocusTriggerText,
                    _ => string.Empty,
                };
                _trace("Binding", "PropertyChanged", $"{e.PropertyName} = \"{currentVal}\"");
            }
        }

        private void RefreshDirtyAndCommands()
        {
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(DirtyBadgeText));
            DeployRecipeCommand.NotifyCanExecuteChanged();
            RevertCommand.NotifyCanExecuteChanged();
        }

        private static bool TryParseNumber(string? text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && double.IsFinite(value);

        private static double ParseOrDefault(string? text, double fallback) =>
            TryParseNumber(text, out var v) ? v : fallback;
    }
}
