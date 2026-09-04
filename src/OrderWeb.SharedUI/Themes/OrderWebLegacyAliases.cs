using System.Collections.Generic;

namespace OrderWeb.SharedUI.Themes;

/// <summary>
/// Registers temporary aliases from legacy Mother / Ow* resource keys onto the
/// authoritative Pos* design tokens. Remove aliases only after call sites migrate.
/// </summary>
public sealed class OrderWebLegacyAliases
{
    public void RegisterAliases(ResourceDictionary target)
    {
        ArgumentNullException.ThrowIfNull(target);

        // Ow* colour aliases (existing SharedUI controls)
        Alias(target, "OwPrimary", "PosPrimary");
        Alias(target, "OwPrimaryPressed", "PosPrimaryPressed");
        Alias(target, "OwPrimarySoft", "PosPrimarySoft");
        Alias(target, "OwPrimarySoftBorder", "PosPrimarySoftBorder");
        Alias(target, "OwBackground", "PosBackground");
        Alias(target, "OwSurface", "PosSurface");
        Alias(target, "OwSurfaceMuted", "PosSurfaceMuted");
        Alias(target, "OwSurfaceStrong", "PosSurfaceStrong");
        Alias(target, "OwTextPrimary", "PosTextPrimary");
        Alias(target, "OwTextStrong", "PosTextStrong");
        Alias(target, "OwTextSecondary", "PosTextSecondary");
        Alias(target, "OwTextMuted", "PosTextMuted");
        Alias(target, "OwTextPlaceholder", "PosTextPlaceholder");
        Alias(target, "OwTextOnPrimary", "PosTextOnPrimary");
        Alias(target, "OwBorder", "PosBorder");
        Alias(target, "OwBorderStrong", "PosBorderStrong");
        Alias(target, "OwInputBorder", "PosInputBorder");
        Alias(target, "OwFocus", "PosFocus");
        Alias(target, "OwShadow", "PosShadow");
        Alias(target, "OwOverlay", "PosOverlay");
        Alias(target, "OwSuccess", "PosSuccess");
        Alias(target, "OwSuccessStrong", "PosSuccessStrong");
        Alias(target, "OwSuccessSoft", "PosSuccessSoft");
        Alias(target, "OwSuccessText", "PosSuccessText");
        Alias(target, "OwWarning", "PosWarning");
        Alias(target, "OwWarningStrong", "PosWarningStrong");
        Alias(target, "OwWarningSoft", "PosWarningSoft");
        Alias(target, "OwWarningBorder", "PosWarningBorder");
        Alias(target, "OwWarningText", "PosWarningText");
        Alias(target, "OwError", "PosError");
        Alias(target, "OwErrorStrong", "PosErrorStrong");
        Alias(target, "OwErrorSoft", "PosErrorSoft");
        Alias(target, "OwErrorBorder", "PosErrorBorder");
        Alias(target, "OwErrorText", "PosErrorText");
        Alias(target, "OwInfo", "PosInfo");
        Alias(target, "OwInfoSoft", "PosInfoSoft");
        Alias(target, "OwInfoText", "PosInfoText");
        Alias(target, "OwOnline", "PosOnline");
        Alias(target, "OwOffline", "PosOffline");
        Alias(target, "OwConnecting", "PosConnecting");
        Alias(target, "OwPrimaryBrush", "PosPrimaryBrush");
        Alias(target, "OwBackgroundBrush", "PosBackgroundBrush");
        Alias(target, "OwSurfaceBrush", "PosSurfaceBrush");
        Alias(target, "OwBorderBrush", "PosBorderBrush");

        // Ow* typography / dimensions
        Alias(target, "OwFontRegular", "PosFontRegular");
        Alias(target, "OwFontSemibold", "PosFontSemibold");
        Alias(target, "OwFontBold", "PosFontBold");
        Alias(target, "OwFontSizeCaption", "PosFontSizeCaption");
        Alias(target, "OwFontSizeSmall", "PosFontSizeSmall");
        Alias(target, "OwFontSizeBody", "PosFontSizeBody");
        Alias(target, "OwFontSizeInput", "PosFontSizeInput");
        Alias(target, "OwFontSizeButton", "PosFontSizeButton");
        Alias(target, "OwFontSizeCardTitle", "PosFontSizeCardTitle");
        Alias(target, "OwFontSizeSectionTitle", "PosFontSizeSectionTitle");
        Alias(target, "OwFontSizePageTitle", "PosFontSizePageTitle");
        Alias(target, "OwFontSizeDisplayTitle", "PosFontSizeDisplayTitle");
        Alias(target, "OwBodyTextStyle", "PosBodyTextStyle");
        Alias(target, "OwCaptionTextStyle", "PosCaptionTextStyle");
        Alias(target, "OwCardTitleStyle", "PosCardTitleStyle");
        Alias(target, "OwSectionTitleStyle", "PosSectionTitleStyle");
        Alias(target, "OwPageTitleStyle", "PosPageTitleStyle");

        Alias(target, "OwPagePadding", "PosPagePadding");
        Alias(target, "OwPagePaddingCompact", "PosPagePaddingCompact");
        Alias(target, "OwCardPadding", "PosCardPadding");
        Alias(target, "OwDialogPadding", "PosDialogPadding");
        Alias(target, "OwButtonPadding", "PosButtonPadding");
        Alias(target, "OwSpacingXs", "PosSpacingXs");
        Alias(target, "OwSpacingSm", "PosSpacingSm");
        Alias(target, "OwSpacingMd", "PosSpacingMd");
        Alias(target, "OwSpacingLg", "PosSpacingLg");
        Alias(target, "OwSpacingXl", "PosSpacingXl");
        Alias(target, "OwSpacing2Xl", "PosSpacing2Xl");
        Alias(target, "OwButtonHeight", "PosButtonHeight");
        Alias(target, "OwButtonHeightLarge", "PosButtonHeightLarge");
        Alias(target, "OwInputHeight", "PosInputHeight");
        Alias(target, "OwInputHeightLarge", "PosInputHeightLarge");
        Alias(target, "OwTouchTargetMinimum", "PosTouchTargetMinimum");
        Alias(target, "OwCornerRadiusSmall", "PosCornerRadiusSmall");
        Alias(target, "OwCornerRadius", "PosCornerRadius");
        Alias(target, "OwCornerRadiusLarge", "PosCornerRadiusLarge");
        Alias(target, "OwDialogCornerRadius", "PosDialogCornerRadius");
        Alias(target, "OwBorderThickness", "PosBorderThickness");
        Alias(target, "OwIconSizeSmall", "PosIconSizeSmall");
        Alias(target, "OwIconSize", "PosIconSize");
        Alias(target, "OwIconSizeLarge", "PosIconSizeLarge");
        Alias(target, "OwDialogIconSize", "PosDialogIconSize");

        // Ow* component styles
        Alias(target, "OwCardShadow", "PosCardShadow");
        Alias(target, "OwDialogShadow", "PosDialogShadow");
        Alias(target, "OwCardStyle", "PosCardStyle");
        Alias(target, "OwPrimaryButtonStyle", "PosPrimaryButtonStyle");
        Alias(target, "OwSecondaryButtonStyle", "PosSecondaryButtonStyle");
        Alias(target, "OwSuccessButtonStyle", "PosSuccessButtonStyle");
        Alias(target, "OwDangerButtonStyle", "PosDangerButtonStyle");
        Alias(target, "OwEntryStyle", "PosEntryStyle");
        Alias(target, "OwDialogShellStyle", "PosDialogShellStyle");
        Alias(target, "OwStatusSuccessPanelStyle", "PosStatusSuccessPanelStyle");
        Alias(target, "OwStatusWarningPanelStyle", "PosStatusWarningPanelStyle");
        Alias(target, "OwStatusErrorPanelStyle", "PosStatusErrorPanelStyle");
        Alias(target, "OwStatusInfoPanelStyle", "PosStatusInfoPanelStyle");
        Alias(target, "OwStatusSuccessTextStyle", "PosStatusSuccessTextStyle");
        Alias(target, "OwStatusWarningTextStyle", "PosStatusWarningTextStyle");
        Alias(target, "OwStatusErrorTextStyle", "PosStatusErrorTextStyle");

        // Mother dialog / chrome keys
        Alias(target, "ButtonPrimary", "PosPrimary");
        Alias(target, "ButtonPrimaryHover", "PosPrimaryPressed");
        Alias(target, "ButtonSecondary", "PosSecondary");
        Alias(target, "ButtonSecondaryHover", "PosSecondaryPressed");
        Alias(target, "ButtonSuccess", "PosSuccessStrong");
        Alias(target, "ButtonDanger", "PosErrorStrong");
        Alias(target, "HeaderBackground", "PosHeaderBackground");
        Alias(target, "HeaderForeground", "PosHeaderForeground");
        Alias(target, "HeaderSubtext", "PosHeaderSubtext");
        Alias(target, "WebSocketConnected", "PosOnline");
        Alias(target, "WebSocketDisconnected", "PosOffline");
        Alias(target, "WebSocketConnecting", "PosConnecting");
        Alias(target, "DialogPrimaryColor", "PosPrimary");
        Alias(target, "DialogSuccessColor", "PosSuccessStrong");
        Alias(target, "DialogOverlayColor", "PosOverlay");
        Alias(target, "DialogTitleColor", "PosTextStrong");
        Alias(target, "DialogMessageColor", "PosTextMuted");
        Alias(target, "DialogCancelBackgroundColor", "PosSurfaceStrong");
        Alias(target, "DialogCancelTextColor", "PosTextSecondary");
        Alias(target, "DialogInputBackgroundColor", "PosSurfaceMuted");
        Alias(target, "DialogInputStrokeColor", "PosInputBorder");
        Alias(target, "DialogShellStrokeColor", "PosBorder");
        Alias(target, "DialogFieldLabelColor", "PosTextMuted");

        // Client colour keys
        Alias(target, "ClientBackground", "PosBackground");
        Alias(target, "ClientTopBarBackground", "PosSurface");
        Alias(target, "ClientSurface", "PosSurfaceMuted");
        Alias(target, "ClientText", "PosTextPrimary");
        Alias(target, "ClientSecondaryText", "PosTextSecondary");
        Alias(target, "ClientMutedText", "PosTextMuted");
        Alias(target, "ClientSidebarText", "PosTextStrong");
        Alias(target, "ClientAccent", "PosAccent");
        Alias(target, "ClientDashboardLabel", "PosPrimaryPressed");
        Alias(target, "ClientSelected", "PosSurfaceSelected");
    }

