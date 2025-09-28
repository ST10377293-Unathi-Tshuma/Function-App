using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Part1.Services;
using Part2FunctionApp.Models;
using Part2FunctionApp.Services;
using System;

[assembly: FunctionsStartup(typeof(Part2FunctionApp.Startup))]

namespace Part2FunctionApp
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            var configuration = builder.GetContext().Configuration;
            builder.Services.AddSingleton<BlobStorageService>();

            builder.Services.AddSingleton<TableStorageService<Customer>>(provider =>
            {
                var configuration = provider.GetRequiredService<IConfiguration>();
                var connectionString = configuration["AzureStorageAccount:ConnectionString"];
                if (string.IsNullOrEmpty(connectionString))
                    throw new InvalidOperationException("AzureStorageAccount:ConnectionString is not configured.");

                return new TableStorageService<Customer>(connectionString, "Customers");
            });

            builder.Services.AddSingleton<FileShareStorageService>();

            builder.Services.AddSingleton<TableStorageService<Product>>(provider =>
            {
                var configuration = provider.GetRequiredService<IConfiguration>();
                var connectionString = configuration["AzureStorageAccount:ConnectionString"];
                return new TableStorageService<Product>(connectionString, "Products");
            });

            builder.Services.AddSingleton<TableStorageService<Order>>(provider =>
            {
                var configuration = provider.GetRequiredService<IConfiguration>();
                var connectionString = configuration["AzureStorageAccount:ConnectionString"];
                return new TableStorageService<Order>(connectionString, "Orders");
            });

            builder.Services.AddSingleton(sp =>
            {
                string connectionString = Environment.GetEnvironmentVariable("AzureStorageAccount:ConnectionString");
                string queueName = "audit-logs";
                return new QueueStorageService(connectionString, queueName);
            });

            builder.Services.AddSingleton<TableStorageService<User>>(provider =>
            {
                var configuration = provider.GetRequiredService<IConfiguration>();
                string connectionString = configuration["AzureStorageAccount:ConnectionString"];
                return new TableStorageService<User>(connectionString, "Users");
            });

            // File Share
            builder.Services.AddSingleton<FileShareStorageService>();
        }
    }
}
