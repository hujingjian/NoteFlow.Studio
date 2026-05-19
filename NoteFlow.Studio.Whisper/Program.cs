using System.Text.Json;
using Whisper.net;
using Whisper.net.Ggml;

if (args.Length < 2)
{
    Console.WriteLine(JsonSerializer.Serialize(new { Error = "Usage: NoteFlow.Studio.Whisper.exe <modelPath> <audioFilePath>" }));
    return;
}

var modelPath = args[0];
var audioFilePath = args[1];

if (!File.Exists(modelPath))
{
    Console.WriteLine(JsonSerializer.Serialize(new { Error = $"Model file not found: {modelPath}" }));
    return;
}

if (!File.Exists(audioFilePath))
{
    Console.WriteLine(JsonSerializer.Serialize(new { Error = $"Audio file not found: {audioFilePath}" }));
    return;
}

try
{
    var whisperFactory = WhisperFactory.FromPath(modelPath);

    var builder = whisperFactory.CreateBuilder()
        .WithLanguage("auto");

    using var processor = builder.Build();
    using var fileStream = File.OpenRead(audioFilePath);

    await foreach (var result in processor.ProcessAsync(fileStream))
    {
        var segment = new { Start = result.Start.TotalMilliseconds, End = result.End.TotalMilliseconds, Text = result.Text };
        Console.WriteLine(JsonSerializer.Serialize(segment));
    }
}
catch (Exception ex)
{
    Console.WriteLine(JsonSerializer.Serialize(new { Error = $"An error occurred: {ex.Message}" }));
}

