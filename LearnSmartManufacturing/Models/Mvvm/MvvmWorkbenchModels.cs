namespace LearnSmartManufacturing.Models.Mvvm
{
    /// <summary>Immutable snapshot of an industrial reflow/conveyor process recipe.</summary>
    public sealed record ProcessRecipe(
        string RecipeCode,
        double TargetTempC,
        double AlarmTempC,
        double ConveyorSpeedMmPerSec,
        int Version,
        DateTime DeployedAt)
    {
        public string SummaryText =>
            $"目标 {TargetTempC:F0}°C · 报警 {AlarmTempC:F0}°C · 带速 {ConveyorSpeedMmPerSec:F0} mm/s";
    }

    /// <summary>Decoupled message broadcast via <see cref="CommunityToolkit.Mvvm.Messaging.IMessenger"/> when a recipe is deployed.</summary>
    public sealed record RecipeDeployedMessage(ProcessRecipe Recipe);

    /// <summary>Reverse decoupled message broadcast from the line monitor back to subscribers.</summary>
    public sealed record LineAlarmMessage(string Code, string Message, double MeasuredTempC);

    /// <summary>Runtime trace item visualizing MVVM data-binding, validation, command and messenger events.</summary>
    public sealed record MvvmTraceEvent(DateTime Timestamp, string Category, string Source, string Detail)
    {
        public string TimeText => Timestamp.ToString("HH:mm:ss.fff");
    }
}
