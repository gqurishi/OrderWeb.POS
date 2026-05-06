using Microsoft.Maui.Controls;
using POS_in_NET.Models;
using System;

namespace POS_in_NET.Views
{
    public partial class ElegantRolePickerModal : ContentPage
    {
        public event EventHandler<UserRole> RoleSelected;
        
        public ElegantRolePickerModal()
        {
            InitializeComponent();
        }
        
        private async void OnRoleSelected(object sender, EventArgs e)
        {
            try
            {
                if (sender is Button button)
                {
                    string role = button.Text;
                    if (Enum.TryParse<UserRole>(role, true, out UserRole userRole))
                    {
                        RoleSelected?.Invoke(this, userRole);
                        await Navigation.PopModalAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in role selection: {ex.Message}");
                // Still try to close modal
                try { await Navigation.PopModalAsync(); } catch { }
            }
        }
        
        private async void OnCancelClicked(object sender, EventArgs e)
        {
            try
            {
                await Navigation.PopModalAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error closing modal: {ex.Message}");
            }
        }
    }
}