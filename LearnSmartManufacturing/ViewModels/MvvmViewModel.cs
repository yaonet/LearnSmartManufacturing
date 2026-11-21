using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LearnSmartManufacturing.Models.Mvvm;
using Microsoft.Extensions.DependencyInjection;

namespace LearnSmartManufacturing.ViewModels
{
    public partial class MvvmViewModel : ObservableObject, IDisposable
    {
        private const int MaxTraceEntries = 100;

        private readonly WeakReferenceMessenger _messenger = new();
        private bool _disposed;

        [ActivatorUtilitiesConstructor]
        public MvvmViewModel() : this(deployDelayMs: 900)
        {
        }

        public MvvmViewModel(int deployDelayMs)
        {
            Editor = new RecipeEditorViewModel(_messenger, RecordTrace, deployDelayMs);
            Monitor = new ProductionMonitorViewModel(_messenger, RecordTrace);
            RecordTrace("Binding", "MvvmViewModel", "已通过独立 WeakReferenceMessenger 实例解耦组装 EditorVM 与 MonitorVM。");
        }

        public string Heading => "MVVM";

        public RecipeEditorViewModel Editor { get; }
        public ProductionMonitorViewModel Monitor { get; }

        public ObservableCollection<MvvmTraceEvent> Traces { get; } = [];

        [ObservableProperty]
        public partial string ActiveCategory { get; private set; } = "Binding";

        [ObservableProperty]
        public partial string LastEventText { get; private set; } = "修改左侧参数或点击命令观察 MVVM 数据流";

        [ObservableProperty]
        public partial int TracePulseCount { get; private set; }

        [RelayCommand]
        private void ClearTraces() => Traces.Clear();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Monitor.IsActive = false;
            Editor.Dispose();
        }

        private void RecordTrace(string category, string source, string detail)
        {
            if (_disposed)
            {
                return;
            }

            ActiveCategory = category;
            LastEventText = $"[{category}] {source}: {detail}";
            TracePulseCount++;

            Traces.Insert(0, new MvvmTraceEvent(DateTime.Now, category, source, detail));
            while (Traces.Count > MaxTraceEntries)
            {
                Traces.RemoveAt(Traces.Count - 1);
            }
        }
    }
}
