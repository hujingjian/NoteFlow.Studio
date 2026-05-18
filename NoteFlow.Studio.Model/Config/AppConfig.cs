using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NoteFlow.Studio.Model.Config
{
    [Table("AppConfig")]
    public class AppConfig
    {
        [Key]
        public string Key { get; set; }
        public string Value { get; set; }
    }
}