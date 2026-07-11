namespace POS_in_NET.Services;

public static class ServiceHelper
{
    public static IServiceProvider? Services
    {
        get
        {
            try
            {
                return Application.Current?.Handler?.MauiContext?.Services;
            }
            catch
            {
                return null;
            }
        }
    }

    public static T? GetService<T>() where T : class
    {
        try
        {
            return Services?.GetService(typeof(T)) as T;
        }
        catch
        {
            return null;
        }
    }
}
