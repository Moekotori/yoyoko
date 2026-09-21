using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Chat.UI.Voice;

public sealed record AudioQualityChoice(string Id, string Title, string Detail)
{
    public string Label => Title + "  " + Detail;
}

public interface IVoiceQualityHost : INotifyPropertyChanged
{
    ObservableCollection<AudioQualityChoice> AudioQualityChoices { get; }
    AudioQualityChoice? SelectedAudioQuality { get; set; }
    ObservableCollection<AudioQualityChoice> ChannelQualityChoices { get; }
    AudioQualityChoice? SelectedChannelQuality { get; set; }
    bool CanSetChannelQuality { get; }
}
