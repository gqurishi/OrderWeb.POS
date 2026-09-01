namespace OrderWeb.Client.Pages.Pos;

public partial class RestaurantPage : ContentPage
{
    public RestaurantPage()
    {
        InitializeComponent();

        var tableLayoutPage = new TableLayoutPage();
        Root.Children.Add(tableLayoutPage.Content);
    }
}
