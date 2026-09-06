namespace OrderWeb.Contracts.Navigation;

public sealed record NavigationItemContract(
    string Route,
    string Title,
    string IconKey,
    IReadOnlySet<string> RequiredCapabilities,
    IReadOnlySet<string> RequiredFeatures,
    int SortOrder = 0);
