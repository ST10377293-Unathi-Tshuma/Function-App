using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Part2FunctionApp.Models;
using Part2FunctionApp.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Part2FunctionApp.Functions
{
    public class ProductFunctions
    {
        private readonly TableStorageService<Product> _productTableService;
        private readonly BlobStorageService _blobStorageService;
        private readonly QueueStorageService _queueStorageService;
        private const string PARTITION_KEY = "Product";

        public ProductFunctions(TableStorageService<Product> productTableService, BlobStorageService blobStorageService, QueueStorageService queueStorageService)
        {
            _productTableService = productTableService;
            _blobStorageService = blobStorageService;
            _queueStorageService = queueStorageService;
        }

        [FunctionName("GetProducts")]
        public async Task<IActionResult> GetProducts(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "products")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Retrieving all products");

            var products = await _productTableService.GetAllEntities();

            if(products == null)
            {
                log.LogInformation("No products found.");
                return new OkObjectResult(new { Message = "No products found.", Products = new Product[0] });
            }

            var productdto = products.Select(p => new ProductDTO
            {
                PartitionKey = p.PartitionKey, 
                RowKey = p.RowKey, 
                Timestamp = p.Timestamp, 
                ETag = p.ETag.ToString(), 
                Name = p.Name, 
                Description = p.Description, 
                Price = p.Price,
                ImageUrl = _blobStorageService.GetBlobSasUrl(p.ImageUrl)
            });
            return new OkObjectResult(productdto);
        }

        [FunctionName("GetProduct")]
        public async Task<IActionResult> GetProduct(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route ="products/{productId}")] HttpRequest req, ILogger log, string productId)
        {
            log.LogInformation("Processing request to retrieve product with ID: {productId}", productId);

            if(string.IsNullOrWhiteSpace(productId))
            {
                log.LogWarning("Product ID is missing or empty.");
                return new BadRequestObjectResult("Product ID is required.");
            }

            try
            {
                var product = await _productTableService.GetEntityAsync(PARTITION_KEY, productId);

                if(product == null)
                {
                    log.LogWarning("Product with ID {productId} not found.", productId);
                    return new NotFoundObjectResult($"Product with ID {productId} not found.");
                }

                return new OkObjectResult(product);
            }
            catch(Exception ex)
            {
                log.LogError(ex, "Error retrieving product with ID {productId}.", productId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
            
        }

        [FunctionName("CreateProduct")]
        public async Task<IActionResult> CreateProduct([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "products")] HttpRequest req, ILogger log)
        {
            log.LogInformation("Processing product creation request");

            //Read form data
            var formData = await req.ReadFormAsync();
            var rowKey = Guid.NewGuid().ToString();

            var product = new Product
            {
                PartitionKey = PARTITION_KEY,
                RowKey = rowKey,
                Name = formData["name"],
                Description = formData["description"],
                Price = int.TryParse(formData["price"], out var price) ? price : 0,
            };

            log.LogInformation($"Creating student with partitionkey: {PARTITION_KEY}, rowkey: {rowKey}");

            // handle your photo if uploaded
            var imageFile = formData.Files.FirstOrDefault();
            if (imageFile != null && imageFile.Length > 0)
            {
                using var imageStream = imageFile.OpenReadStream();
                var blobName = await _blobStorageService.UploadImageAsync(imageStream, imageFile.FileName);
                product.ImageUrl = blobName;
            }

            if (string.IsNullOrEmpty(product.PartitionKey) || string.IsNullOrEmpty(product.RowKey))
            {
                return new BadRequestObjectResult("PartitionKey and RowKey are required.");
            }

            await _productTableService.UpsertEntityAsync(product);
            //Send a message to the queue for further processing
            var auditLog = new AuditLog
            {
                TableName = "Products",
                Action = "Create",
                DataSnapshot = JsonConvert.SerializeObject(new
                {
                    ProductId = product.RowKey,
                    product.Name,
                    product.Price,
                    HasImage = !string.IsNullOrEmpty(product.ImageUrl)
                })
            };
            await _queueStorageService.SendLogEntryAsync(auditLog);

            return new OkObjectResult(new
            {
                success = true,
                message = "Product created successfully",
                productId = product.RowKey,
                productName = product.Name,
                imageUrl = product.ImageUrl
            });
        }

        [FunctionName("UpdateProduct")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "products/{productId}")] HttpRequest req,
            string productId,
            ILogger log)
        {
            log.LogInformation($"Processing request to update product with PartitionKey: Product, RowKey: {productId}");

            // Read form data
            var formData = await req.ReadFormAsync();
            var name = formData["name"];
            var description = formData["description"];
            var priceString = formData["price"];
            var price = int.Parse(priceString);

            // Retrieve the existing product
            var existingProduct = await _productTableService.GetEntityAsync(PARTITION_KEY, productId);
            if (existingProduct == null)
            {
                log.LogWarning($"Product with RowKey {productId} not found.");
                return new NotFoundObjectResult(new { success = false, message = "Product not found" });
            }

            // Update only provided fields
            if (!string.IsNullOrEmpty(name)) existingProduct.Name = name;
            if (!string.IsNullOrEmpty(description)) existingProduct.Description = description;
            if (price > 0) existingProduct.Price = price;

            // Handle optional image upload
            if (formData.Files.Count > 0)
            {
                var imageFile = formData.Files.First();
                using var imageStream = imageFile.OpenReadStream();
                var blobName = await _blobStorageService.UploadImageAsync(imageStream, imageFile.FileName);
                existingProduct.ImageUrl = blobName;
            }

         
            // Save updated product
            await _productTableService.UpsertEntityAsync(existingProduct);

            // Create DTO
            var updatedProductDto = new ProductDTO
            {
                PartitionKey = existingProduct.PartitionKey,
                RowKey = existingProduct.RowKey,
                Timestamp = existingProduct.Timestamp,
                ETag = existingProduct.ETag.ToString(),
                Name = existingProduct.Name,
                Description = existingProduct.Description,
                Price = existingProduct.Price,
                ImageUrl = existingProduct.ImageUrl
            };

            // Audit log
            var audit = new AuditLog
            {
                TableName = "Products",
                Action = "Update",
                DataSnapshot = JsonConvert.SerializeObject(updatedProductDto),
                Timestamp = DateTime.UtcNow
            };
            await _queueStorageService.SendLogEntryAsync(audit);

            log.LogInformation($"Product {productId} updated successfully.");
            return new OkObjectResult(updatedProductDto);
        }

        [FunctionName("DeleteProduct")]
        public async Task<IActionResult> DeleteProduct(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "products/{productId}")] HttpRequest req,
            string productId,
            ILogger log)
        {
            log.LogInformation($"Processing request to delete product with PartitionKey: Product, RowKey: {productId}");

            if (string.IsNullOrWhiteSpace(productId))
            {
                return new BadRequestObjectResult(new { success = false, message = "Product ID is required" });
            }

            // Retrieve the existing product
            var existingProduct = await _productTableService.GetEntityAsync("Product", productId);
            if (existingProduct == null)
            {
                log.LogWarning($"Product with RowKey {productId} not found.");
                return new NotFoundObjectResult(new { success = false, message = "Product not found" });
            }

            if (!string.IsNullOrEmpty(existingProduct.ImageUrl))
            {
                await _blobStorageService.DeleteBlobAsync(existingProduct.ImageUrl);
            }

            // Delete the product
            await _productTableService.DeleteEntityAsync(existingProduct.PartitionKey, existingProduct.RowKey);

            // Send audit log
            var audit = new AuditLog
            {
                TableName = "Products",
                Action = "Deleted",
                DataSnapshot = JsonConvert.SerializeObject(new
                {
                    ProductId = existingProduct.RowKey,
                    existingProduct.Name,
                    existingProduct.Price
                }),
                Timestamp = DateTime.UtcNow
            };
            await _queueStorageService.SendLogEntryAsync(audit);

            log.LogInformation($"Product {productId} deleted successfully.");

            return new OkObjectResult(new
            {
                success = true,
                message = "Product deleted successfully",
                deletedProductId = productId
            });
        }
    }
}
