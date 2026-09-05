namespace OrderWeb.SharedUI.Themes;

/// <summary>
/// Locks host application resources onto the SharedUI Pos* design system after
/// local dictionaries (OrderWebColors, Syncfusion, Client Colors) have merged.
/// </summary>
public static class DesignSystemBootstrap
{
    /// <summary>
    /// Call from Mother and Client <c>App</c> constructors after
    /// <c>InitializeComponent</c> so SharedUI Pos* tokens win for POS chrome.
    /// Syncfusion theme files stay Mother-only; this only reasserts shared
    /// semantic colour keys used by POS screens.
    /// </summary>
    public static void LockHostResources(ResourceDictionary applicationResources)
    {
        ArgumentNullException.ThrowIfNull(applicationResources);

        if (!ContainsKey(applicationResources, "PosPrimary"))
        {
            applicationResources.MergedDictionaries.Add(new OrderWebTheme());
        }

        var aliases = new OrderWebLegacyAliases();
        aliases.RegisterAliases(applicationResources);
        aliases.ReassertMotherPosKeys(applicationResources);
    }

    private static bool ContainsKey(ResourceDictionary resources, string key)
    {
        if (resources.ContainsKey(key))
            return true;

        foreach (var merged in resources.MergedDictionaries)
        {
            if (ContainsKey(merged, key))
                return true;
        }

        return false;
    }
}
