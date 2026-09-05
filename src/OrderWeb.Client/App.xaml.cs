using OrderWeb.SharedUI.Themes;

namespace OrderWeb.Client;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		DesignSystemBootstrap.LockHostResources(Resources);
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell())
		{
			Title = "Restaurant POS"
		};
	}
}
