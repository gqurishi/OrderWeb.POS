using OrderWeb.SharedUI.Hosting;

namespace POS_in_NET.Services;

/// <summary>
/// Thin adapter so Mother Order Place uses the same SharedUI shell as Client.
/// Business logic stays on <see cref="Pages.OrderPlacementPageSimple"/>.
/// </summary>
public sealed class MotherOrderPlaceHost : IOrderPlaceHost
{
    private readonly Pages.OrderPlacementPageSimple _page;

    public MotherOrderPlaceHost(Pages.OrderPlacementPageSimple page)
    {
        _page = page;
        Session = page.OrderPlaceSession;
    }

    public OrderPlaceSessionState Session { get; }

    public event EventHandler? StateChanged
    {
        add => _page.OrderPlaceStateChanged += value;
        remove => _page.OrderPlaceStateChanged -= value;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        _page.OrderPlaceInitializeAsync(cancellationToken);

    public Task SelectCategoryAsync(string categoryId, CancellationToken cancellationToken = default) =>
        _page.OrderPlaceSelectCategoryAsync(categoryId, cancellationToken);

    public Task SelectSubcategoryAsync(string? subcategoryId, CancellationToken cancellationToken = default) =>
        _page.OrderPlaceSelectSubcategoryAsync(subcategoryId, cancellationToken);

    public Task AddProductAsync(string productId, CancellationToken cancellationToken = default) =>
        _page.OrderPlaceAddProductAsync(productId, cancellationToken);

    public Task SetLineQuantityAsync(string lineId, int quantity, CancellationToken cancellationToken = default) =>
        _page.OrderPlaceSetLineQuantityAsync(lineId, quantity, cancellationToken);

    public Task EditLineNoteAsync(string lineId, CancellationToken cancellationToken = default) =>
        _page.OrderPlaceEditLineNoteAsync(lineId, cancellationToken);

    public Task TrailingLineActionAsync(string lineId, CancellationToken cancellationToken = default) =>
        _page.OrderPlaceTrailingLineActionAsync(lineId, cancellationToken);

    public Task OrderNotesAsync(CancellationToken cancellationToken = default) =>
        _page.OrderPlaceOrderNotesAsync(cancellationToken);

    public Task VoidAsync(CancellationToken cancellationToken = default) =>
        _page.OrderPlaceVoidAsync(cancellationToken);

    public Task MoreAsync(CancellationToken cancellationToken = default) =>
        _page.OrderPlaceMoreAsync(cancellationToken);

    public Task SendAsync(CancellationToken cancellationToken = default) =>
        _page.OrderPlaceSendAsync(cancellationToken);

    public Task PrintAsync(CancellationToken cancellationToken = default) =>
        _page.OrderPlacePrintAsync(cancellationToken);

    public Task PayAsync(CancellationToken cancellationToken = default) =>
        _page.OrderPlacePayAsync(cancellationToken);
}
