using Microsoft.Data.Sqlite;
using NoteFlow.Studio.Model;
using System;
using System.Collections.Generic;
using System.IO;

namespace NoteFlow.Studio.Repository
{
    public class ProcessingRecordRepository
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public ProcessingRecordRepository()
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
                CREATE TABLE IF NOT EXISTS ProcessingRecords (
                    VideoId TEXT PRIMARY KEY,
                    Title TEXT,
                    Description TEXT,
                    VideoUrl TEXT,
                    CoverUrl TEXT,
                    ProcessingStartTime DATETIME,
                    Status TEXT
                );
            ";
            command.ExecuteNonQuery();
        }

        public void AddOrUpdate(ProcessingRecord record)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO ProcessingRecords (VideoId, Title, Description, VideoUrl, CoverUrl, ProcessingStartTime, Status)
                VALUES ($VideoId, $Title, $Description, $VideoUrl, $CoverUrl, $ProcessingStartTime, $Status)
            ";
            command.Parameters.AddWithValue("$VideoId", record.VideoId);
            command.Parameters.AddWithValue("$Title", record.Title);
            command.Parameters.AddWithValue("$Description", record.Description ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$VideoUrl", record.VideoUrl);
            command.Parameters.AddWithValue("$CoverUrl", record.CoverUrl ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$ProcessingStartTime", record.ProcessingStartTime);
            command.Parameters.AddWithValue("$Status", record.Status);
            command.ExecuteNonQuery();
        }

        public List<ProcessingRecord> GetAll()
        {
            var list = new List<ProcessingRecord>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT VideoId, Title, Description, VideoUrl, CoverUrl, ProcessingStartTime, Status FROM ProcessingRecords";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new ProcessingRecord
                {
                    VideoId = reader.GetString(0),
                    Title = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    VideoUrl = reader.GetString(3),
                    CoverUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ProcessingStartTime = reader.GetDateTime(5),
                    Status = reader.GetString(6)
                });
            }
            return list;
        }

        public void Delete(string videoId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM ProcessingRecords WHERE VideoId = $VideoId";
            command.Parameters.AddWithValue("$VideoId", videoId);
            command.ExecuteNonQuery();
        }

        public ProcessingRecord GetById(string videoId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT VideoId, Title, Description, VideoUrl, CoverUrl, ProcessingStartTime, Status FROM ProcessingRecords WHERE VideoId = $VideoId";
            command.Parameters.AddWithValue("$VideoId", videoId);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return new ProcessingRecord
                {
                    VideoId = reader.GetString(0),
                    Title = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    VideoUrl = reader.GetString(3),
                    CoverUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ProcessingStartTime = reader.GetDateTime(5),
                    Status = reader.GetString(6)
                };
            }
            return null;
        }
    }
}
