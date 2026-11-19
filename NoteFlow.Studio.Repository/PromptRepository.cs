using Microsoft.Data.Sqlite;
using NoteFlow.Studio.Model;
using System;
using System.Collections.Generic;
using System.IO;

namespace NoteFlow.Studio.Repository
{
    public class PromptRepository
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public PromptRepository()
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
                CREATE TABLE IF NOT EXISTS Prompts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    Type INTEGER NOT NULL,
                    Content TEXT NOT NULL
                );
            ";
            command.ExecuteNonQuery();

            EnsureDefaultPrompts();
        }

        private void EnsureDefaultPrompts()
        {
            var existing = GetAll();
            if (existing.Count == 0)
            {
                Add(new Prompt
                {
                    Title = "默认清洗提示词",
                    Type = PromptType.CleanText,
                    Content = "请仔细阅读以下视频转换出的文本，将其清洗为通顺的文字：\n1. 去除语气词、重复词\n2. 根据语义添加合适的标点符号和分段\n3. 保持原意不变"
                });
            }
        }

        public void Add(Prompt prompt)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Prompts (Title, Type, Content)
                VALUES (@Title, @Type, @Content);
                SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("@Title", prompt.Title);
            command.Parameters.AddWithValue("@Type", (int)prompt.Type);
            command.Parameters.AddWithValue("@Content", prompt.Content);
            
            prompt.Id = Convert.ToInt32(command.ExecuteScalar());
        }

        public void Update(Prompt prompt)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Prompts 
                SET Title = @Title, Type = @Type, Content = @Content
                WHERE Id = @Id;";
            command.Parameters.AddWithValue("@Id", prompt.Id);
            command.Parameters.AddWithValue("@Title", prompt.Title);
            command.Parameters.AddWithValue("@Type", (int)prompt.Type);
            command.Parameters.AddWithValue("@Content", prompt.Content);
            
            command.ExecuteNonQuery();
        }

        public void Delete(int id)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Prompts WHERE Id = @Id;";
            command.Parameters.AddWithValue("@Id", id);
            
            command.ExecuteNonQuery();
        }

        public List<Prompt> GetAll()
        {
            var list = new List<Prompt>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Title, Type, Content FROM Prompts;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Prompt
                {
                    Id = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    Type = (PromptType)reader.GetInt32(2),
                    Content = reader.GetString(3)
                });
            }
            return list;
        }

        public List<Prompt> GetByType(PromptType type)
        {
            var list = new List<Prompt>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Title, Type, Content FROM Prompts WHERE Type = @Type;";
            command.Parameters.AddWithValue("@Type", (int)type);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Prompt
                {
                    Id = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    Type = (PromptType)reader.GetInt32(2),
                    Content = reader.GetString(3)
                });
            }
            return list;
        }
    }
}
