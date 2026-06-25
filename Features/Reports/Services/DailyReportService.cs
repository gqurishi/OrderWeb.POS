using MySqlConnector;
using Syncfusion.Drawing;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using System.Globalization;
using System.Text;
using POS_in_NET.Models;
using PdfColor = Syncfusion.Drawing.Color;
using PdfPointF = Syncfusion.Drawing.PointF;

namespace POS_in_NET.Services;

public enum ReportDatePreset
{
	Today,
	Last7Days,
	Last30Days,
	Custom
}

public enum ReportSourceFilter
{
	All,
	Local,
	Web
}

public enum ReportOrderTypeFilter
{
	All,
	Pickup,
	Delivery,
	Table
}

public enum TopSellSection
{
	Food,
	Drink
}

public sealed class ReportSummary
{
	public int OrderCount { get; set; }
	public decimal GrossSales { get; set; }
	public decimal NetSales { get; set; }
	public decimal VatAmount { get; set; }
	public decimal DeliveryChargeTotal { get; set; }
	public decimal AverageOrderValue { get; set; }
}

public sealed class ReportOrderRow
{
	public int OrderDbId { get; set; }
	public string OrderId { get; set; } = string.Empty;
	public string OrderNumber { get; set; } = string.Empty;
	public DateTime CreatedAt { get; set; }
	public string SourceChannel { get; set; } = string.Empty;
	public string OrderType { get; set; } = string.Empty;
	public string Status { get; set; } = string.Empty;
	public string CustomerName { get; set; } = string.Empty;
	public string CustomerPhone { get; set; } = string.Empty;
	public int ItemCount { get; set; }
	public decimal GrossSales { get; set; }
	public decimal NetSales { get; set; }
	public decimal VatAmount { get; set; }

	public string CreatedAtDisplay => CreatedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);
	public string AmountDisplay => $"£{GrossSales:F2}";
	public string OrderMeta => $"{SourceChannel} · {OrderType} · {Status}";
}

public sealed class ReportOrderAddonRow
{
	public string AddonName { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public decimal AddonPrice { get; set; }

	public decimal TotalPrice => AddonPrice * Quantity;
	public string DisplayText => Quantity > 1
		? $"{Quantity}x {AddonName} (+£{TotalPrice:F2})"
		: $"{AddonName} (+£{TotalPrice:F2})";
}

public sealed class ReportOrderLineDetail
{
	public int OrderItemId { get; set; }
	public string ItemName { get; set; } = string.Empty;
	public int Quantity { get; set; }
	public decimal UnitPrice { get; set; }
	public decimal AddonUnitTotal { get; set; }
	public decimal LineGross { get; set; }
	public decimal LineNet { get; set; }
	public decimal LineVat { get; set; }
	public string SpecialInstructions { get; set; } = string.Empty;
	public string AddonSummary { get; set; } = string.Empty;
	public List<ReportOrderAddonRow> Addons { get; set; } = new();

	public string QuantityDisplay => $"{Quantity}x";
	public string UnitPriceDisplay => $"£{UnitPrice:F2}";
	public string LineTotalDisplay => $"£{LineGross:F2}";
	public string LineVatDisplay => $"£{LineVat:F2}";
	public bool HasAddons => Addons.Count > 0 || !string.IsNullOrWhiteSpace(AddonSummary);
	public bool HasInstructions => !string.IsNullOrWhiteSpace(SpecialInstructions);
}

public sealed class ReportOrderDetail
{
	public int OrderDbId { get; set; }
	public string OrderId { get; set; } = string.Empty;
	public string OrderNumber { get; set; } = string.Empty;
	public string CloudOrderId { get; set; } = string.Empty;
	public DateTime CreatedAt { get; set; }
	public string SourceChannel { get; set; } = string.Empty;
	public string OrderType { get; set; } = string.Empty;
	public string Status { get; set; } = string.Empty;
	public string CustomerName { get; set; } = string.Empty;
	public string CustomerPhone { get; set; } = string.Empty;
	public string CustomerEmail { get; set; } = string.Empty;
	public string CustomerAddress { get; set; } = string.Empty;
	public string PaymentMethod { get; set; } = string.Empty;
	public string SpecialInstructions { get; set; } = string.Empty;
	public decimal SubtotalAmount { get; set; }
	public decimal DeliveryFee { get; set; }
	public decimal TaxAmount { get; set; }
	public decimal TotalAmount { get; set; }
	public List<ReportOrderLineDetail> Lines { get; set; } = new();

	public string CreatedAtDisplay => CreatedAt.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);
	public string TotalDisplay => $"£{TotalAmount:F2}";
	public string SubtotalDisplay => $"£{SubtotalAmount:F2}";
	public string DeliveryFeeDisplay => $"£{DeliveryFee:F2}";
	public string TaxDisplay => $"£{TaxAmount:F2}";
	public bool HasCustomerDetails => !string.IsNullOrWhiteSpace(CustomerName) || !string.IsNullOrWhiteSpace(CustomerPhone) || !string.IsNullOrWhiteSpace(CustomerEmail) || !string.IsNullOrWhiteSpace(CustomerAddress);
}

public sealed class ReportTopItemRow
{
	public string CategoryName { get; set; } = string.Empty;
	public string ItemName { get; set; } = string.Empty;
	public int TotalQuantity { get; set; }
	public decimal GrossSales { get; set; }
	public decimal NetSales { get; set; }
	public decimal VatAmount { get; set; }

