using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NoteFlow.Studio.Model
{
    /// <summary>
    /// 提示词类型
    /// </summary>
    public enum PromptType
    {
        /// <summary>
        /// 清洗文案
        /// </summary>
        CleanText,
        /// <summary>
        /// 生成分镜脚本
        /// </summary>
        GenerateStoryboard,
        /// <summary>
        /// 生成分镜HTML (Remotion)
        /// </summary>
        GenerateRemotionHtml
    }

    /// <summary>
    /// 提示词表
    /// </summary>
    public class Prompt
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>
        /// 提示词标题，用于在下拉框中显示
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string Title { get; set; }

        /// <summary>
        /// 提示词类型
        /// </summary>
        [Required]
        public PromptType Type { get; set; }

        /// <summary>
        /// 提示词内容
        /// </summary>
        [Required]
        public string Content { get; set; }
    }
}
