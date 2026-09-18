namespace OrderWeb.Contracts.Dtos;

/// <summary>
/// Bar Inventory contracts — Mother owns stock; Client uses SharedUI via Mother.
/// Phase 1–3: stock master + track/link + board. Phase 4: receive/count/waste + weekly report.
/// </summary>
public static class BarStockUnits
{
    public const string Bottle = "bottle";
    public const string Case = "case";
    public const string Ml = "ml";

    public static readonly IReadOnlyList<string> All = [Bottle, Case, Ml];

    public static string Normalize(string? unit)
    {
        var value = (unit ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "bottles" or "bottle" => Bottle,
            "cases" or "case" => Case,
            "millilitre" or "milliliters" or "millilitres" or "mls" or "ml" => Ml,
            _ => Bottle
        };
    }
}

/// <summary>Bar Inventory board sections — presets + Admin-typed custom categories.</summary>
public static class BarStockSections
{
    public const string All = "all";
    public const string Wine = "wine";
    public const string SoftDrink = "soft_drink";
    public const string Spirit = "spirit";
    public const string Beer = "beer";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> Assignable =
        [Wine, SoftDrink, Spirit, Beer, Other];

    public static readonly IReadOnlyList<(string Key, string Label)> ChipOptions =
    [
        (All, "All"),
        (Wine, "Wine"),
        (SoftDrink, "Soft Drink"),
        (Spirit, "Spirit"),
        (Beer, "Beer"),
        (Other, "Other")
    ];

    public static readonly IReadOnlyList<(string Key, string Label)> PresetChips =
    [
        (Wine, "Wine"),
        (SoftDrink, "Soft Drink"),
        (Spirit, "Spirit"),
        (Beer, "Beer"),
        (Other, "Other")
    ];

    /// <summary>Normalize for storage — presets map to fixed keys; anything else is a custom slug.</summary>
    public static string Normalize(string? section)
    {
        var raw = (section ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Other;
        }

        var value = raw.ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return value switch
        {
            "wine" or "wines" => Wine,
            "soft" or "soft_drink" or "softdrink" or "soft_drinks" => SoftDrink,
            "spirit" or "spirits" or "liquor" => Spirit,
            "beer" or "beers" or "lager" => Beer,
            "other" or "misc" => Other,
            "all" => Other,
            _ => ToCustomKey(raw)
        };
    }

    public static string NormalizeFilter(string? section)
    {
        var raw = (section ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw) ||
            string.Equals(raw, "all", StringComparison.OrdinalIgnoreCase))
        {
            return All;
        }

        return Normalize(raw);
    }

    public static string DisplayName(string? section)
    {
        var key = string.IsNullOrWhiteSpace(section) ? Other : section.Trim();
        var normalized = key.ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized switch
        {
            All => "All",
            Wine => "Wine",
            SoftDrink or "soft" or "softdrink" => "Soft Drink",
            Spirit or "spirits" => "Spirit",
            Beer or "beers" => "Beer",
            Other or "misc" => "Other",
            _ => ToDisplayLabel(Normalize(key))
        };
    }

    public static bool IsPreset(string? section)
    {
        var key = Normalize(section);
        return key is Wine or SoftDrink or Spirit or Beer or Other;
    }

    private static string ToCustomKey(string raw)
    {
        var chars = raw.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        var key = new string(chars);
        while (key.Contains("__", StringComparison.Ordinal))
        {
            key = key.Replace("__", "_", StringComparison.Ordinal);
        }

        key = key.Trim('_');
        return string.IsNullOrWhiteSpace(key) ? Other : key;
    }

    private static string ToDisplayLabel(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Other";
        }

        var parts = key.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(p =>
            p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));
    }
}

/// <summary>
/// Suggest how much to order: only when on-hand ≤ Low; qty = Max − on-hand.
/// Bottle stocks use pack_size as ml/bottle — suggest in bottles (not cases).
/// </summary>
public static class BarStockOrderSuggest
{
    public static bool IsBelowLow(decimal onHand, decimal lowLevel) =>
        lowLevel > 0m && onHand <= lowLevel;