	public string GrossDisplay => $"£{GrossSales:F2}";
	public string NetDisplay => $"£{NetSales:F2}";
	public string VatDisplay => $"£{VatAmount:F2}";
}

public sealed class ReportDailyTrendRow
{
	public DateTime BusinessDate { get; set; }
	public int OrderCount { get; set; }
	public decimal GrossSales { get; set; }
	public decimal NetSales { get; set; }
	public decimal VatAmount { get; set; }
}

public sealed class DailyReportSnapshot
{
	public DateTime StartDate { get; set; }
	public DateTime EndDate { get; set; }
	public string SearchText { get; set; } = string.Empty;
	public ReportSourceFilter SourceFilter { get; set; } = ReportSourceFilter.All;
	public ReportOrderTypeFilter OrderTypeFilter { get; set; } = ReportOrderTypeFilter.All;
	public ReportSummary Summary { get; set; } = new();
	public List<ReportOrderRow> Orders { get; set; } = new();
	public List<ReportTopItemRow> TopItems { get; set; } = new();
}

public sealed class DailyReportService
{
	private readonly DatabaseService _databaseService;
	private readonly OrderService _orderService;

	public DailyReportService(DatabaseService databaseService)
	{
		_databaseService = databaseService;
		_orderService = new OrderService();
	}

	public async Task<OperationalAnalyticsSnapshot> GetOperationalAnalyticsAsync(DateTime startDate, DateTime endDate)
	{
		var normalizedStart = startDate.Date;
		var normalizedEndExclusive = endDate.Date.AddDays(1);
		return await _orderService.GetOperationalAnalyticsAsync(normalizedStart, normalizedEndExclusive);
	}

	public async Task<List<ReportDailyTrendRow>> GetDailyTrendAsync(
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter = ReportSourceFilter.All,
		ReportOrderTypeFilter orderTypeFilter = ReportOrderTypeFilter.All)
	{
		if (startDate.Date == endDate.Date)
		{
			return await GetHourlyTrendAsync(startDate, endDate, sourceFilter, orderTypeFilter);
		}

		return await GetDailyTrendInternalAsync(startDate, endDate, sourceFilter, orderTypeFilter, groupByHour: false);
	}

	private async Task<List<ReportDailyTrendRow>> GetHourlyTrendAsync(
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		return await GetDailyTrendInternalAsync(startDate, endDate, sourceFilter, orderTypeFilter, groupByHour: true);
	}

