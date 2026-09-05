using OrderWeb.Contracts.Customers;

namespace POS_in_NET.Services;

/// <summary>
/// Mother-controlled customer field visibility for Client terminals.
/// Persisted in app preferences until a dedicated settings table exists.
/// </summary>
public sealed class MotherClientCustomerFieldPolicyService
{
    public const string PreferenceKey = "client_customer_field_policy";

    public const string PolicyClientDefault = "ClientDefault";
    public const string PolicyClientWithDeliveryAddress = "ClientWithDeliveryAddress";

    public CustomerFieldAccessPolicy GetClientPolicy()
    {
        var stored = Preferences.Default.Get(PreferenceKey, PolicyClientDefault);
        return stored switch
        {
            PolicyClientWithDeliveryAddress => CustomerFieldAccessPolicy.ClientWithDeliveryAddress,
            _ => CustomerFieldAccessPolicy.ClientDefault
        };
    }

    public void SetClientPolicy(CustomerFieldAccessPolicy policy)
    {
        var value = ReferenceEquals(policy, CustomerFieldAccessPolicy.ClientWithDeliveryAddress)
            || policy.SearchFields.Contains(CustomerFieldKind.Address)
                ? PolicyClientWithDeliveryAddress
                : PolicyClientDefault;
        Preferences.Default.Set(PreferenceKey, value);
    }
}
