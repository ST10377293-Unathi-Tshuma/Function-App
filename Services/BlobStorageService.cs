using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Azure.Storage;

namespace Part2FunctionApp.Services
{
    public class BlobStorageService
    {
        private readonly string _containerName;
        private readonly string accountName;
        private readonly string accountKey;

        // Retrieves the storage account name, key, and container name from the configuration.
        // Microsoft, 2025. Configuration in ASP.NET Core. [Online] 
        // Available at: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/ 
        // [Accessed 13 May 2025].
        public BlobStorageService(IConfiguration configuration)
        {
            accountName = configuration["AzureBlob:StorageAccountName"] ?? throw new ArgumentNullException("StorageAccountName is not configured");
            accountKey = configuration["AzureBlob:StorageAccountKey"] ?? throw new ArgumentNullException("StorageAccountKey is not configured");
            _containerName = configuration["AzureBlob:ContainerName"] ?? throw new ArgumentNullException("ContainerName is not configured");
        }


        // BlobContainerClient instance for interacting with the specified container.</returns>
        // Constructs a BlobServiceClient using the account name and key, then returns a client for the specified container.
        // Microsoft, 2025. Quickstart: Azure Blob Storage client library for .NET. [Online] 
        // Available at: https://learn.microsoft.com/en-us/azure/storage/blobs/storage-quickstart-blobs-dotnet 
        // [Accessed 13 May 2025].
        public BlobContainerClient GetContainerClient()
        {
            var serviceUri = new Uri($"https://{accountName}.blob.core.windows.net");
            var serviceClient = new BlobServiceClient(serviceUri, new StorageSharedKeyCredential(accountName, accountKey));
            return serviceClient.GetBlobContainerClient(_containerName);
        }

        // Uploads an image file stream to Azure Blob Storage and returns the blob name.
        // A task that resolves to the unique blob name of the uploaded file.</returns>
        // Creates the container if it does not exist, generates a unique blob name, and uploads the file with an image content type.
        // Microsoft, 2025. Upload a blob to Azure Blob Storage using the .NET SDK. [Online] 
        // Available at: https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blob-upload-dotnet 
        // [Accessed 13 May 2025].
        public async Task<string> UploadImageAsync(Stream fileStream, string fileName)
        {
            var containerClient = GetContainerClient();
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

            var blobName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";
            var blobClient = containerClient.GetBlobClient(blobName);
            // Upload the file stream to Azure Blob Storage.
            await blobClient.UploadAsync(fileStream, new BlobHttpHeaders { ContentType = "image/*" });
            return blobName;
        }

        // Deletes a blob from Azure Blob Storage if it exists.
        // A task representing the asynchronous deletion operation.</returns>
        // Checks if the blob name is valid, then attempts to delete the blob using the BlobClient.
        // Microsoft, 2025. Delete a blob in Azure Blob Storage using the .NET SDK. [Online] 
        // Available at: https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blob-delete-dotnet 
        // [Accessed 13 May 2025].
        public async Task DeleteBlobAsync(string blobName)
        {
            if (string.IsNullOrEmpty(blobName))
                return;
            var containerClient = GetContainerClient();
            var blobClient = containerClient.GetBlobClient(blobName);
            await blobClient.DeleteIfExistsAsync();
        }

        // Generates a shared access signature (SAS) URL for a blob with read permissions.
        // The SAS URL for the blob, or null if the blob name is empty.</returns>
        // Creates a short-lived SAS token with read permissions, valid for one hour, and appends it to the blob URI.
        // Microsoft, 2025. Create a service SAS for a blob with .NET. [Online] 
        // Available at: https://learn.microsoft.com/en-us/azure/storage/blobs/sas-service-create-dotnet 
        // [Accessed 13 May 2025].
        public string GetBlobSasUrl(string blobName)
        {
            if (string.IsNullOrEmpty(blobName))
                return null;

            var blobUri = new Uri($"https://{accountName}.blob.core.windows.net/{_containerName}/{blobName}");

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _containerName,
                BlobName = blobName,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(1) //short-lived sas
            };

            sasBuilder.SetPermissions(BlobSasPermissions.Read);
            var sasToken = sasBuilder.ToSasQueryParameters(new StorageSharedKeyCredential(accountName, accountKey));
            return $"{blobUri}?{sasToken}";
        }
    }
}