using System;
using System.Threading;
using System.Threading.Tasks;

namespace NoteFlow.Studio.Service.Interfaces
{
    public interface IWhisperService
    {
        Task ValidateModelAsync(string modelPath);
        Task<string> ExtractAudioToWavAsync(string videoFilePath, string toolDownloadDir, CancellationToken cancellationToken = default);
        Task TranscribeAudioAsync(string wavFilePath, string modelPath, string language, Action<string, string> onSegmentRecognized, CancellationToken cancellationToken = default);
    }
}