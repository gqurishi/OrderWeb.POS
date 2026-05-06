using System;

namespace POS_in_NET.Services
{
    public static class AppDataRefreshService
    {
        public static event EventHandler? RefreshRequested;

        public static void RequestRefresh()
        {
            RefreshRequested?.Invoke(null, EventArgs.Empty);
        }
    }
}