using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;
using Part2FunctionApp.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Part1.Services
{
    public class FileShareStorageService
    {
        private readonly string _connectionString;
        private readonly string _shareName = "contracts";

        //Butt, M. (2021) Using Azure File Storage in C#, C# Corner, 28 May. Available at: https://www.c-sharpcorner.com/article/using-azure-file-storage-in-c-sharp/
        //(Accessed: 27 August 2025).
        public FileShareStorageService(string connectionString, string shareName)
        {
            _connectionString = connectionString ?? throw new Exception("Connection string isnt found");
            _shareName = shareName ?? _shareName; // instead of hardcoded
        }


        private ShareClient GetShareClient() =>
            new ShareClient(_connectionString, _shareName);

        /// <summary>
        /// Uploads audit log entries as an Excel file to Azure File Share.
        /// </summary>
        public async Task UploadAuditLogAsync(IEnumerable<AuditLog> logs, string fileName)
        {
            var share = GetShareClient();
            await share.CreateIfNotExistsAsync();

            var rootDir = share.GetRootDirectoryClient();

            // Build CSV content
            var sb = new StringBuilder();
            sb.AppendLine("Id,TableName,Action,PerformedBy,Timestamp,DataSnapshot"); // header

            foreach (var log in logs)
            {
                // Escape commas or quotes in data
                string Escape(string s) =>
                    s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

                sb.AppendLine($"{log.Id},{Escape(log.TableName)},{Escape(log.Action)},{log.Timestamp:u},{Escape(log.DataSnapshot)}");
            }

            var csvBytes = Encoding.UTF8.GetBytes(sb.ToString());
            using var ms = new MemoryStream(csvBytes);

            // Upload to Azure File Share
            var fileClient = rootDir.GetFileClient(fileName);
            await fileClient.CreateAsync(ms.Length);
            await fileClient.UploadAsync(ms);
        }
        /// <summary>
        /// List files stored in the Azure File Share.
        /// </summary>
        public async Task<List<string>> ListFilesAsync()
        {
            var share = GetShareClient();
            var rootDir = share.GetRootDirectoryClient();

            var files = new List<string>();
            await foreach (ShareFileItem item in rootDir.GetFilesAndDirectoriesAsync())
            {
                if (!item.IsDirectory)
                    files.Add(item.Name);
            }

            return files;
        }

        /// <summary>
        /// Download a file from Azure File Share.
        /// </summary>
        public async Task<Stream?> DownloadFileAsync(string fileName)
        {
            var share = GetShareClient();
            var rootDir = share.GetRootDirectoryClient();
            var fileClient = rootDir.GetFileClient(fileName);

            if (!await fileClient.ExistsAsync())
                return null;

            var download = await fileClient.DownloadAsync();

            var ms = new MemoryStream();
            await download.Value.Content.CopyToAsync(ms);
            ms.Position = 0; // important!

            return ms;
        }

    }
}