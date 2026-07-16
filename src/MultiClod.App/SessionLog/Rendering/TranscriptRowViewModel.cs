using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// Base for one rendered row in the transcript viewer. A row is a stable instance for its whole
/// lifetime - a tool-call row mutates its own fields in place when its result arrives (see
/// ToolCallRowViewModel.ApplyToolResult) rather than being replaced, so a user's
/// IsExpanded/IsAdditionalPropertiesExpanded choice survives a live update untouched.
/// </summary>
public abstract class TranscriptRowViewModel : INotifyPropertyChanged
{
    private bool isExpanded;
    private bool isAdditionalPropertiesExpanded;

    protected TranscriptRowViewModel(TranscriptRowCategory category, DateTimeOffset? timestamp)
    {
        this.Category = category;
        this.Timestamp = timestamp;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TranscriptRowCategory Category { get; }

    public DateTimeOffset? Timestamp { get; }

    public abstract string SummaryText { get; }

    // The full content shown when a row is expanded - distinct from SummaryText (truncated, for
    // the collapsed row) and AdditionalPropertiesJson (leftover JSON fields, in its own nested
    // expander). E.g. a message row's full untruncated text, or a tool call's full input/result.
    public abstract string ExpandedBodyText { get; }

    public abstract string AdditionalPropertiesJson { get; }

    public abstract string CopyableJson { get; }

    public bool IsExpanded
    {
        get => this.isExpanded;
        set => this.SetField(ref this.isExpanded, value);
    }

    public bool IsAdditionalPropertiesExpanded
    {
        get => this.isAdditionalPropertiesExpanded;
        set => this.SetField(ref this.isAdditionalPropertiesExpanded, value);
    }

    // Called by a subclass after mutating the fields its Summary/AdditionalProperties/Copyable
    // getters read from (e.g. a tool call's result arriving), so bound UI recomputes them.
    protected void RaiseContentChanged()
    {
        this.RaisePropertyChanged(nameof(this.SummaryText));
        this.RaisePropertyChanged(nameof(this.ExpandedBodyText));
        this.RaisePropertyChanged(nameof(this.AdditionalPropertiesJson));
        this.RaisePropertyChanged(nameof(this.CopyableJson));
    }

    protected void RaisePropertyChanged(string propertyName) =>
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        this.RaisePropertyChanged(propertyName!);
    }
}
