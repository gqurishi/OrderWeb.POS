using OrderWeb.SharedUI.Controls;
using POS_in_NET.Models;

namespace POS_in_NET.Views;

public partial class RoleSelectionOverlay : ContentView
{
    public event EventHandler<UserRole>? RoleSelected;
    public event EventHandler? OverlayClosed;

    public RoleSelectionOverlay()
    {
        InitializeComponent();
        RoleView.ShowDefaultPosRoles();
    }

    public void ShowOverlay()
    {
        IsVisible = true;
        RoleView.ShowDefaultPosRoles();
    }

    public void HideOverlay()
    {
        IsVisible = false;
    }

    private void OnSharedRoleSelected(object? sender, RoleOption option)
    {
        var role = option.Key switch
        {
            "Staff" => UserRole.Staff,
            "User" => UserRole.User,
            "Manager" => UserRole.Manager,
            "Admin" => UserRole.Admin,
            _ => UserRole.User
        };
        RoleSelected?.Invoke(this, role);
        HideOverlay();
    }

    private void OnSharedDismissed(object? sender, EventArgs e)
    {
        OverlayClosed?.Invoke(this, EventArgs.Empty);
        HideOverlay();
    }
}