    public static decimal QtyToReachMax(
        decimal onHand,
        decimal lowLevel,
        decimal maxLevel,
        decimal packSize,
        string? stockUnit = null)
    {
        if (maxLevel <= 0m || !IsBelowLow(onHand, lowLevel))
        {
            return 0m;
        }

        var need = maxLevel - onHand;
        if (need <= 0m)
        {
            return 0m;
        }

        var unit = BarStockUnits.Normalize(stockUnit);
        // Bottle / ml on-hand: pack_size is volume conversion, not order pack.
        if (unit is BarStockUnits.Bottle or BarStockUnits.Ml)
        {
            return Math.Ceiling(need);
        }

        var pack = packSize <= 0 ? 1m : packSize;
        if (pack <= 1m)
        {
            return Math.Ceiling(need);
        }

        var packs = Math.Ceiling(need / pack);
        return packs * pack;
    }

    /// <summary>Legacy: treat single par as both Low and Max (same threshold).</summary>
    public static decimal QtyToReachPar(decimal onHand, decimal parLevel, decimal packSize, string? stockUnit = null) =>
        QtyToReachMax(onHand, parLevel, parLevel, packSize, stockUnit);

    public static string FormatSuggest(
        decimal onHand,
        decimal lowLevel,
        decimal maxLevel,
        decimal packSize,
        string stockUnit)
    {
        var need = QtyToReachMax(onHand, lowLevel, maxLevel, packSize, stockUnit);
        if (need <= 0m)
        {
            return "—";
        }

        var unit = BarStockUnits.Normalize(stockUnit);
        if (unit is BarStockUnits.Bottle or BarStockUnits.Ml)
        {
            return $"{need:0.###} {unit}";
        }

        var pack = packSize <= 0 ? 1m : packSize;
        if (pack > 1m)
        {
            var packs = Math.Ceiling(need / pack);
            return $"{packs:0.###} case{(packs == 1 ? "" : "s")} ({packs * pack:0.###} {unit})";
        }

        return $"{need:0.###} {unit}";
    }

    public static string FormatSuggest(decimal onHand, decimal parLevel, decimal packSize, string stockUnit) =>
        FormatSuggest(onHand, parLevel, parLevel, packSize, stockUnit);
}

public static class BarInventoryErrorCodes
{
    public const string Validation = "bar_inventory.validation";
    public const string NotFound = "bar_inventory.not_found";
    public const string AccessDenied = "bar_inventory.access_denied";
    public const string OfflineMother = "bar_inventory.offline_mother";
    public const string Unknown = "bar_inventory.unknown";
}

public static class BarStockMovementTypes
{
    public const string Receive = "receive";
    public const string Count = "count";
    public const string Waste = "waste";
    public const string Sale = "sale";
    public const string SaleVoid = "sale_void";
}

