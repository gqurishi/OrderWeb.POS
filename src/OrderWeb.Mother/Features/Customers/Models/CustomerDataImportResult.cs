namespace POS_in_NET.Models;

public class CustomerDataImportResult
{
    public int Inserted { get; set; }

    public int Updated { get; set; }

    public int Skipped { get; set; }

    public List<string> Errors { get; } = new();

    public string Summary =>
        $"Inserted: {Inserted}, Updated: {Updated}, Skipped: {Skipped}" +
        (Errors.Count > 0 ? $", Errors: {Errors.Count}" : string.Empty);
}
