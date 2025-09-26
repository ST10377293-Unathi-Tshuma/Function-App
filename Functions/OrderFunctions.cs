using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Part2FunctionApp.Models;
using Part2FunctionApp.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Part2FunctionApp.Functions
{
    public class OrderFunctions
    {
        private readonly TableStorageService<Order> _orderTableService;
        private readonly TableStorageService<Customer> _customerTableService;
        private readonly TableStorageService<Product> _productTableService;
        private readonly BlobStorageService _blobStorageService;
        private readonly QueueStorageService _queueStorageService;
        private readonly AuthService authService;

        public OrderFunctions(
            TableStorageService<Order> orderTableService,
            TableStorageService<Customer> customerTableService,
            TableStorageService<Product> productTableService,
            BlobStorageService blobStorageService,
            QueueStorageService queueStorageService)
        {
            _orderTableService = orderTableService;
            _customerTableService = customerTableService;
            _productTableService = productTableService;
            _blobStorageService = blobStorageService;
            _queueStorageService = queueStorageService;
        }

        [FunctionName("GetOrders")]
        public async Task<IActionResult> GetOrders(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Retrieving all orders");

            try
            {
                var orders = await _orderTableService.GetAllEntities();

                if (orders == null || !orders.Any())
                {
                    log.LogInformation("No orders found.");
                    return new OkObjectResult(new { Message = "No orders found.", Orders = new OrderViewModel[0] });
                }

                var orderViewModels = new List<OrderViewModel>();

                foreach (var order in orders)
                {
                    var customer = await _customerTableService.GetEntityAsync("Customer", order.CustomerId);
                    var product = await _productTableService.GetEntityAsync("Product", order.ProductId);

                    var orderViewModel = new OrderViewModel
                    {
                        PartitionKey = order.PartitionKey,
                        RowKey = order.RowKey,
                        CustomerName = customer?.FullName ?? "Unknown Customer",
                        ProductName = product?.Name ?? "Unknown Product",
                        ProductImageUrl = !string.IsNullOrEmpty(product?.ImageUrl) ?
                            _blobStorageService.GetBlobSasUrl(product.ImageUrl) : "",
                        Status = order.Status
                    };

                    orderViewModels.Add(orderViewModel);
                }

                return new OkObjectResult(orderViewModels);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error retrieving orders");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("GetOrder")]
        public async Task<IActionResult> GetOrder(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders/{orderId}")] HttpRequest req,
            ILogger log,
            string orderId)
        {
            log.LogInformation("Processing request to retrieve order with ID: {orderId}", orderId);

            if (string.IsNullOrWhiteSpace(orderId))
            {
                log.LogWarning("Order ID is missing or empty.");
                return new BadRequestObjectResult("Order ID is required.");
            }

            try
            {
                var order = await _orderTableService.GetEntityAsync("Order", orderId);

                if (order == null)
                {
                    log.LogWarning("Order with ID {orderId} not found.", orderId);
                    return new NotFoundObjectResult($"Order with ID {orderId} not found.");
                }

                // Get related customer and product information
                var customer = await _customerTableService.GetEntityAsync("Customer", order.CustomerId);
                var product = await _productTableService.GetEntityAsync("Product", order.ProductId);

                var orderViewModel = new OrderViewModel
                {
                    PartitionKey = order.PartitionKey,
                    RowKey = order.RowKey,
                    CustomerName = customer?.FullName ?? "Unknown Customer",
                    ProductName = product?.Name ?? "Unknown Product",
                    ProductImageUrl = !string.IsNullOrEmpty(product?.ImageUrl) ?
                        _blobStorageService.GetBlobSasUrl(product.ImageUrl) : "",
                    Status = order.Status
                };

                return new OkObjectResult(orderViewModel);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error retrieving order with ID {orderId}.", orderId);
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("CreateOrder")]
        public async Task<IActionResult> CreateOrder(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "orders")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Processing order creation request");

            try
            {
                var form = await req.ReadFormAsync();
                var customerId = form["customerId"];
                var productId = form["productId"];

                if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(productId))
                {
                    return new BadRequestObjectResult("CustomerId and ProductId are required.");
                }

                // Verify customer exists
                var customer = await _customerTableService.GetEntityAsync("Customer", customerId);
                if (customer == null)
                {
                    log.LogWarning("Customer with ID {customerId} not found.", customerId);
                    return new BadRequestObjectResult($"Customer with ID {customerId} not found.");
                }

                // Verify product exists
                var product = await _productTableService.GetEntityAsync("Product", productId);
                if (product == null)
                {
                    log.LogWarning("Product with ID {productId} not found.", productId);
                    return new BadRequestObjectResult($"Product with ID {productId} not found.");
                }

                // Create order
                var order = new Order
                {
                    PartitionKey = "Order",
                    RowKey = Guid.NewGuid().ToString(),
                    CustomerId = customerId,
                    ProductId = productId,
                    Status = "Processing"
                };

                log.LogInformation($"Creating order with PartitionKey: {order.PartitionKey}, RowKey: {order.RowKey}");

                // Save order to table storage
                await _orderTableService.UpsertEntityAsync(order);

                // Send audit log to queue
                var auditLog = new AuditLog
                {
                    TableName = "Orders",
                    Action = "Create",
                    DataSnapshot = JsonConvert.SerializeObject(new
                    {
                        OrderId = order.RowKey,
                        CustomerId = order.CustomerId,
                        CustomerName = customer.FullName,
                        ProductId = order.ProductId,
                        ProductName = product.Name,
                        Status = order.Status
                    })
                };
                await _queueStorageService.SendLogEntryAsync(auditLog);

                return new OkObjectResult(new
                {
                    success = true,
                    message = "Order created successfully",
                    orderId = order.RowKey,
                    customerId = order.CustomerId,
                    productId = order.ProductId,
                    status = order.Status
                });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error creating order");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("UpdateOrderStatus")]
        public async Task<IActionResult> UpdateOrderStatus(
    [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "orders/{orderId}/status")] HttpRequest req,
    string orderId,
    ILogger log)
        {
            log.LogInformation($"Processing request to update order status for OrderId: {orderId}");

            if (string.IsNullOrWhiteSpace(orderId))
            {
                return new BadRequestObjectResult("Order ID is required.");
            }

            try
            {
                var form = await req.ReadFormAsync();
                var status = form["status"].ToString();
                var updatedBy = string.IsNullOrWhiteSpace(form["updatedBy"]) ? "System" : form["updatedBy"].ToString();

                if (string.IsNullOrWhiteSpace(status))
                {
                    return new BadRequestObjectResult("Status is required.");
                }

                // Validate status values
                var validStatuses = new[] { "Processing", "Confirmed", "Shipped", "Delivered", "Cancelled" };
                if (!validStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
                {
                    return new BadRequestObjectResult($"Invalid status. Valid statuses: {string.Join(", ", validStatuses)}");
                }

                // Retrieve existing order
                var existingOrder = await _orderTableService.GetEntityAsync("Order", orderId);
                if (existingOrder == null)
                {
                    log.LogWarning($"Order with RowKey {orderId} not found.");
                    return new NotFoundObjectResult("Order not found.");
                }

                var oldStatus = existingOrder.Status;
                existingOrder.Status = status;

                // Update order in table storage
                await _orderTableService.UpsertEntityAsync(existingOrder);

                // Send audit log to queue
                var auditLog = new AuditLog
                {
                    TableName = "Orders",
                    Action = "StatusUpdate",
                    DataSnapshot = JsonConvert.SerializeObject(new
                    {
                        OrderId = orderId,
                        OldStatus = oldStatus,
                        NewStatus = status,
                        UpdatedBy = updatedBy
                    })
                };
                await _queueStorageService.SendLogEntryAsync(auditLog);

                log.LogInformation($"Order {orderId} status updated from {oldStatus} to {status}");

                return new OkObjectResult(new
                {
                    success = true,
                    message = "Order status updated successfully",
                    orderId,
                    oldStatus,
                    newStatus = status
                });
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Error updating order status for OrderId: {orderId}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }


        [FunctionName("CancelOrder")]
        public async Task<IActionResult> CancelOrder(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "orders/{orderId}")] HttpRequest req,
            string orderId,
            ILogger log)
        {
            log.LogInformation($"Processing request to cancel order with OrderId: {orderId}");

            if (string.IsNullOrWhiteSpace(orderId))
            {
                return new BadRequestObjectResult("Order ID is required.");
            }

            try
            {
                // Retrieve existing order
                var existingOrder = await _orderTableService.GetEntityAsync("Order", orderId);
                if (existingOrder == null)
                {
                    log.LogWarning($"Order with RowKey {orderId} not found.");
                    return new NotFoundObjectResult("Order not found.");
                }

                // Check if order can be cancelled
                var nonCancellableStatuses = new[] { "Shipped", "Delivered" };
                if (nonCancellableStatuses.Contains(existingOrder.Status))
                {
                    return new BadRequestObjectResult($"Cannot cancel order with status: {existingOrder.Status}");
                }

                var oldStatus = existingOrder.Status;

                // Mark as cancelled (recommended for audit trail)
                existingOrder.Status = "Cancelled";
                await _orderTableService.UpsertEntityAsync(existingOrder);

                // Alternative: Actually delete the order (uncomment if preferred)
                // await _orderTableService.DeleteEntityAsync(existingOrder.PartitionKey, existingOrder.RowKey);

                // Send audit log to queue
                var auditLog = new AuditLog
                {
                    TableName = "Orders",
                    Action = "Cancelled", // or "Deleted" if using alternative approach
                    DataSnapshot = JsonConvert.SerializeObject(new
                    {
                        OrderId = orderId,
                        PreviousStatus = oldStatus,
                        CustomerId = existingOrder.CustomerId,
                        ProductId = existingOrder.ProductId
                    })
                };
                await _queueStorageService.SendLogEntryAsync(auditLog);

                log.LogInformation($"Order {orderId} cancelled successfully. Previous status: {oldStatus}");

                return new OkObjectResult(new
                {
                    success = true,
                    message = "Order cancelled successfully",
                    orderId = orderId,
                    previousStatus = oldStatus
                });
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Error cancelling order with OrderId: {orderId}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("GetCustomerOrders")]
        public async Task<IActionResult> GetCustomerOrders(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "customers/{customerId}/orders")] HttpRequest req,
            string customerId,
            ILogger log)
        {
            log.LogInformation($"Retrieving orders for customer: {customerId}");

            if (string.IsNullOrWhiteSpace(customerId))
            {
                return new BadRequestObjectResult("Customer ID is required.");
            }

            try
            {
                // Verify customer exists
                var customer = await _customerTableService.GetEntityAsync("Customer", customerId);
                if (customer == null)
                {
                    return new NotFoundObjectResult("Customer not found.");
                }

                // Get all orders for the customer
                var allOrders = await _orderTableService.GetAllEntities();
                var customerOrders = allOrders.Where(o => o.CustomerId == customerId).ToList();

                var orderViewModels = new List<OrderViewModel>();

                foreach (var order in customerOrders)
                {
                    var product = await _productTableService.GetEntityAsync("Product", order.ProductId);

                    var orderViewModel = new OrderViewModel
                    {
                        PartitionKey = order.PartitionKey,
                        RowKey = order.RowKey,
                        CustomerName = customer.FullName,
                        ProductName = product?.Name ?? "Unknown Product",
                        ProductImageUrl = !string.IsNullOrEmpty(product?.ImageUrl) ?
                            _blobStorageService.GetBlobSasUrl(product.ImageUrl) : "",
                        Status = order.Status
                    };

                    orderViewModels.Add(orderViewModel);
                }

                return new OkObjectResult(new
                {
                    customerId = customerId,
                    customerName = customer.FullName,
                    orders = orderViewModels,
                    totalOrders = orderViewModels.Count
                });
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Error retrieving orders for customer: {customerId}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // Queue-triggered function for processing orders asynchronously
        [FunctionName("ProcessOrderQueue")]
        public async Task ProcessOrderQueue([QueueTrigger("orders")] string queueMessage, ILogger log)
        {
            log.LogInformation($"Processing order queue message: {queueMessage}");

            try
            {
                var orderProcessingData = JsonConvert.DeserializeObject<OrderProcessingData>(queueMessage);

                if (orderProcessingData?.Order != null)
                {
                    var order = orderProcessingData.Order;

                    // Validate customer exists
                    var customer = await _customerTableService.GetEntityAsync("Customer", order.CustomerId);
                    if (customer == null)
                    {
                        log.LogError($"Customer {order.CustomerId} not found during order processing");
                        throw new InvalidOperationException($"Customer {order.CustomerId} not found");
                    }

                    // Validate product exists
                    var product = await _productTableService.GetEntityAsync("Product", order.ProductId);
                    if (product == null)
                    {
                        log.LogError($"Product {order.ProductId} not found during order processing");
                        throw new InvalidOperationException($"Product {order.ProductId} not found");
                    }

                    // Process the order (business logic here)
                    order.Status = "Confirmed";
                    await _orderTableService.UpsertEntityAsync(order);

                    // Log successful processing
                    var auditLog = new AuditLog
                    {
                        TableName = "Orders",
                        Action = "QueueProcessed",
                        DataSnapshot = JsonConvert.SerializeObject(new
                        {
                            OrderId = order.RowKey,
                            CustomerId = order.CustomerId,
                            ProductId = order.ProductId,
                            Status = order.Status,
                            CustomerName = customer.FullName,
                            ProductName = product.Name
                        })
                    };

                    await _queueStorageService.SendLogEntryAsync(auditLog);
                    log.LogInformation($"Successfully processed order: {order.RowKey}");
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Error processing order queue message: {queueMessage}");
                throw; // This will put the message in poison queue for manual review
            }
        }
    }

    // Supporting model classes
    public class CreateOrderRequest
    {
        public string CustomerId { get; set; }
        public string ProductId { get; set; }
    }

    public class UpdateOrderStatusRequest
    {
        public string Status { get; set; }
        public string UpdatedBy { get; set; }
    }

    public class OrderProcessingData
    {
        public Order Order { get; set; }
    }
}