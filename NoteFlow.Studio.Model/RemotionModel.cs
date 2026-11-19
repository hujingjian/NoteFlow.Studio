using System;
using System.Collections.Generic;

namespace NoteFlow.Studio.Model
{
    public class RemotionOverview
    {
        public string Id { get; set; }
        
        /// <summary>
        /// 来源文案（本地目录即可）
        /// </summary>
        public string SourceTextPath { get; set; }

        /// <summary>
        /// 文案清洗内容
        /// </summary>
        public string CleanedText { get; set; }

        /// <summary>
        /// 分镜脚本内容 (JSON格式)
        /// </summary>
        public string StoryboardScript { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public class RemotionStoryboard
    {
        public string Id { get; set; }
        
        /// <summary>
        /// 概览表id
        /// </summary>
        public string OverviewId { get; set; }

        /// <summary>
        /// 分镜Id
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("sceneId")]
        public int SceneId { get; set; }

        /// <summary>
        /// 画面描述
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("画面描述")]
        public string SceneDescription { get; set; }

        /// <summary>
        /// 动画效果
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("动画效果")]
        public string AnimationEffect { get; set; }

        /// <summary>
        /// 字幕
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("字幕")]
        public string Subtitles { get; set; }

        /// <summary>
        /// 时长
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("时长")]
        public decimal Duration { get; set; }

        /// <summary>
        /// BGM/音效
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("BGM/音效")]
        public string BgmSoundtrack { get; set; }

        /// <summary>
        /// 分镜生成的html
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string GeneratedHtml { get; set; }

        /// <summary>
        /// 原始JSON字符串
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string RawJson { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string Status { get; set; }

        /// <summary>
        /// 视频保存位置
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string VideoSavePath { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        [System.Text.Json.Serialization.JsonIgnore]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
