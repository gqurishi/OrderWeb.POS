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
				
				// Create database and tables if they don't exist
				await _databaseService.CreateDatabaseIfNotExistsAsync();
				var tablesCreated = await _databaseService.CreateTablesAsync();
				
				if (tablesCreated)
				{
					StatusLabel.Text += "\n Database and tables ready!";
					TestDbBtn.Text = "Connection Successful!";
				}
				else
				{
					StatusLabel.Text += "\n Failed to create tables";
					TestDbBtn.Text = "Tables Creation Failed";
				}
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