	private async Task<List<ReportDailyTrendRow>> GetDailyTrendInternalAsync(
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter,
		bool groupByHour)
	{
		var normalizedStart = startDate.Date;
		var normalizedEndExclusive = endDate.Date.AddDays(1);

		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();

		var bucketExpression = groupByHour
			? "DATE_FORMAT(created_at, '%Y-%m-%d %H:00:00')"
			: "business_date";

		var query = new StringBuilder($@"
			SELECT
				{bucketExpression} AS bucket,
				COUNT(DISTINCT order_db_id) AS order_count,
				COALESCE(SUM(line_gross), 0.00) AS gross_sales,
				COALESCE(SUM(line_net), 0.00) AS net_sales,
				COALESCE(SUM(line_vat), 0.00) AS vat_amount
			FROM vw_report_order_lines_live
			WHERE created_at >= @startDate
			  AND created_at < @endDate");

		if (sourceFilter != ReportSourceFilter.All)
		{
			query.Append(" AND source_channel = @sourceChannel");
		}

		if (orderTypeFilter != ReportOrderTypeFilter.All)
		{
			query.Append(" AND order_type = @orderType");
		}

		query.Append($@"
			GROUP BY bucket
			ORDER BY bucket");

		await using var command = new MySqlCommand(query.ToString(), connection);
		command.Parameters.AddWithValue("@startDate", normalizedStart);
		command.Parameters.AddWithValue("@endDate", normalizedEndExclusive);

		if (sourceFilter != ReportSourceFilter.All)
		{
			command.Parameters.AddWithValue("@sourceChannel", sourceFilter.ToString().ToLowerInvariant());
		}

		if (orderTypeFilter != ReportOrderTypeFilter.All)
		{
			command.Parameters.AddWithValue("@orderType", orderTypeFilter.ToString().ToLowerInvariant());
		}

		var rows = new List<ReportDailyTrendRow>();
		await using var reader = await command.ExecuteReaderAsync();
		while (await reader.ReadAsync())
		{
			var bucket = GetString(reader, "bucket");
			rows.Add(new ReportDailyTrendRow
			{
				BusinessDate = DateTime.TryParse(bucket, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
					? parsed
					: normalizedStart,
				OrderCount = GetInt32(reader, "order_count"),
				GrossSales = GetDecimal(reader, "gross_sales"),
				NetSales = GetDecimal(reader, "net_sales"),
				VatAmount = GetDecimal(reader, "vat_amount")
			});
		}

		return rows;
	}

	public async Task<DailyReportSnapshot> GetReportAsync(
		DateTime startDate,
		DateTime endDate,
		string? searchText = null,
		ReportSourceFilter sourceFilter = ReportSourceFilter.All,
		ReportOrderTypeFilter orderTypeFilter = ReportOrderTypeFilter.All)
	{
		var snapshot = new DailyReportSnapshot
		{
			StartDate = startDate.Date,
			EndDate = endDate.Date,
			SearchText = searchText?.Trim() ?? string.Empty,
			SourceFilter = sourceFilter,
			OrderTypeFilter = orderTypeFilter
		};

		var queryEndDate = snapshot.EndDate.AddDays(1);
		var summaryTask = LoadSummaryWithConnectionAsync(snapshot.StartDate, queryEndDate, sourceFilter, orderTypeFilter);
		var deliveryChargeTask = LoadDeliveryChargeTotalAsync(snapshot.StartDate, queryEndDate, sourceFilter, orderTypeFilter);
		var ordersTask = LoadOrdersWithConnectionAsync(snapshot.StartDate, queryEndDate, snapshot.SearchText, sourceFilter, orderTypeFilter);
		var topItemsTask = LoadTopItemsWithConnectionAsync(snapshot.StartDate, queryEndDate, sourceFilter, orderTypeFilter);

		await Task.WhenAll(summaryTask, deliveryChargeTask, ordersTask, topItemsTask);

		snapshot.Summary = await summaryTask;
		snapshot.Summary.DeliveryChargeTotal = await deliveryChargeTask;
		snapshot.Orders = await ordersTask;
		snapshot.TopItems = await topItemsTask;

		return snapshot;
	}

	private async Task<ReportSummary> LoadSummaryWithConnectionAsync(
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();
		return await LoadSummaryAsync(connection, startDate, endDate, sourceFilter, orderTypeFilter);
	}

	private async Task<decimal> LoadDeliveryChargeTotalAsync(
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();

		var query = new StringBuilder(@"
			SELECT COALESCE(SUM(delivery_fee), 0.00)
			FROM orders
			WHERE created_at >= @startDate
			  AND created_at < @endDate
			  AND COALESCE(status, '') NOT IN ('cancelled', 'voided')
			  AND COALESCE(local_lifecycle_state, '') <> 'voided'");

		AppendOptionalFilters(query, sourceFilter, orderTypeFilter);

		await using var command = new MySqlCommand(query.ToString(), connection);
		command.Parameters.AddWithValue("@startDate", startDate);
		command.Parameters.AddWithValue("@endDate", endDate);
		AddOptionalParameters(command, sourceFilter, orderTypeFilter);

		return Convert.ToDecimal(await command.ExecuteScalarAsync() ?? 0m, CultureInfo.InvariantCulture);
	}

	private async Task<List<ReportOrderRow>> LoadOrdersWithConnectionAsync(
		DateTime startDate,
		DateTime endDate,
		string searchText,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();
		return await LoadOrdersAsync(connection, startDate, endDate, searchText, sourceFilter, orderTypeFilter);
	}

	private async Task<List<ReportTopItemRow>> LoadTopItemsWithConnectionAsync(
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();
		return await LoadTopItemsAsync(connection, startDate, endDate, sourceFilter, orderTypeFilter);
	}

	public async Task<DailyReportSnapshot> GetTopSellReportAsync(
		DateTime startDate,
		DateTime endDate,
		TopSellSection section,
		string? searchText = null)
	{
		var snapshot = new DailyReportSnapshot
		{
			StartDate = startDate.Date,
			EndDate = endDate.Date,
			SearchText = searchText?.Trim() ?? string.Empty
		};

		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();

		snapshot.TopItems = await LoadTopSellItemsAsync(connection, snapshot.StartDate, snapshot.EndDate.AddDays(1), section, snapshot.SearchText);
		snapshot.Summary = new ReportSummary
		{
			OrderCount = 0,
			GrossSales = snapshot.TopItems.Sum(item => item.GrossSales),
			NetSales = snapshot.TopItems.Sum(item => item.NetSales),
			VatAmount = snapshot.TopItems.Sum(item => item.VatAmount),
			AverageOrderValue = 0m
		};

		return snapshot;
	}

	public async Task<string> ExportCsvAsync(DailyReportSnapshot report)
	{
		var folderPath = GetReportOutputFolder();
		Directory.CreateDirectory(folderPath);

		var fileName = $"Report_{report.StartDate:yyyyMMdd}_{report.EndDate:yyyyMMdd}_{DateTime.Now:HHmmss}.csv";
		var filePath = Path.Combine(folderPath, fileName);

		var builder = new StringBuilder();
		builder.AppendLine($"Report Range,{report.StartDate:yyyy-MM-dd},{report.EndDate:yyyy-MM-dd}");
		builder.AppendLine($"Source Filter,{report.SourceFilter}");
		builder.AppendLine($"Order Type Filter,{report.OrderTypeFilter}");
		builder.AppendLine($"Search,{EscapeCsv(report.SearchText)}");
		builder.AppendLine();
		builder.AppendLine("Summary");
		builder.AppendLine("Order Count,Gross Sales,Net Sales,VAT,Delivery Charges,Average Order Value");
		builder.AppendLine($"{report.Summary.OrderCount},{report.Summary.GrossSales:F2},{report.Summary.NetSales:F2},{report.Summary.VatAmount:F2},{report.Summary.DeliveryChargeTotal:F2},{report.Summary.AverageOrderValue:F2}");
		builder.AppendLine();
		builder.AppendLine("Orders");
		builder.AppendLine("Created At,Order Number,Customer,Phone,Source,Type,Status,Items,Gross,Net,VAT");

		foreach (var order in report.Orders)
		{
			builder.AppendLine(string.Join(',', new[]
			{
				EscapeCsv(order.CreatedAtDisplay),
				EscapeCsv(order.OrderNumber),
				EscapeCsv(order.CustomerName),
				EscapeCsv(order.CustomerPhone),
				EscapeCsv(order.SourceChannel),
				EscapeCsv(order.OrderType),
				EscapeCsv(order.Status),
				order.ItemCount.ToString(CultureInfo.InvariantCulture),
				order.GrossSales.ToString("F2", CultureInfo.InvariantCulture),
				order.NetSales.ToString("F2", CultureInfo.InvariantCulture),
				order.VatAmount.ToString("F2", CultureInfo.InvariantCulture)
			}));
		}

		builder.AppendLine();
		builder.AppendLine("Top Items");
		builder.AppendLine("Category,Item,Quantity,Gross,Net,VAT");

		foreach (var item in report.TopItems)
		{
			builder.AppendLine(string.Join(',', new[]
			{
				EscapeCsv(item.CategoryName),
				EscapeCsv(item.ItemName),
				item.TotalQuantity.ToString(CultureInfo.InvariantCulture),
				item.GrossSales.ToString("F2", CultureInfo.InvariantCulture),
				item.NetSales.ToString("F2", CultureInfo.InvariantCulture),
				item.VatAmount.ToString("F2", CultureInfo.InvariantCulture)
			}));
		}

		await File.WriteAllTextAsync(filePath, builder.ToString(), Encoding.UTF8);
		return filePath;
	}

	public async Task<string> ExportPdfAsync(DailyReportSnapshot report, BusinessInfo? businessInfo = null, string businessName = "POS-in-NET")
	{
		var folderPath = GetReportOutputFolder();
		Directory.CreateDirectory(folderPath);

		var fileName = $"Report_{report.StartDate:yyyyMMdd}_{report.EndDate:yyyyMMdd}_{DateTime.Now:HHmmss}.pdf";
		var filePath = Path.Combine(folderPath, fileName);

		using var document = new PdfDocument();
		document.PageSettings.Size = PdfPageSize.A4;
		document.PageSettings.Orientation = PdfPageOrientation.Landscape;

		var page = document.Pages.Add();
		var graphics = page.Graphics;

		var titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 20, PdfFontStyle.Bold);
		var headingFont = new PdfStandardFont(PdfFontFamily.Helvetica, 12, PdfFontStyle.Bold);
		var normalFont = new PdfStandardFont(PdfFontFamily.Helvetica, 10);
		var smallFont = new PdfStandardFont(PdfFontFamily.Helvetica, 8);
		var metaFont = new PdfStandardFont(PdfFontFamily.Helvetica, 9);

		var resolvedBusinessName = string.IsNullOrWhiteSpace(businessInfo?.RestaurantName)
			? businessName
			: businessInfo!.RestaurantName.Trim();

		var pageWidth = page.GetClientSize().Width;
		var contentWidth = pageWidth - 40;

		var y = 20f;
		var headerRect = new RectangleF(20, y, contentWidth, 92);
		graphics.DrawRectangle(new PdfSolidBrush(PdfColor.FromArgb(255, 239, 246, 255)), headerRect);
		graphics.DrawRectangle(new PdfPen(PdfColor.FromArgb(255, 191, 219, 254), 1), headerRect);

		var textStartX = 34f;
		if (!string.IsNullOrWhiteSpace(businessInfo?.LogoPath) && File.Exists(businessInfo.LogoPath))
		{
			try
			{
				await using var logoStream = File.OpenRead(businessInfo.LogoPath);
				var logo = new PdfBitmap(logoStream);
				graphics.DrawImage(logo, new RectangleF(34, y + 16, 56, 56));
				textStartX = 100f;
			}
			catch
			{
				// Keep rendering even if logo cannot be loaded.
			}
		}

		graphics.DrawString(resolvedBusinessName, titleFont, PdfBrushes.Black, new PdfPointF(textStartX, y + 16));

		var rightMetaX = pageWidth - 250;
		graphics.DrawString("Sales Report", headingFont, PdfBrushes.Black, new PdfPointF(rightMetaX, y + 16));
		graphics.DrawString($"Period: {report.StartDate:dd MMM yyyy} - {report.EndDate:dd MMM yyyy}", metaFont, PdfBrushes.DarkSlateGray, new PdfPointF(rightMetaX, y + 36));
		graphics.DrawString($"Generated: {DateTime.Now:dd MMM yyyy HH:mm}", metaFont, PdfBrushes.DarkSlateGray, new PdfPointF(rightMetaX, y + 52));

		y += 104;

		var businessLines = BuildBusinessInfoLines(businessInfo);
		if (businessLines.Count > 0)
		{
			var businessInfoHeight = 14 + (businessLines.Count * 12);
			var infoRect = new RectangleF(20, y, contentWidth, businessInfoHeight);
			graphics.DrawRectangle(new PdfSolidBrush(PdfColor.FromArgb(255, 248, 250, 252)), infoRect);
			graphics.DrawRectangle(new PdfPen(PdfColor.FromArgb(255, 226, 232, 240), 1), infoRect);

			graphics.DrawString("Business Information", headingFont, PdfBrushes.Black, new PdfPointF(28, y + 6));
			var businessLineY = y + 20;
			foreach (var line in businessLines)
			{
				graphics.DrawString(line, metaFont, PdfBrushes.DarkSlateGray, new PdfPointF(28, businessLineY));
				businessLineY += 12;
			}

			y += businessInfoHeight + 10;
		}

		var filterRect = new RectangleF(20, y, contentWidth, 24);
		graphics.DrawRectangle(new PdfSolidBrush(PdfColor.FromArgb(255, 255, 255, 255)), filterRect);
		graphics.DrawRectangle(new PdfPen(PdfColor.FromArgb(255, 226, 232, 240), 1), filterRect);
		graphics.DrawString($"Source: {report.SourceFilter}  |  Order Type: {report.OrderTypeFilter}  |  Search: {(string.IsNullOrWhiteSpace(report.SearchText) ? "All" : report.SearchText)}", metaFont, PdfBrushes.DarkSlateGray, new PdfPointF(28, y + 7));

		y += 34;

		var metricWidth = (contentWidth - 40) / 6;
		DrawMetricBlock(graphics, "Orders", report.Summary.OrderCount.ToString(CultureInfo.InvariantCulture), 20, y, metricWidth);
		DrawMetricBlock(graphics, "Gross", $"£{report.Summary.GrossSales:F2}", 20 + (metricWidth + 8), y, metricWidth);
		DrawMetricBlock(graphics, "Net", $"£{report.Summary.NetSales:F2}", 20 + ((metricWidth + 8) * 2), y, metricWidth);
		DrawMetricBlock(graphics, "VAT", $"£{report.Summary.VatAmount:F2}", 20 + ((metricWidth + 8) * 3), y, metricWidth);
		DrawMetricBlock(graphics, "Delivery", $"£{report.Summary.DeliveryChargeTotal:F2}", 20 + ((metricWidth + 8) * 4), y, metricWidth);
		DrawMetricBlock(graphics, "AOV", $"£{report.Summary.AverageOrderValue:F2}", 20 + ((metricWidth + 8) * 5), y, metricWidth);
		y += 72;

		graphics.DrawString("Top Items", headingFont, PdfBrushes.Black, new PdfPointF(20, y));
		y += 16;
		foreach (var item in report.TopItems.Take(8))
		{
			graphics.DrawString($"{item.CategoryName} / {item.ItemName}  •  Qty {item.TotalQuantity}  •  Gross £{item.GrossSales:F2}  •  VAT £{item.VatAmount:F2}", normalFont, PdfBrushes.Black, new PdfPointF(24, y));
			y += 14;
		}

		y += 10;
		graphics.DrawString("Recent Orders", headingFont, PdfBrushes.Black, new PdfPointF(20, y));
		y += 16;
		foreach (var order in report.Orders.Take(10))
		{
			var line = $"{order.CreatedAtDisplay}  |  {order.OrderNumber}  |  {order.CustomerName}  |  {order.OrderMeta}  |  £{order.GrossSales:F2}";
			graphics.DrawString(line, smallFont, PdfBrushes.Black, new PdfPointF(24, y));
			y += 12;
		}

		await using var stream = File.Create(filePath);
		document.Save(stream);

		return filePath;
	}

	private static List<string> BuildBusinessInfoLines(BusinessInfo? businessInfo)
	{
		var lines = new List<string>();
		if (businessInfo == null)
		{
			return lines;
		}

		var addressParts = new[]
		{
			businessInfo.Address,
			businessInfo.City,
			businessInfo.County,
			businessInfo.Postcode,
			businessInfo.Country
		}
		.Where(part => !string.IsNullOrWhiteSpace(part))
		.Select(part => part.Trim())
		.ToArray();

		if (addressParts.Length > 0)
		{
			lines.Add($"Address: {string.Join(", ", addressParts)}");
		}

		if (!string.IsNullOrWhiteSpace(businessInfo.PhoneNumber))
		{
			lines.Add($"Phone: {businessInfo.PhoneNumber.Trim()}");
		}

		if (!string.IsNullOrWhiteSpace(businessInfo.Email))
		{
			lines.Add($"Email: {businessInfo.Email.Trim()}");
		}

		if (!string.IsNullOrWhiteSpace(businessInfo.Website))
		{
			lines.Add($"Website: {businessInfo.Website.Trim()}");
		}

		if (!string.IsNullOrWhiteSpace(businessInfo.VATNumber))
		{
			lines.Add($"VAT Number: {businessInfo.VATNumber.Trim()}");
		}

		if (!string.IsNullOrWhiteSpace(businessInfo.TaxCode))
		{
			lines.Add($"Tax Code: {businessInfo.TaxCode.Trim()}");
		}

		return lines;
	}

	private static string GetReportOutputFolder()
	{
		return Path.Combine(FileSystem.Current.AppDataDirectory, "POS_Reports");
	}

	private async Task<ReportSummary> LoadSummaryAsync(
		MySqlConnection connection,
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		var query = new StringBuilder(@"
			SELECT
				COUNT(DISTINCT order_db_id) AS order_count,
				COALESCE(SUM(line_gross), 0) AS gross_sales,
				COALESCE(SUM(line_net), 0) AS net_sales,
				COALESCE(SUM(line_vat), 0) AS vat_amount
			FROM vw_report_order_lines_live
			WHERE created_at >= @startDate AND created_at < @endDate");

		AppendOptionalFilters(query, sourceFilter, orderTypeFilter);

		await using var command = new MySqlCommand(query.ToString(), connection);
		command.Parameters.AddWithValue("@startDate", startDate);
		command.Parameters.AddWithValue("@endDate", endDate);
		AddOptionalParameters(command, sourceFilter, orderTypeFilter);

		await using var reader = await command.ExecuteReaderAsync();
		if (await reader.ReadAsync())
		{
			var orderCount = GetInt32(reader, "order_count");
			var grossSales = GetDecimal(reader, "gross_sales");
			var netSales = GetDecimal(reader, "net_sales");
			var vatAmount = GetDecimal(reader, "vat_amount");

			return new ReportSummary
			{
				OrderCount = orderCount,
				GrossSales = grossSales,
				NetSales = netSales,
				VatAmount = vatAmount,
				AverageOrderValue = orderCount > 0 ? grossSales / orderCount : 0m
			};
		}

		return new ReportSummary();
	}

	private async Task<List<ReportOrderRow>> LoadOrdersAsync(
		MySqlConnection connection,
		DateTime startDate,
		DateTime endDate,
		string searchText,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		var query = new StringBuilder(@"
			SELECT
				o.order_db_id,
				o.order_id,
				o.order_number,
				o.created_at,
				o.source_channel,
				o.order_type,
				o.status,
				o.customer_name,
				o.customer_phone,
				COALESCE(order_lines.item_count, 0) AS item_count,
				COALESCE(order_lines.gross_sales, 0) AS gross_sales,
				COALESCE(order_lines.net_sales, 0) AS net_sales,
				COALESCE(order_lines.vat_amount, 0) AS vat_amount
			FROM vw_report_orders_live o
			LEFT JOIN (
				SELECT
					order_db_id,
					COUNT(*) AS item_count,
					ROUND(SUM(line_gross), 2) AS gross_sales,
					ROUND(SUM(line_net), 2) AS net_sales,
					ROUND(SUM(line_vat), 2) AS vat_amount
				FROM vw_report_order_lines_live
				GROUP BY order_db_id
			) order_lines ON order_lines.order_db_id = o.order_db_id
			WHERE o.created_at >= @startDate AND o.created_at < @endDate");

		AppendOptionalFilters(query, sourceFilter, orderTypeFilter);

		if (!string.IsNullOrWhiteSpace(searchText))
		{
			query.Append(@"
				AND (
					o.order_number LIKE @searchText
					OR o.customer_name LIKE @searchText
					OR o.customer_phone LIKE @searchText
					OR o.order_id LIKE @searchText
				)");
		}

		query.Append(" ORDER BY o.created_at DESC LIMIT 500");

		await using var command = new MySqlCommand(query.ToString(), connection);
		command.Parameters.AddWithValue("@startDate", startDate);
		command.Parameters.AddWithValue("@endDate", endDate);
		AddOptionalParameters(command, sourceFilter, orderTypeFilter);
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			command.Parameters.AddWithValue("@searchText", $"%{searchText.Trim()}%");
		}

		var orders = new List<ReportOrderRow>();
		await using var reader = await command.ExecuteReaderAsync();
		while (await reader.ReadAsync())
		{
			orders.Add(new ReportOrderRow
			{
				OrderDbId = GetInt32(reader, "order_db_id"),
				OrderId = GetString(reader, "order_id"),
				OrderNumber = GetString(reader, "order_number"),
				CreatedAt = GetDateTime(reader, "created_at"),
				SourceChannel = GetString(reader, "source_channel"),
				OrderType = GetString(reader, "order_type"),
				Status = GetString(reader, "status"),
				CustomerName = GetString(reader, "customer_name"),
				CustomerPhone = GetString(reader, "customer_phone"),
				ItemCount = GetInt32(reader, "item_count"),
				GrossSales = GetDecimal(reader, "gross_sales"),
				NetSales = GetDecimal(reader, "net_sales"),
				VatAmount = GetDecimal(reader, "vat_amount")
			});
		}

		return orders;
	}

	public async Task<ReportOrderDetail?> GetOrderDetailAsync(int orderDbId)
	{
		await using var connection = new MySqlConnection(_databaseService.GetConnectionString());
		await connection.OpenAsync();

		var detail = await LoadOrderHeaderAsync(connection, orderDbId);
		if (detail == null)
		{
			return null;
		}

		detail.Lines = await LoadOrderLinesAsync(connection, orderDbId);
		return detail;
	}

	private async Task<List<ReportTopItemRow>> LoadTopItemsAsync(
		MySqlConnection connection,
		DateTime startDate,
		DateTime endDate,
		ReportSourceFilter sourceFilter,
		ReportOrderTypeFilter orderTypeFilter)
	{
		var query = new StringBuilder(@"
			SELECT
				item_name,
				SUM(quantity) AS total_quantity,
				ROUND(SUM(line_gross), 2) AS gross_sales,
				ROUND(SUM(line_net), 2) AS net_sales,
				ROUND(SUM(line_vat), 2) AS vat_amount
			FROM vw_report_order_lines_live
			WHERE created_at >= @startDate AND created_at < @endDate");

		AppendOptionalFilters(query, sourceFilter, orderTypeFilter);
		query.Append(@"
			GROUP BY item_name
			ORDER BY total_quantity DESC, gross_sales DESC
			LIMIT 20");

		await using var command = new MySqlCommand(query.ToString(), connection);
		command.Parameters.AddWithValue("@startDate", startDate);
		command.Parameters.AddWithValue("@endDate", endDate);
		AddOptionalParameters(command, sourceFilter, orderTypeFilter);

		var items = new List<ReportTopItemRow>();
		await using var reader = await command.ExecuteReaderAsync();
		while (await reader.ReadAsync())
		{
			items.Add(new ReportTopItemRow
			{
				ItemName = GetString(reader, "item_name"),
				TotalQuantity = GetInt32(reader, "total_quantity"),
				GrossSales = GetDecimal(reader, "gross_sales"),
				NetSales = GetDecimal(reader, "net_sales"),
				VatAmount = GetDecimal(reader, "vat_amount")
			});
		}

		return items;
	}

	private async Task<List<ReportTopItemRow>> LoadTopSellItemsAsync(
		MySqlConnection connection,
		DateTime startDate,
		DateTime endDate,
		TopSellSection section,
		string searchText)
	{
		var drinkKeywords = new[]
		{
			"drink", "beverage", "juice", "coffee", "tea", "water", "soda", "cola",
			"milkshake", "shake", "smoothie", "latte", "espresso", "cappuccino",
			"hot chocolate", "beer", "wine", "cider", "mocktail", "cocktail",
			"soft drink", "bottle", "bottled"
		};

		const string normalizedText = "LOWER(CONCAT(COALESCE(mc.Name, ''), ' ', COALESCE(l.item_name, ''))) COLLATE utf8mb4_unicode_ci";
		var drinkFilter = string.Join(" OR ", drinkKeywords.Select(word => $"{normalizedText} LIKE '%{word}%'"));

		var query = new StringBuilder(@"
			SELECT
				COALESCE(mc.Name, _utf8mb4'Uncategorized' COLLATE utf8mb4_unicode_ci) AS category_name,
				l.item_name,
				SUM(l.quantity) AS total_quantity,
				ROUND(SUM(l.line_gross), 2) AS gross_sales,
				ROUND(SUM(l.line_net), 2) AS net_sales,
				ROUND(SUM(l.line_vat), 2) AS vat_amount
			FROM vw_report_order_lines_live l
			LEFT JOIN MenuItems mi ON CONVERT(mi.Id USING utf8mb4) COLLATE utf8mb4_unicode_ci = CONVERT(l.menu_item_id USING utf8mb4) COLLATE utf8mb4_unicode_ci
			LEFT JOIN MenuCategories mc ON CONVERT(mc.Id USING utf8mb4) COLLATE utf8mb4_unicode_ci = CONVERT(mi.CategoryId USING utf8mb4) COLLATE utf8mb4_unicode_ci
			WHERE l.created_at >= @startDate AND l.created_at < @endDate");

		query.Append(section == TopSellSection.Drink
			? $@"
				AND ({drinkFilter})"
			: $@"
				AND NOT ({drinkFilter})");

		if (!string.IsNullOrWhiteSpace(searchText))
		{
			query.Append(@"
				AND (
					l.item_name COLLATE utf8mb4_unicode_ci LIKE @searchText
					OR mc.Name COLLATE utf8mb4_unicode_ci LIKE @searchText
				)");
		}

		query.Append(@"
			GROUP BY category_name, l.menu_item_id, l.item_name
			ORDER BY total_quantity DESC, gross_sales DESC
			LIMIT 30");

		await using var command = new MySqlCommand(query.ToString(), connection);
		command.Parameters.AddWithValue("@startDate", startDate);
		command.Parameters.AddWithValue("@endDate", endDate);
		if (!string.IsNullOrWhiteSpace(searchText))
		{
			command.Parameters.AddWithValue("@searchText", $"%{searchText.Trim()}%");
		}

		var items = new List<ReportTopItemRow>();
		await using var reader = await command.ExecuteReaderAsync();
		while (await reader.ReadAsync())
		{
			items.Add(new ReportTopItemRow
			{
				CategoryName = GetString(reader, "category_name"),
				ItemName = GetString(reader, "item_name"),
				TotalQuantity = GetInt32(reader, "total_quantity"),
				GrossSales = GetDecimal(reader, "gross_sales"),
				NetSales = GetDecimal(reader, "net_sales"),
				VatAmount = GetDecimal(reader, "vat_amount")
			});
		}

		return items;
	}

	private async Task<ReportOrderDetail?> LoadOrderHeaderAsync(MySqlConnection connection, int orderDbId)
	{
		var query = @"
			SELECT
				id,
				order_id,
				order_number,
				cloud_order_id,
				created_at,
				source_channel,
				order_type,
				status,
				customer_name,
				customer_phone,
				customer_email,
				customer_address,
				payment_method,
				special_instructions,
				subtotal_amount,
				delivery_fee,
				tax_amount,
				total_amount
			FROM orders
			WHERE id = @orderDbId";

		await using var command = new MySqlCommand(query, connection);
		command.Parameters.AddWithValue("@orderDbId", orderDbId);

		await using var reader = await command.ExecuteReaderAsync();
		if (!await reader.ReadAsync())
		{
			return null;
		}

		return new ReportOrderDetail
		{
			OrderDbId = GetInt32(reader, "id"),
			OrderId = GetString(reader, "order_id"),
			OrderNumber = GetString(reader, "order_number"),
			CloudOrderId = GetString(reader, "cloud_order_id"),
			CreatedAt = GetDateTime(reader, "created_at"),
			SourceChannel = GetString(reader, "source_channel"),
			OrderType = GetString(reader, "order_type"),
			Status = GetString(reader, "status"),
			CustomerName = GetString(reader, "customer_name"),
			CustomerPhone = GetString(reader, "customer_phone"),
			CustomerEmail = GetString(reader, "customer_email"),
			CustomerAddress = GetString(reader, "customer_address"),
			PaymentMethod = GetString(reader, "payment_method"),
			SpecialInstructions = GetString(reader, "special_instructions"),
			SubtotalAmount = GetDecimal(reader, "subtotal_amount"),
			DeliveryFee = GetDecimal(reader, "delivery_fee"),
			TaxAmount = GetDecimal(reader, "tax_amount"),
			TotalAmount = GetDecimal(reader, "total_amount")
		};
	}

	private async Task<List<ReportOrderLineDetail>> LoadOrderLinesAsync(MySqlConnection connection, int orderDbId)
	{
		var query = @"
			SELECT
				oi.id AS order_item_id,
				oi.item_name,
				oi.quantity,
				COALESCE(oi.item_price, 0.00) AS unit_price,
				COALESCE(oi.special_instructions, '') AS special_instructions,
				COALESCE(addons.addon_unit_total, 0.00) AS addon_unit_total,
				COALESCE(addons.addon_summary, '') AS addon_summary,
				COALESCE(order_lines.line_gross, 0.00) AS line_gross,
				COALESCE(order_lines.line_net, 0.00) AS line_net,
				COALESCE(order_lines.line_vat, 0.00) AS line_vat
			FROM order_items oi
			LEFT JOIN (
				SELECT
					order_item_id,
					SUM(COALESCE(addon_price, 0.00) * COALESCE(quantity, 1)) AS addon_unit_total,
					GROUP_CONCAT(CONCAT(COALESCE(addon_name, ''), ' x', COALESCE(quantity, 1)) ORDER BY id SEPARATOR ', ') AS addon_summary
				FROM order_item_addons
				GROUP BY order_item_id
			) addons ON addons.order_item_id = oi.id
			LEFT JOIN vw_report_order_lines_live order_lines ON order_lines.order_item_id = oi.id
			WHERE oi.order_id = @orderDbId
			ORDER BY oi.id";

		await using var command = new MySqlCommand(query, connection);
		command.Parameters.AddWithValue("@orderDbId", orderDbId);

		var items = new List<ReportOrderLineDetail>();
		await using var reader = await command.ExecuteReaderAsync();
		while (await reader.ReadAsync())
		{
			items.Add(new ReportOrderLineDetail
			{
				OrderItemId = GetInt32(reader, "order_item_id"),
				ItemName = GetString(reader, "item_name"),
				Quantity = GetInt32(reader, "quantity"),
				UnitPrice = GetDecimal(reader, "unit_price"),
				AddonUnitTotal = GetDecimal(reader, "addon_unit_total"),
				AddonSummary = GetString(reader, "addon_summary"),
				LineGross = GetDecimal(reader, "line_gross"),
				LineNet = GetDecimal(reader, "line_net"),
				LineVat = GetDecimal(reader, "line_vat"),
				SpecialInstructions = GetString(reader, "special_instructions")
			});
		}

		return items;
	}

	private static void AppendOptionalFilters(StringBuilder query, ReportSourceFilter sourceFilter, ReportOrderTypeFilter orderTypeFilter)
	{
		if (sourceFilter != ReportSourceFilter.All)
		{
			query.Append(" AND source_channel = @sourceChannel");
		}

		if (orderTypeFilter != ReportOrderTypeFilter.All)
		{
			query.Append(" AND order_type = @orderType");
		}
	}

	private static void AddOptionalParameters(MySqlCommand command, ReportSourceFilter sourceFilter, ReportOrderTypeFilter orderTypeFilter)
	{
		if (sourceFilter != ReportSourceFilter.All)
		{
			command.Parameters.AddWithValue("@sourceChannel", sourceFilter.ToString().ToLowerInvariant());
		}

		if (orderTypeFilter != ReportOrderTypeFilter.All)
		{
			command.Parameters.AddWithValue("@orderType", orderTypeFilter.ToString().ToLowerInvariant());
		}
	}

	private static string EscapeCsv(string? value)
	{
		var text = value ?? string.Empty;
		if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
		{
			return $"\"{text.Replace("\"", "\"\"")}\"";
		}

		return text;
	}

	private static string GetString(MySqlDataReader reader, string columnName)
	{
		var ordinal = reader.GetOrdinal(columnName);
		return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;
	}

	private static int GetInt32(MySqlDataReader reader, string columnName)
	{
		var ordinal = reader.GetOrdinal(columnName);
		return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	}

	private static decimal GetDecimal(MySqlDataReader reader, string columnName)
	{
		var ordinal = reader.GetOrdinal(columnName);
		return reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	}

	private static DateTime GetDateTime(MySqlDataReader reader, string columnName)
	{
		var ordinal = reader.GetOrdinal(columnName);
		return reader.IsDBNull(ordinal) ? DateTime.MinValue : Convert.ToDateTime(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	}

	private static void DrawMetricBlock(PdfGraphics graphics, string label, string value, float x, float y, float width = 130)
	{
		var rectangle = new RectangleF(x, y, width, 56);
		graphics.DrawRectangle(new PdfSolidBrush(PdfColor.FromArgb(255, 255, 255, 255)), rectangle);
		graphics.DrawRectangle(new PdfPen(PdfColor.FromArgb(255, 226, 232, 240), 1), rectangle);

		var labelFont = new PdfStandardFont(PdfFontFamily.Helvetica, 9, PdfFontStyle.Bold);
		var valueFont = new PdfStandardFont(PdfFontFamily.Helvetica, 16, PdfFontStyle.Bold);
		graphics.DrawString(label, labelFont, PdfBrushes.DarkSlateGray, new PdfPointF(x + 10, y + 10));
		graphics.DrawString(value, valueFont, PdfBrushes.Black, new PdfPointF(x + 10, y + 28));
	}
}
