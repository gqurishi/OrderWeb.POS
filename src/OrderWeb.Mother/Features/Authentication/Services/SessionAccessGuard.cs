namespace POS_in_NET.Services;

/// <summary>
/// After idle/auto logout the session is empty. Pages that reappear must
/// return to login quietly — never show "Access Denied" for a logged-out user.
/// </summary>
public static class SessionAccessGuard
{
    public static async Task<bool> RequireSignedInAsync(AuthenticationService? authService)
    {
        if (authService?.CurrentUser != null)
        {
            return true;
        }

        try
        {
            if (Shell.Current != null)
            {
                await NavigationCoordinator.Shared.NavigateShellAsync("login", animated: false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Silent login redirect failed: {ex.Message}");
        }

        return false;
    }
}
