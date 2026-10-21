using Hangfire.Dashboard;

namespace Africanideasfaceapi.Configuration;

/// <summary>
/// Authorization filter for Hangfire dashboard - restricts access to admin users only
/// </summary>
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        
        // Check if user is authenticated and has admin role
        var isAuthenticated = httpContext.Session.GetString("UserId") != null;
        var isAdmin = httpContext.Session.GetString("UserRole") == "Admin";
        
        return isAuthenticated && isAdmin;
    }
}
