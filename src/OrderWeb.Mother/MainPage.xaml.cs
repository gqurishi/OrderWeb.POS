using POS_in_NET.Services;

namespace POS_in_NET;

public partial class MainPage : ContentPage
{
	private readonly DatabaseService _databaseService;

	public MainPage()
	{
		InitializeComponent();
		_databaseService = new DatabaseService();
	}

	private async void OnTestDbClicked(object? sender, EventArgs e)
	{
		TestDbBtn.IsEnabled = false;
		TestDbBtn.Text = "Testing...";
		StatusLabel.Text = "Connecting to MariaDB...";

		try
		{
			// Test database connection
			var connectionStatus = await _databaseService.GetConnectionStatusAsync();
			
			if (connectionStatus.Contains("Connected"))
			{
				StatusLabel.Text = connectionStatus;

				var schemaResult = await _databaseService.EnsureProductionSchemaAsync();
				StatusLabel.Text += schemaResult.Success
					? $"\n{schemaResult.Message}"
					: $"\n{schemaResult.Message}";
				TestDbBtn.Text = schemaResult.Success ? "Schema Ready" : "Schema Check Failed";
			}
			else
			{
				StatusLabel.Text = connectionStatus;
				TestDbBtn.Text = "Connection Failed";
			}
		}
		catch (Exception ex)
		{
			StatusLabel.Text = $"Error: {ex.Message}";
			TestDbBtn.Text = "Error Occurred";
		}
		finally
		{
			TestDbBtn.IsEnabled = true;
		}
	}
}
