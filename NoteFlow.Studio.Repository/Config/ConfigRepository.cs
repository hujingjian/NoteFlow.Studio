using Microsoft.Data.Sqlite;
using NoteFlow.Studio.Model.Config;
using System;
using System.Collections.Generic;
using System.IO;

namespace NoteFlow.Studio.Repository.Config
{
    public class ConfigRepository
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public ConfigRepository()
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SelfMediaKnowledgeBase");
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
                CREATE TABLE IF NOT EXISTS AppConfig (
                    Key TEXT PRIMARY KEY,
                    Value TEXT
                );
            ";
            command.ExecuteNonQuery();
        }

        public string GetConfig(string key, string defaultValue = "")
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT Value FROM AppConfig WHERE Key = $key";
            command.Parameters.AddWithValue("$key", key);

            var result = command.ExecuteScalar();
            return result?.ToString() ?? defaultValue;
        }

        public void SetConfig(string key, string value)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO AppConfig (Key, Value) 
                VALUES ($key, $value)
                ON CONFLICT(Key) DO UPDATE SET Value = $value;
            ";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }

        public Dictionary<string, string> GetAllConfigs()
        {
            var dict = new Dictionary<string, string>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT Key, Value FROM AppConfig";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                dict[reader.GetString(0)] = reader.GetString(1);
            }
            return dict;
        }
    }
}