/// <summary>One order line to deduct / reverse (Mother Phase 5).</summary>
public sealed class BarSaleOrderLineDto
{
    /// <summary>Stable key for idempotency — prefer <c>i{dbId}</c> else <c>c{clientItemId}</c>.</summary>
    public string LineKey { get; set; } = string.Empty;
    public string? MenuItemId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class BarSaleDeductionResultDto
{
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public int AppliedCount { get; set; }
    public int SkippedCount { get; set; }
    public int OverdrawCount { get; set; }
}

/// <summary>Stock master row — shelf quantity lives here (not on the menu sell SKU).</summary>
public sealed class BarStockItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Sku { get; set; }
    /// <summary><see cref="BarStockSections"/> assignable key (wine, soft_drink, …).</summary>
    public string Section { get; set; } = BarStockSections.Other;
    /// <summary><see cref="BarStockUnits"/> — bottle, case, or ml.</summary>
    public string StockUnit { get; set; } = BarStockUnits.Bottle;
    /// <summary>Pack size (e.g. 12 bottles/case, or 750 when tracking bottle volume in ml).</summary>
    public decimal PackSize { get; set; } = 1m;
    /// <summary>Reorder point — Suggest when on-hand ≤ Low.</summary>
    public decimal LowLevel { get; set; }
    /// <summary>Target fill — Suggest qty = Max − on-hand.</summary>
    public decimal MaxLevel { get; set; }
    /// <summary>Legacy alias of <see cref="MaxLevel"/> (older callers / reports).</summary>
    public decimal ParLevel { get; set; }
    public decimal OnHand { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>One SKU deduct line on a menu item (cocktail = many rows).</summary>
public sealed class MenuItemBarStockComponentDto
{
    public string BarStockItemId { get; set; } = string.Empty;
    /// <summary>Display only.</summary>
    public string? Sku { get; set; }
    /// <summary>Display only.</summary>
    public string? StockName { get; set; }
    /// <summary>ml deducted from this SKU when the menu item is sold.</summary>
    public decimal SellPortionMl { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Admin setup on a menu item — Track On + one or more SKU · ml links.</summary>
public sealed class MenuItemBarStockSetupDto
{
    public string MenuItemId { get; set; } = string.Empty;
    public bool TrackBarInventory { get; set; }
    /// <summary>Legacy single link (kept for migration / first component).</summary>
    public string? BarStockItemId { get; set; }
    /// <summary>Legacy sell portion — prefer <see cref="Components"/>.</summary>
    public decimal? SellPortionQty { get; set; }
    public string? SellPortionUnit { get; set; }
    /// <summary>Optional nested stock create (legacy Add Item create path).</summary>
    public BarStockItemDto? Stock { get; set; }
    /// <summary>Multi-SKU deduct rows (SKU + ml). When Track On, at least one required.</summary>
    public IReadOnlyList<MenuItemBarStockComponentDto> Components { get; set; } =
        Array.Empty<MenuItemBarStockComponentDto>();
}

public sealed class BarStockListResponseDto
{
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public string? ErrorCode { get; set; }
    public IReadOnlyList<BarStockItemDto> Items { get; set; } = Array.Empty<BarStockItemDto>();
}

public sealed class MenuItemBarStockSetupResponseDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ErrorCode { get; set; }
    public MenuItemBarStockSetupDto? Setup { get; set; }
    public BarStockItemDto? Stock { get; set; }
}

/// <summary>One row on the Bar Inventory board (SharedUI presentation).</summary>
public sealed class BarStockBoardRowDto
{
    public string StockId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string Section { get; set; } = BarStockSections.Other;
    public string StockUnit { get; set; } = BarStockUnits.Bottle;
    public decimal PackSize { get; set; } = 1m;
    public decimal OnHand { get; set; }
    /// <summary>Reorder point — Suggest when on-hand ≤ Low.</summary>
    public decimal LowLevel { get; set; }
    /// <summary>Target fill — order back up to Max.</summary>
    public decimal MaxLevel { get; set; }
    /// <summary>Legacy alias of MaxLevel.</summary>
    public decimal ParLevel { get; set; }
    /// <summary>OK when on-hand &gt; Low; Low when at/under Low (and Low &gt; 0).</summary>
    public string Status { get; set; } = "OK";
    public int LinkedMenuItemCount { get; set; }
    /// <summary>Qty to order to reach Max (0 when not Low).</summary>
    public decimal SuggestOrderQty { get; set; }
    public string SuggestOrderDisplay { get; set; } = "—";
}

public sealed class BarStockBoardResponseDto
{
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public string? ErrorCode { get; set; }
    public IReadOnlyList<BarStockBoardRowDto> Items { get; set; } = Array.Empty<BarStockBoardRowDto>();
    /// <summary>Soft amber advisory — high-volume Track Off drinks (last 7 days). Null/empty = hide.</summary>
    public string? AdvisoryNote { get; set; }
    /// <summary>True when Items came from Client last-good cache (Mother offline).</summary>
    public bool FromCache { get; set; }
}

/// <summary>Who / till context for a movement (Mother applies; Client sends via API).</summary>
public sealed class BarStockMovementActorDto
{
    public string? StaffUserId { get; set; }
    public string? StaffDisplayName { get; set; }
    public string? TerminalId { get; set; }
    public string? TerminalLabel { get; set; }
    /// <summary><c>mother</c> or <c>client</c>.</summary>
    public string Source { get; set; } = "mother";
}

public sealed class BarStockReceiveRequestDto
{
    public string StockId { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    /// <summary>Stock unit or <see cref="BarStockUnits.Case"/> when converting via pack size.</summary>
    public string? InputUnit { get; set; }
    public string? Note { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? SessionToken { get; set; }
}

public sealed class BarStockCountRequestDto
{
    public string StockId { get; set; } = string.Empty;
    public decimal CountedQty { get; set; }
    public string? Note { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? SessionToken { get; set; }
}

public sealed class BarStockWasteRequestDto
{
    public string StockId { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public string? SessionToken { get; set; }
}

public sealed class BarStockMovementResponseDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ErrorCode { get; set; }
    public string? MovementId { get; set; }
    public string? MovementType { get; set; }
    public decimal QtyBefore { get; set; }
    public decimal QtyAfter { get; set; }
    public decimal QtyDelta { get; set; }
    public BarStockItemDto? Stock { get; set; }
    public BarStockBoardRowDto? BoardRow { get; set; }
}

public sealed class BarStockWeeklyReportRowDto
{
    public string StockId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string Section { get; set; } = BarStockSections.Other;
    public string StockUnit { get; set; } = BarStockUnits.Bottle;
    public decimal PackSize { get; set; } = 1m;
    public decimal ReceiveTotal { get; set; }
    public decimal WasteTotal { get; set; }
    public decimal CountVarianceTotal { get; set; }
    /// <summary>Sales usage in period (absolute sale deduct qty in stock_unit).</summary>
    public decimal UsedTotal { get; set; }
    /// <summary>Current on-hand (Have).</summary>
    public decimal EndingOnHand { get; set; }
    public decimal ParLevel { get; set; }
    public decimal SuggestOrderQty { get; set; }
    public string SuggestOrderDisplay { get; set; } = "—";
    public string Status { get; set; } = "OK";
    public string HaveDisplay { get; set; } = "—";
    public string UsedDisplay { get; set; } = "—";
    public string WasteDisplay { get; set; } = "—";
}

public sealed class BarStockWeeklyReportResponseDto
{
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public string? ErrorCode { get; set; }
    public string PeriodLabel { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal ReceiveTotal { get; set; }
    public decimal WasteTotal { get; set; }
    public decimal CountVarianceTotal { get; set; }
    public decimal UsedTotal { get; set; }
    /// <summary>Sum of current on-hand for listed rows (Have).</summary>
    public decimal HaveTotal { get; set; }
    public string HaveTotalDisplay { get; set; } = "—";
    public string UsedTotalDisplay { get; set; } = "—";
    public string WasteTotalDisplay { get; set; } = "—";
    public IReadOnlyList<BarStockWeeklyReportRowDto> Items { get; set; } = Array.Empty<BarStockWeeklyReportRowDto>();
}

/// <summary>Bottles · ml display for Bar Inventory report / cards.</summary>
public static class BarStockQtyDisplay
{
    public static string Format(decimal qtyInStockUnit, decimal packSize, string? stockUnit)
    {
        var unit = BarStockUnits.Normalize(stockUnit);
        var abs = Math.Abs(qtyInStockUnit);
        var qtyText = FormatQty(qtyInStockUnit);
        if (unit == BarStockUnits.Bottle && packSize > 0m)
        {
            var bottleWord = abs == 1m ? "bottle" : "bottles";
            var ml = qtyInStockUnit * packSize;
            return $"{qtyText} {bottleWord} · {FormatQty(ml)} ml";
        }

        return $"{qtyText} {unit}";
    }

    public static string FormatQty(decimal value)
    {
        if (value == decimal.Truncate(value))
        {
            return value.ToString("0");
        }

        return value.ToString("0.###");
    }
}

/// <summary>CSV for usage report — no Low/Max columns.</summary>
public static class BarStockUsageReportCsv
{
    public static string Build(
        BarStockWeeklyReportResponseDto report,
        string? restaurantName = null,
        DateTime? generatedAt = null)
    {
        var sb = new System.Text.StringBuilder();
        var name = string.IsNullOrWhiteSpace(restaurantName) ? "POS-in-NET" : restaurantName.Trim();
        var when = generatedAt ?? DateTime.Now;
        sb.AppendLine(Csv(name));
        sb.AppendLine(Csv($"Stock report generated - {when:dd MMM yyyy HH:mm}"));
        sb.AppendLine(Csv(string.IsNullOrWhiteSpace(report.PeriodLabel)
            ? $"{report.StartDate:dd MMM yyyy} – {report.EndDate:dd MMM yyyy}"
            : report.PeriodLabel));
        sb.AppendLine(Csv(
            $"Have {report.HaveTotalDisplay} · Use {report.UsedTotalDisplay} · Waste {report.WasteTotalDisplay}"));
        sb.AppendLine();
        sb.AppendLine("Section,Name,SKU,Have bottles,Have ml,Use bottles,Use ml,Waste bottles,Waste ml,Period start,Period end");
        var start = report.StartDate.ToString("yyyy-MM-dd");
        var end = report.EndDate.ToString("yyyy-MM-dd");
        foreach (var row in report.Items ?? Array.Empty<BarStockWeeklyReportRowDto>())
        {
            var pack = row.PackSize > 0 ? row.PackSize : 0m;
            var isBottle = string.Equals(
                BarStockUnits.Normalize(row.StockUnit),
                BarStockUnits.Bottle,
                StringComparison.OrdinalIgnoreCase);
            sb.Append(Csv(BarStockSections.DisplayName(row.Section))).Append(',');
            sb.Append(Csv(row.Name)).Append(',');
            sb.Append(Csv(row.Sku ?? string.Empty)).Append(',');
            sb.Append(BarStockQtyDisplay.FormatQty(row.EndingOnHand)).Append(',');
            sb.Append(isBottle && pack > 0 ? BarStockQtyDisplay.FormatQty(row.EndingOnHand * pack) : string.Empty).Append(',');
            sb.Append(BarStockQtyDisplay.FormatQty(row.UsedTotal)).Append(',');
            sb.Append(isBottle && pack > 0 ? BarStockQtyDisplay.FormatQty(row.UsedTotal * pack) : string.Empty).Append(',');
            sb.Append(BarStockQtyDisplay.FormatQty(row.WasteTotal)).Append(',');
            sb.Append(isBottle && pack > 0 ? BarStockQtyDisplay.FormatQty(row.WasteTotal * pack) : string.Empty).Append(',');
            sb.Append(start).Append(',');
            sb.Append(end);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}

/// <summary>Admin creates shelf stock on Bar Inventory (SKU + bottle↔ml calculator).</summary>
public sealed class BarStockCreateRequestDto
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Preset or Admin-typed category.</summary>
    public string Section { get; set; } = BarStockSections.Wine;
    /// <summary>ml in one bottle (e.g. 750) — stored as pack_size.</summary>
    public decimal MlPerBottle { get; set; } = 750m;
    /// <summary>Bottles added now (Stock IN) — stored as opening on-hand.</summary>
    public decimal OpeningBottles { get; set; }
    /// <summary>Mandatory reorder point (Suggest when on-hand ≤ Low).</summary>
    public decimal LowLevel { get; set; }
    /// <summary>Mandatory target fill (Suggest qty = Max − on-hand).</summary>
    public decimal MaxLevel { get; set; }
    /// <summary>Legacy: if MaxLevel is 0, treated as MaxLevel.</summary>
    public decimal ParLevel { get; set; }
}

public sealed class BarStockCreateResponseDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ErrorCode { get; set; }
    public BarStockItemDto? Stock { get; set; }
    public BarStockBoardRowDto? BoardRow { get; set; }
}

/// <summary>Admin edits shelf stock details (SKU / name / section / ml). On-hand unchanged.</summary>
public sealed class BarStockUpdateRequestDto
{
    public string StockId { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Section { get; set; } = BarStockSections.Wine;
    /// <summary>ml in one bottle — stored as pack_size.</summary>
    public decimal MlPerBottle { get; set; } = 750m;
    /// <summary>Mandatory reorder point.</summary>
    public decimal LowLevel { get; set; }
    /// <summary>Mandatory target fill.</summary>
    public decimal MaxLevel { get; set; }
}
