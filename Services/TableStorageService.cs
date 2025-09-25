using Azure.Data.Tables;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Part2FunctionApp.Services
{
    public class TableStorageService<T> where T : class, ITableEntity, new()
    {
        private readonly TableClient _tableClient;

        // Reference: Sbeeh, M. (2019) Azure Storage – Tables, C# Corner. Available at:
        // https://www.c-sharpcorner.com/article/azure-storage-tables/ (Accessed: 27 August 2025).

        // Creates the table if it does not already exist.
        public TableStorageService(string connectionString, string tableName)
        {
            var serviceClient = new TableServiceClient(connectionString);
            _tableClient = serviceClient.GetTableClient(tableName);
            _tableClient.CreateIfNotExists();
        }

        // Inserts a new entity or updates it if it already exists.
        public async Task UpsertEntityAsync(T entity)
        {
            await _tableClient.UpsertEntityAsync(entity);
        }

        // Retrieves a single entity from the table using the partition key and row key.
        public async Task<T> GetEntityAsync(string partitionKey, string rowKey)
        {
            var response = await _tableClient.GetEntityAsync<T>(partitionKey, rowKey);
            return response.Value;
        }

        // Retrieves all entities from the table.
        public async Task<List<T>> GetAllEntities()
        {
            var results = new List<T>();

            await foreach (var entity in _tableClient.QueryAsync<T>())
            {
                results.Add(entity);
            }

            return results;
        }

        // Deletes an entity from the table using the partition key and row key.
        public async Task DeleteEntityAsync(string partitionKey, string rowKey)
        {
            await _tableClient.DeleteEntityAsync(partitionKey, rowKey);
        }
    }
}