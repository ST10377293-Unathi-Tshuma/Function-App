using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Part2FunctionApp.Models;
using Part2FunctionApp.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Part2FunctionApp.Functions
{
    public class ProductFunctions
    {
        private readonly TableStorageService<Product> _productTableService;
        private readonly BlobStorageService _blobStorageService;
        private readonly QueueStorageService _queueStorageService;

        public ProductFunctions(
            TableStorageService<Product> productTableService,
            BlobStorageService blobStorageService,
            QueueStorageService queueStorageService)
        {
            _productTableService = productTableService;
            _blobStorageService = blobStorageService;
            _queueStorageService = queueStorageService;
        }

        // PUBLIC - Get all products (no authentication required)
        [FunctionName("GetProducts")]
        public async Task<IActionResult> GetProducts(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "products")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Retrieving all products (public access)");

            try
            {
                var products = await _productTableService.GetAllEntities();

                if (products == null || !products.Any())
                {
                    log.LogInformation("No products found.");
                    return new OkObjectResult(new { Message = "No products found.", Products = new ProductDTO[0] });
                }

                var productDtos = products.Select(p => new ProductDTO
                {
                    PartitionKey = p.PartitionKey,
                    RowKey = p.RowKey,
                    Timestamp = p.Timestamp,
                    ETag = p.ETag.ToString(),
                    Name = p.Name,
                    Description = p.Description,
                    Price = p.Price,
                    ImageUrl = !string.IsNullOrEmpty(p.ImageUrl) ? _blobStorageService.GetBlobSasUrl(p.ImageUrl) : ""
                }).ToList();

                return new OkObjectResult(productDtos);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error retrieving products");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // PUBLIC - Get specific product (no authentication required)
        [FunctionName("GetProduct")]
        public async Task<IActionResult> GetProduct(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "products/{productId}")] HttpRequest req,
            ILogger log,
            string productId)
        {
            log.LogInformation("Processing request to retrieve product with ID: {productId} (public access)", productId);

            if (string.IsNullOrWhiteSpace(productId))
            {
                log.LogWarning("Product ID is missing or empty.");
                return new BadRequestObjectResult("Product ID is required.");
            }

            try
            {
                var product = await _productTableService.GetEntityAsync("Product", productId);

                if (product == null)
                {
                    log.LogWarning("Product with ID {productId} not found.", productId);
                    return new NotFoundObjectResult($"Product with ID {productId} not found.");
                }

                var productDto = new ProductDTO
                {
                    PartitionKey = product.PartitionKey,
                    RowKey = product.RowKey,
                    Timestamp = product.Timestamp,
                    ETag = product.ETag.ToString(),
                    Name = product.Name,
                    Description = product.Description,
                    Price = product.Price,
                    ImageUrl = !string.IsNullOrEmpty(product.ImageUrl) ? _blobStorageService.GetBlobSasUrl(product.ImageUrl) : ""
                };

                return new OkObjectResult(productDto);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error retrieving product with ID {productId}.", productId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // MANAGER+ ONLY - Create product
        [FunctionName("CreateProduct")]
        public async Task<IActionResult> CreateProduct(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "products")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Processing product creation request");

            try
            {
                // Read form data
                var formData = await req.ReadFormAsync();
                var partitionKey = "Product";
                var rowKey = Guid.NewGuid().ToString();

                var product = new Product
                {
                    PartitionKey = partitionKey,
                    RowKey = rowKey,
                    Name = formData["name"],
                    Description = formData["description"],
                    Price = int.TryParse(formData["price"], out var price) ? price : 0,
                };

                // Validation
                if (string.IsNullOrWhiteSpace(product.Name))
                {
                    return new BadRequestObjectResult("Product name is required");
                }

                if (product.Price <= 0)
                {
                    return new BadRequestObjectResult("Valid price is required");
                }

                log.LogInformation($"Creating product with partitionkey: {partitionKey}, rowkey: {rowKey}");

                // Handle image upload if provided
                var imageFile = formData.Files.FirstOrDefault();
                if (imageFile != null && imageFile.Length > 0)
                {
                    using var imageStream = imageFile.OpenReadStream();
                    var blobName = await _blobStorageService.UploadImageAsync(imageStream, imageFile.FileName);
                    product.ImageUrl = blobName;
                }

                await _productTableService.UpsertEntityAsync(product);

                // Enhanced audit log with user information
                var auditLog = new AuditLog
                {
                    TableName = "Products",
                    Action = "Create",
                    DataSnapshot = JsonConvert.SerializeObject(new
                    {
                        ProductId = product.RowKey,
                        product.Name,
                        product.Price,
                        HasImage = !string.IsNullOrEmpty(product.ImageUrl),
                    })
                };
                await _queueStorageService.SendLogEntryAsync(auditLog);


                return new OkObjectResult(new
                {
                    success = true,
                    message = "Product created successfully",
                    productId = product.RowKey,
                    productName = product.Name,
                    imageUrl = !string.IsNullOrEmpty(product.ImageUrl) ? _blobStorageService.GetBlobSasUrl(product.ImageUrl) : "",
                });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error creating product");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // MANAGER+ ONLY - Update product
        [FunctionName("UpdateProduct")]
        public async Task<IActionResult> UpdateProduct(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "products/{productId}")] HttpRequest req,
            string productId,
            ILogger log)
        {
            log.LogInformation($"Processing request to update product with ID: {productId}");

            try
            {
                // Read form data
                var formData = await req.ReadFormAsync();
                var name = formData["name"];
                var description = formData["description"];
                var priceString = formData["price"];

                // Retrieve the existing product
                var existingProduct = await _productTableService.GetEntityAsync("Product", productId);
                if (existingProduct == null)
                {
                    log.LogWarning($"Product with RowKey {productId} not found.");
                    return new NotFoundObjectResult(new { success = false, message = "Product not found" });
                }

                // Store old image URL for potential cleanup
                var oldImageUrl = existingProduct.ImageUrl;

                // Update only provided fields
                if (!string.IsNullOrEmpty(name)) existingProduct.Name = name;
                if (!string.IsNullOrEmpty(description)) existingProduct.Description = description;
                if (int.TryParse(priceString, out var price) && price > 0) existingProduct.Price = price;

                // Handle optional image upload
                if (formData.Files.Count > 0)
                {
                    // Delete old image if it exists
                    if (!string.IsNullOrEmpty(oldImageUrl))
                    {
                        await _blobStorageService.DeleteBlobAsync(oldImageUrl);
                    }

                    var imageFile = formData.Files.First();
                    using var imageStream = imageFile.OpenReadStream();
                    var blobName = await _blobStorageService.UploadImageAsync(imageStream, imageFile.FileName);
                    existingProduct.ImageUrl = blobName;
                }

                // Save updated product
                await _productTableService.UpsertEntityAsync(existingProduct);

                // Create DTO for response
                var updatedProductDto = new ProductDTO
                {
                    PartitionKey = existingProduct.PartitionKey,
                    RowKey = existingProduct.RowKey,
                    Timestamp = existingProduct.Timestamp,
                    ETag = existingProduct.ETag.ToString(),
                    Name = existingProduct.Name,
                    Description = existingProduct.Description,
                    Price = existingProduct.Price,
                    ImageUrl = !string.IsNullOrEmpty(existingProduct.ImageUrl) ? _blobStorageService.GetBlobSasUrl(existingProduct.ImageUrl) : ""
                };

                // Enhanced audit log
                var audit = new AuditLog
                {
                    TableName = "Products",
                    Action = "Update",
                    DataSnapshot = JsonConvert.SerializeObject(new
                    {
                        ProductId = productId,
                        UpdatedFields = new
                        {
                            Name = !string.IsNullOrEmpty(name),
                            Description = !string.IsNullOrEmpty(description),
                            Price = int.TryParse(priceString, out _),
                            ImageUpdated = formData.Files.Count > 0
                        },
                    }),
                    Timestamp = DateTime.UtcNow
                };
                await _queueStorageService.SendLogEntryAsync(audit);


                return new OkObjectResult(new
                {
                    success = true,
                    message = "Product updated successfully",
                    product = updatedProductDto,
                });
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Error updating product {productId}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("DeleteProduct")]
        public async Task<IActionResult> DeleteProduct(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "products/{productId}")] HttpRequest req,
            string productId,
            ILogger log)
        {
            log.LogInformation($"Processing request to delete product with ID: {productId}");

            if (string.IsNullOrWhiteSpace(productId))
            {
                return new BadRequestObjectResult(new { success = false, message = "Product ID is required" });
            }

            try
            {
                // Retrieve the existing product
                var existingProduct = await _productTableService.GetEntityAsync("Product", productId);
                if (existingProduct == null)
                {
                    log.LogWarning($"Product with RowKey {productId} not found.");
                    return new NotFoundObjectResult(new { success = false, message = "Product not found" });
                }

                // Delete associated image from blob storage
                if (!string.IsNullOrEmpty(existingProduct.ImageUrl))
                {
                    await _blobStorageService.DeleteBlobAsync(existingProduct.ImageUrl);
                }

                // Delete the product
                await _productTableService.DeleteEntityAsync(existingProduct.PartitionKey, existingProduct.RowKey);

                // Enhanced audit log
                var audit = new AuditLog
                {
                    TableName = "Products",
                    Action = "Deleted",
                    DataSnapshot = JsonConvert.SerializeObject(new
                    {
                        ProductId = existingProduct.RowKey,
                        ProductName = existingProduct.Name,
                        Price = existingProduct.Price,
                        ImageDeleted = !string.IsNullOrEmpty(existingProduct.ImageUrl),
                    }),
                    Timestamp = DateTime.UtcNow
                };
                await _queueStorageService.SendLogEntryAsync(audit);

                return new OkObjectResult(new
                {
                    success = true,
                    message = "Product deleted successfully",
                    deletedProductId = productId,
                    productName = existingProduct.Name,
                });
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Error deleting product {productId}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
        
    }
}