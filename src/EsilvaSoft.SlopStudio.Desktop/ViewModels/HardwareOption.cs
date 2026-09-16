using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class HardwareOption(AiAccelerationMode mode, string label) : ObservableObject
{
    public AiAccelerationMode Mode { get; } = mode;
    [ObservableProperty] private string _label = label;
    [ObservableProperty] private bool _isAvailable = true;
    public override string ToString() => Label;
}
