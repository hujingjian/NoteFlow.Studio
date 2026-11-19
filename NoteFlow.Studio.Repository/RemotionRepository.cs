using Microsoft.Data.Sqlite;
using NoteFlow.Studio.Model;
using System;
using System.Collections.Generic;
using System.IO;

namespace NoteFlow.Studio.Repository
{
    public class RemotionRepository
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public RemotionRepository()
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoteFlow Studio");
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            _dbPath = Path.Combine(appDataPath, "config.db");
            _connectionString = $"Data Source={_dbPath}";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS RemotionOverview (
                    Id TEXT PRIMARY KEY,
                    SourceTextPath TEXT,
                    CleanedText TEXT,
                    StoryboardScript TEXT,
                    CreatedAt DATETIME,
                    UpdatedAt DATETIME
                );

                CREATE TABLE IF NOT EXISTS RemotionStoryboard (
                    Id TEXT PRIMARY KEY,
                    OverviewId TEXT,
                    SceneId INTEGER,
                    SceneDescription TEXT,
                    AnimationEffect TEXT,
                    Subtitles TEXT,
                    Duration INTEGER,
                    BgmSoundtrack TEXT,
                    GeneratedHtml TEXT,
                    Status TEXT,
                    VideoSavePath TEXT,
                    RawJson TEXT,
                    CreatedAt DATETIME,
                    UpdatedAt DATETIME
                );
            ";
            command.ExecuteNonQuery();
        }

        public void SaveOrUpdateOverview(RemotionOverview overview)
        {
            overview.UpdatedAt = DateTime.Now;
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO RemotionOverview (Id, SourceTextPath, CleanedText, StoryboardScript, CreatedAt, UpdatedAt)
                VALUES (@Id, @SourceTextPath, @CleanedText, @StoryboardScript, @CreatedAt, @UpdatedAt)
                ON CONFLICT(Id) DO UPDATE SET 
                    SourceTextPath = @SourceTextPath,
                    CleanedText = @CleanedText,
                    StoryboardScript = @StoryboardScript,
                    UpdatedAt = @UpdatedAt;
            ";
            command.Parameters.AddWithValue("@Id", overview.Id);
            command.Parameters.AddWithValue("@SourceTextPath", overview.SourceTextPath ?? string.Empty);
            command.Parameters.AddWithValue("@CleanedText", overview.CleanedText ?? string.Empty);
            command.Parameters.AddWithValue("@StoryboardScript", overview.StoryboardScript ?? string.Empty);
            command.Parameters.AddWithValue("@CreatedAt", overview.CreatedAt);
            command.Parameters.AddWithValue("@UpdatedAt", overview.UpdatedAt);
            command.ExecuteNonQuery();
        }

        public int GetRemotionOverviewCount()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM RemotionOverview";
            var result = command.ExecuteScalar();
            return Convert.ToInt32(result);
        }

        public List<RemotionOverview> GetPagedRemotionOverviews(int pageNumber, int pageSize)
        {
            var list = new List<RemotionOverview>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, SourceTextPath, CleanedText, StoryboardScript, CreatedAt, UpdatedAt
                FROM RemotionOverview
                ORDER BY CreatedAt DESC
                LIMIT @Limit OFFSET @Offset;
            ";
            command.Parameters.AddWithValue("@Limit", pageSize);
            command.Parameters.AddWithValue("@Offset", (pageNumber - 1) * pageSize);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new RemotionOverview
                {
                    Id = reader.GetString(0),
                    SourceTextPath = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    CleanedText = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    StoryboardScript = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    CreatedAt = reader.GetDateTime(4),
                    UpdatedAt = reader.GetDateTime(5)
                });
            }
            return list;
        }

        public void SaveStoryboard(RemotionStoryboard storyboard)
        {
            storyboard.UpdatedAt = DateTime.Now;
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO RemotionStoryboard (Id, OverviewId, SceneId, SceneDescription, AnimationEffect, Subtitles, Duration, BgmSoundtrack, GeneratedHtml, Status, VideoSavePath, RawJson, CreatedAt, UpdatedAt)
                VALUES (@Id, @OverviewId, @SceneId, @SceneDescription, @AnimationEffect, @Subtitles, @Duration, @BgmSoundtrack, @GeneratedHtml, @Status, @VideoSavePath, @RawJson, @CreatedAt, @UpdatedAt)
                ON CONFLICT(Id) DO UPDATE SET 
                    SceneDescription = @SceneDescription,
                    AnimationEffect = @AnimationEffect,
                    Subtitles = @Subtitles,
                    Duration = @Duration,
                    BgmSoundtrack = @BgmSoundtrack,
                    GeneratedHtml = @GeneratedHtml,
                    Status = @Status,
                    VideoSavePath = @VideoSavePath,
                    RawJson = @RawJson,
                    UpdatedAt = @UpdatedAt;
            ";
            command.Parameters.AddWithValue("@Id", storyboard.Id);
            command.Parameters.AddWithValue("@OverviewId", storyboard.OverviewId ?? string.Empty);
            command.Parameters.AddWithValue("@SceneId", storyboard.SceneId);
            command.Parameters.AddWithValue("@SceneDescription", storyboard.SceneDescription ?? string.Empty);
            command.Parameters.AddWithValue("@AnimationEffect", storyboard.AnimationEffect ?? string.Empty);
            command.Parameters.AddWithValue("@Subtitles", storyboard.Subtitles ?? string.Empty);
            command.Parameters.AddWithValue("@Duration", storyboard.Duration);
            command.Parameters.AddWithValue("@BgmSoundtrack", storyboard.BgmSoundtrack ?? string.Empty);
            command.Parameters.AddWithValue("@GeneratedHtml", storyboard.GeneratedHtml ?? string.Empty);
            command.Parameters.AddWithValue("@Status", storyboard.Status ?? string.Empty);
            command.Parameters.AddWithValue("@VideoSavePath", storyboard.VideoSavePath ?? string.Empty);
            command.Parameters.AddWithValue("@RawJson", storyboard.RawJson ?? string.Empty);
            command.Parameters.AddWithValue("@CreatedAt", storyboard.CreatedAt);
            command.Parameters.AddWithValue("@UpdatedAt", storyboard.UpdatedAt);
            command.ExecuteNonQuery();
        }

        public void UpdateStoryboardHtmlAndStatus(string id, string generatedHtml, string status)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE RemotionStoryboard 
                SET GeneratedHtml = @GeneratedHtml, Status = @Status, UpdatedAt = @UpdatedAt
                WHERE Id = @Id;
            ";
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@GeneratedHtml", generatedHtml ?? string.Empty);
            command.Parameters.AddWithValue("@Status", status ?? string.Empty);
            command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
            command.ExecuteNonQuery();
        }

        public void UpdateStoryboardHtmlAndStatusByScene(string overviewId, int sceneId, string generatedHtml, string status)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE RemotionStoryboard 
                SET GeneratedHtml = @GeneratedHtml, Status = @Status, UpdatedAt = @UpdatedAt
                WHERE OverviewId = @OverviewId AND SceneId = @SceneId;
            ";
            command.Parameters.AddWithValue("@OverviewId", overviewId);
            command.Parameters.AddWithValue("@SceneId", sceneId);
            command.Parameters.AddWithValue("@GeneratedHtml", generatedHtml ?? string.Empty);
            command.Parameters.AddWithValue("@Status", status ?? string.Empty);
            command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
            command.ExecuteNonQuery();
        }

        public void UpdateStoryboardVideoSavePath(string id, string videoSavePath, string status)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE RemotionStoryboard 
                SET VideoSavePath = @VideoSavePath, Status = @Status, UpdatedAt = @UpdatedAt
                WHERE Id = @Id;
            ";
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@VideoSavePath", videoSavePath ?? string.Empty);
            command.Parameters.AddWithValue("@Status", status ?? string.Empty);
            command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
            command.ExecuteNonQuery();
        }

        public void UpdateStoryboardVideoSavePathByScene(string overviewId, int sceneId, string videoSavePath, string status)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE RemotionStoryboard 
                SET VideoSavePath = @VideoSavePath, Status = @Status, UpdatedAt = @UpdatedAt
                WHERE OverviewId = @OverviewId AND SceneId = @SceneId;
            ";
            command.Parameters.AddWithValue("@OverviewId", overviewId);
            command.Parameters.AddWithValue("@SceneId", sceneId);
            command.Parameters.AddWithValue("@VideoSavePath", videoSavePath ?? string.Empty);
            command.Parameters.AddWithValue("@Status", status ?? string.Empty);
            command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);
            command.ExecuteNonQuery();
        }

        public RemotionOverview GetOverview(string id)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, SourceTextPath, CleanedText, StoryboardScript, CreatedAt, UpdatedAt FROM RemotionOverview WHERE Id = @Id";
            command.Parameters.AddWithValue("@Id", id);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return new RemotionOverview
                {
                    Id = reader.GetString(0),
                    SourceTextPath = reader.GetString(1),
                    CleanedText = reader.GetString(2),
                    StoryboardScript = reader.GetString(3),
                    CreatedAt = reader.GetDateTime(4),
                    UpdatedAt = reader.GetDateTime(5)
                };
            }
            return null;
        }

        public List<RemotionStoryboard> GetStoryboardsByOverviewId(string overviewId)
        {
            var list = new List<RemotionStoryboard>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, OverviewId, SceneId, SceneDescription, AnimationEffect, Subtitles, Duration, BgmSoundtrack, GeneratedHtml, Status, VideoSavePath, CreatedAt, UpdatedAt, RawJson FROM RemotionStoryboard WHERE OverviewId = @OverviewId ORDER BY SceneId ASC";
            command.Parameters.AddWithValue("@OverviewId", overviewId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new RemotionStoryboard
                {
                    Id = reader.GetString(0),
                    OverviewId = reader.GetString(1),
                    SceneId = reader.GetInt32(2),
                    SceneDescription = reader.GetString(3),
                    AnimationEffect = reader.GetString(4),
                    Subtitles = reader.GetString(5),
                    Duration = reader.GetInt32(6),
                    BgmSoundtrack = reader.GetString(7),
                    GeneratedHtml = reader.GetString(8),
                    Status = reader.GetString(9),
                    VideoSavePath = reader.GetString(10),
                    CreatedAt = reader.GetDateTime(11),
                    UpdatedAt = reader.GetDateTime(12),
                    RawJson = reader.IsDBNull(13) ? string.Empty : reader.GetString(13)
                });
            }
            return list;
        }

        public RemotionStoryboard GetByOverviewAndScene(string overviewId, int sceneId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, OverviewId, SceneId, SceneDescription, AnimationEffect, Subtitles, Duration, BgmSoundtrack, GeneratedHtml, Status, VideoSavePath, CreatedAt, UpdatedAt, RawJson FROM RemotionStoryboard WHERE OverviewId = @OverviewId AND SceneId = @SceneId";
            command.Parameters.AddWithValue("@OverviewId", overviewId);
            command.Parameters.AddWithValue("@SceneId", sceneId);
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return new RemotionStoryboard
                {
                    Id = reader.GetString(0),
                    OverviewId = reader.GetString(1),
                    SceneId = reader.GetInt32(2),
                    SceneDescription = reader.GetString(3),
                    AnimationEffect = reader.GetString(4),
                    Subtitles = reader.GetString(5),
                    Duration = reader.GetInt32(6),
                    BgmSoundtrack = reader.GetString(7),
                    GeneratedHtml = reader.GetString(8),
                    Status = reader.GetString(9),
                    VideoSavePath = reader.GetString(10),
                    CreatedAt = reader.GetDateTime(11),
                    UpdatedAt = reader.GetDateTime(12),
                    RawJson = reader.IsDBNull(13) ? string.Empty : reader.GetString(13)
                };
            }
            return null;
        }
    }
}