using Microsoft.Maui.Controls;
using POS_in_NET.Models;
using System;

namespace POS_in_NET.Views
{
    public partial class RoleSelectionOverlay : ContentView
    {
        public event EventHandler<UserRole> RoleSelected;
        public event EventHandler OverlayClosed;
        
        public RoleSelectionOverlay()
        {
            InitializeComponent();
        }
        
        public void ShowOverlay()
        {
            IsVisible = true;
            System.Diagnostics.Debug.WriteLine("Role selection overlay shown");
        }
        
        public void HideOverlay()
        {
            IsVisible = false;
            System.Diagnostics.Debug.WriteLine("Role selection overlay hidden");
        }
        
        private void OnUserSelected(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("User role selected");
            RoleSelected?.Invoke(this, UserRole.User);
            HideOverlay();
        }
        
        private void OnManagerSelected(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Manager role selected");
            RoleSelected?.Invoke(this, UserRole.Manager);
            HideOverlay();
        }
        
        private void OnAdminSelected(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Admin role selected");
            RoleSelected?.Invoke(this, UserRole.Admin);
            HideOverlay();
        }
        
        private void OnCancelClicked(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Role selection cancelled");
            OverlayClosed?.Invoke(this, EventArgs.Empty);
            HideOverlay();
        }
        
        private void OnOverlayTapped(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Overlay background tapped - closing");
            OverlayClosed?.Invoke(this, EventArgs.Empty);
            HideOverlay();
        }
    }
}