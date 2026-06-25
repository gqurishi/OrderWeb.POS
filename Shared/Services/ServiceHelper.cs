namespace POS_in_NET.Services;

public static class ServiceHelper
{
    public static T? GetService<T>() where T : class
    {
        try
        {
            var services = Application.Current?.Handler?.MauiContext?.Services;
            return services?.GetService(typeof(T)) as T;
        }
        catch
        {
            return null;
        }
    }
}
