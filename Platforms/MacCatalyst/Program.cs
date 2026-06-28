using ObjCRuntime;
using POS_in_NET.Services;
using UIKit;

namespace POS_in_NET;

public class Program
{
	static void Main(string[] args)
	{
		try
		{
			AppDiagnostics.Log("=== PROGRAM MAIN ===");
			UIApplication.Main(args, null, typeof(AppDelegate));
		}
		catch (Exception ex)
		{
			AppDiagnostics.LogFatal("ProgramMain", ex);
			throw;
		}
	}
}