    /// <summary>
    /// Re-asserts POS semantic keys after host dictionaries (Syncfusion / local
    /// OrderWebColors) so SharedUI remains the runtime source of truth for POS chrome.
    /// </summary>
    public void ReassertMotherPosKeys(ResourceDictionary applicationResources)
    {
        ArgumentNullException.ThrowIfNull(applicationResources);

        var map = new Dictionary<string, string>
        {
            ["Primary"] = "PosPrimary",
            ["PrimaryForeground"] = "PosTextOnPrimary",
            ["PrimaryContainer"] = "PosPrimarySoft",
            ["Background"] = "PosBackground",
            ["Foreground"] = "PosTextPrimary",
            ["Secondary"] = "PosPrimarySoft",
            ["SecondaryForeground"] = "PosInfoText",
            ["Card"] = "PosSurface",
            ["Border"] = "PosBorder",
            ["Input"] = "PosInputBackground",
            ["Ring"] = "PosRing",
            ["Muted"] = "PosSurfaceMuted",
            ["MutedForeground"] = "PosTextMuted",
            ["Accent"] = "PosBorder",
            ["Success"] = "PosSuccessSoft",
            ["SuccessForeground"] = "PosSuccessForeground",
            ["Destructive"] = "PosDestructiveSoft",
            ["DestructiveForeground"] = "PosDestructiveForeground",
            ["SidebarBackground"] = "PosSidebarBackground",
            ["SidebarForeground"] = "PosSidebarForeground",
            ["SidebarPrimary"] = "PosSidebarPrimary",
            ["SidebarBorder"] = "PosSidebarBorder",
            ["ButtonPrimary"] = "PosPrimary",
            ["ButtonPrimaryHover"] = "PosPrimaryPressed",
            ["ButtonSecondary"] = "PosSecondary",
            ["ButtonSecondaryHover"] = "PosSecondaryPressed",
            ["ButtonSuccess"] = "PosSuccessStrong",
            ["ButtonDanger"] = "PosErrorStrong",
            ["HeaderBackground"] = "PosHeaderBackground",
            ["HeaderForeground"] = "PosHeaderForeground",
            ["HeaderSubtext"] = "PosHeaderSubtext",
            ["WebSocketConnected"] = "PosOnline",
            ["WebSocketDisconnected"] = "PosOffline",
            ["WebSocketConnecting"] = "PosConnecting",
        };

        foreach (var pair in map)
            Alias(applicationResources, pair.Key, pair.Value);
    }

    private static void Alias(ResourceDictionary target, string aliasKey, string targetKey)
    {
        if (!TryFind(target, targetKey, out var value) || value is null)
            return;

        target[aliasKey] = value;
    }

    private static bool TryFind(ResourceDictionary dictionary, string key, out object? value)
    {
        if (dictionary.ContainsKey(key))
        {
            value = dictionary[key];
            return true;
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (TryFind(merged, key, out value))
                return true;
        }

        value = null;
        return false;
    }
}
