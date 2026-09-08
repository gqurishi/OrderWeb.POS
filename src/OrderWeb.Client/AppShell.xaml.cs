using OrderWeb.Client.Pages.Orders;

namespace OrderWeb.Client;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		// Mirror Mother temporary Collection/Delivery routes (full-page, no shell chrome).
		Routing.RegisterRoute("collection", typeof(CollectionOrderPage));
		Routing.RegisterRoute("delivery", typeof(DeliveryOrderPage));
	}
}
