using Microsoft.Data.Sqlite;
using NoteFlow.Studio.Model.Config;
using System;
using System.Collections.Generic;
using System.IO;

namespace NoteFlow.Studio.Repository.Config
{
    public class AiModelRepository
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public AiModelRepository()
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
                CREATE TABLE IF NOT EXISTS AiModels (
                    Id TEXT PRIMARY KEY,
                    Name TEXT,
                    IsCustom INTEGER,
                    IsEnabled INTEGER,
                    Endpoint TEXT,
                    ApiKey TEXT,
                    ModelName TEXT
                );
            ";
            command.ExecuteNonQuery();
        }

        public List<AiModel> GetAll()
        {
            var list = new List<AiModel>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Name, IsCustom, IsEnabled, Endpoint, ApiKey, ModelName FROM AiModels";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new AiModel
                {
                    Id = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    IsCustom = reader.GetInt32(2) == 1,
                    IsEnabled = reader.GetInt32(3) == 1,
                    Endpoint = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    ApiKey = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    ModelName = reader.IsDBNull(6) ? "" : reader.GetString(6)
                });
            }
            return list;
        }

        public void SaveAll(IEnumerable<AiModel> models)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            try
            {
                var cmdDelete = connection.CreateCommand();
                cmdDelete.Transaction = transaction;
                cmdDelete.CommandText = "DELETE FROM AiModels";
                cmdDelete.ExecuteNonQuery();

                var cmdInsert = connection.CreateCommand();
                cmdInsert.Transaction = transaction;
                cmdInsert.CommandText = @"
                    INSERT INTO AiModels (Id, Name, IsCustom, IsEnabled, Endpoint, ApiKey, ModelName) 
                    VALUES ($Id, $Name, $IsCustom, $IsEnabled, $Endpoint, $ApiKey, $ModelName)
                ";

                var pId = cmdInsert.Parameters.Add("$Id", SqliteType.Text);
                var pName = cmdInsert.Parameters.Add("$Name", SqliteType.Text);
                var pIsCustom = cmdInsert.Parameters.Add("$IsCustom", SqliteType.Integer);
                var pIsEnabled = cmdInsert.Parameters.Add("$IsEnabled", SqliteType.Integer);
                var pEndpoint = cmdInsert.Parameters.Add("$Endpoint", SqliteType.Text);
                var pApiKey = cmdInsert.Parameters.Add("$ApiKey", SqliteType.Text);
                var pModelName = cmdInsert.Parameters.Add("$ModelName", SqliteType.Text);

                foreach (var m in models)
                {
                    pId.Value = m.Id ?? "";
                    pName.Value = m.Name ?? "";
                    pIsCustom.Value = m.IsCustom ? 1 : 0;
                    pIsEnabled.Value = m.IsEnabled ? 1 : 0;
                    pEndpoint.Value = m.Endpoint ?? "";
                    pApiKey.Value = m.ApiKey ?? "";
                    pModelName.Value = m.ModelName ?? "";
                    cmdInsert.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}
