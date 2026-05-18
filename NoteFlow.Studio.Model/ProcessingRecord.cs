using System;

namespace NoteFlow.Studio.Model
{
    public class ProcessingRecord
    {
        public string VideoId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string VideoUrl { get; set; }
        public string CoverUrl { get; set; }
        public DateTime ProcessingStartTime { get; set; }
        public string Status { get; set; }
    }
}
