namespace Vice.Display;

internal sealed class RecordingStatusDisplay : IStatusDisplay
{
    public List<StatusRecord> Records { get; } = new();

    public IStatusHandle Start(string label, IConsoleWriter console)
    {
        var record = new StatusRecord(label);
        Records.Add(record);
        return new RecordingHandle(record, console);
    }

    internal sealed class StatusRecord
    {
        public string Label { get; }
        public bool? Success { get; set; }
        public List<double> ProgressReports { get; } = new();

        public StatusRecord(string label) => Label = label;
    }

    private sealed class RecordingHandle : IStatusHandle
    {
        private readonly StatusRecord _record;

        public IConsoleWriter Writer { get; }
        public bool SupportsProgress => true;

        public RecordingHandle(StatusRecord record, IConsoleWriter writer)
        {
            _record = record;
            Writer = writer;
        }

        public void Succeed() => _record.Success = true;
        public void Fail() => _record.Success = false;
        public void UpdateLabel(string label) { }

        public void UpdateProgress(double fraction)
        {
            _record.ProgressReports.Add(fraction);
        }

        public ValueTask DisposeAsync()
        {
            _record.Success ??= false;
            return ValueTask.CompletedTask;
        }
    }
}
