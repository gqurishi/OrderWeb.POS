using System.Text.Json;
using System.Text.Json.Serialization;
using OrderWeb.Contracts.Customers;

namespace OrderWeb.Client.Services.Customer;

public sealed class ClientCustomerFieldPolicyService
{
    private const string PolicyConfigKey = "customer_field_policy_json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ClientCacheService _cache;
    private CustomerFieldAccessPolicy? _memoryPolicy;

    public ClientCustomerFieldPolicyService(ClientCacheService cache)
    {
        _cache = cache;
    }

    public CustomerFieldAccessPolicy GetPolicy() =>
        _memoryPolicy ?? CustomerFieldAccessPolicy.ClientDefault;

    public async Task<CustomerFieldAccessPolicy> LoadPolicyAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _cache.GetDeviceConfigAsync(PolicyConfigKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            try
            {
                var dto = JsonSerializer.Deserialize<CustomerFieldPolicyDto>(stored, JsonOptions);
                if (dto is not null)
                {
                    _memoryPolicy = dto.ToPolicy();
                    return _memoryPolicy;
                }
            }
            catch
            {
            }
        }

        _memoryPolicy = CustomerFieldAccessPolicy.ClientDefault;
        return _memoryPolicy;
    }

    public async Task SavePolicyAsync(CustomerFieldAccessPolicy policy, CancellationToken cancellationToken = default)
    {
        _memoryPolicy = policy;
        await _cache.UpsertDeviceConfigAsync(
            PolicyConfigKey,
            JsonSerializer.Serialize(CustomerFieldPolicyDto.FromPolicy(policy), JsonOptions),
            cancellationToken);
    }

    internal sealed record CustomerFieldPolicyDto(
        IReadOnlyList<string>? SearchFields,
        IReadOnlyList<string>? CacheFields,
        IReadOnlyList<string>? DetailFields,
        bool AllowOrderHistory,
        bool AllowCustomerDirectory,
        bool AllowAssignCustomer)
    {
        public static CustomerFieldPolicyDto FromPolicy(CustomerFieldAccessPolicy policy) =>
            new(
                policy.SearchFields.Select(field => field.ToString()).ToList(),
                policy.CacheFields.Select(field => field.ToString()).ToList(),
                policy.DetailFields.Select(field => field.ToString()).ToList(),
                policy.AllowOrderHistory,
                policy.AllowCustomerDirectory,
                policy.AllowAssignCustomer);

        public CustomerFieldAccessPolicy ToPolicy() =>
            new(
                ParseFields(SearchFields),
                ParseFields(CacheFields),
                ParseFields(DetailFields),
                AllowOrderHistory,
                AllowCustomerDirectory,
                AllowAssignCustomer);

        private static IReadOnlyList<CustomerFieldKind> ParseFields(IReadOnlyList<string>? values)
        {
            if (values is null || values.Count == 0)
            {
                return Array.Empty<CustomerFieldKind>();
            }

            var fields = new List<CustomerFieldKind>();
            foreach (var value in values)
            {
                if (Enum.TryParse<CustomerFieldKind>(value, true, out var field))
                {
                    fields.Add(field);
                }
            }

            return fields;
        }
    }
}
