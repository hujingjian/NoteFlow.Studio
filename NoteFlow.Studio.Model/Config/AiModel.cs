using System;

namespace NoteFlow.Studio.Model.Config
{
    public class AiModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsCustom { get; set; } = false;
        public bool IsEnabled { get; set; } = false;
        public string Endpoint { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
    }
}
