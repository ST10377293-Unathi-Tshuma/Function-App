using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Part2FunctionApp.Models;
using Part2FunctionApp.Services;

namespace Part2FunctionApp.Helpers
{
    public static class AuthorizationHelper
    {
        // Role hierarchy: Admin > Manager > Customer
        private static readonly Dictionary<string, int> RoleHierarchy = new()
        {
            { "Admin", 3 },
            { "Manager", 2 },
            { "Customer", 1 }
        };

        // Check if user has required role or higher
        public static bool HasRequiredRole(string userRole, string requiredRole)
        {
            if (string.IsNullOrEmpty(userRole) || string.IsNullOrEmpty(requiredRole))
                return false;

            var userLevel = RoleHierarchy.GetValueOrDefault(userRole, 0);
            var requiredLevel = RoleHierarchy.GetValueOrDefault(requiredRole, 0);

            return userLevel >= requiredLevel;
        }

        // Authorize request and return user info
        public static (bool authorized, TokenPayload user, IActionResult errorResult) AuthorizeRequest(
            HttpRequest req,
            AuthService authService,
            string requiredRole,
            ILogger log,
            string customerId = null) // For customer-specific operations
        {
            // Extract token from Authorization header
            var authHeader = req.Headers["Authorization"].FirstOrDefault();
            var token = authService.ExtractTokenFromHeader(authHeader);

            if (string.IsNullOrEmpty(token))
            {
                log.LogWarning("Missing or invalid authorization token");
                return (false, null, new UnauthorizedObjectResult("Authorization token required"));
            }

            // Validate token
            var userPayload = authService.ValidateJwtToken(token);
            if (userPayload == null)
            {
                log.LogWarning("Invalid or expired token");
                return (false, null, new UnauthorizedObjectResult("Invalid or expired token"));
            }

            // Check if token is expired
            if (userPayload.ExpiresAt < DateTime.UtcNow)
            {
                log.LogWarning("Token has expired");
                return (false, null, new UnauthorizedObjectResult("Token has expired"));
            }

            // Check role authorization
            if (!HasRequiredRole(userPayload.Role, requiredRole))
            {
                log.LogWarning($"User {userPayload.Username} with role {userPayload.Role} attempted to access {requiredRole} resource");
                return (false, null, new ForbidResult($"Insufficient privileges. Required role: {requiredRole}"));
            }

            // Special case: Customer can only access their own data
            if (userPayload.Role == "Customer" && !string.IsNullOrEmpty(customerId))
            {
                if (userPayload.UserId != customerId)
                {
                    log.LogWarning($"Customer {userPayload.UserId} attempted to access data for customer {customerId}");
                    return (false, null, new ForbidResult("You can only access your own data"));
                }
            }

            return (true, userPayload, null);
        }
    }

    // Custom attribute for role requirements (for documentation)
    [AttributeUsage(AttributeTargets.Method)]
    public class RequireRoleAttribute : Attribute
    {
        public string Role { get; }
        public RequireRoleAttribute(string role) => Role = role;
    }
}
