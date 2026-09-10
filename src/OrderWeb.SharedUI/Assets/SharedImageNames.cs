namespace OrderWeb.SharedUI.Assets;

/// <summary>
/// Stable packaged image names shared by Mother and Client. Dynamic restaurant images
/// use API/cache paths and must not be added to this catalog.
/// </summary>
public static class SharedImageNames
{
    public const string CompanyLogo = "companylogo.png";
    public const string CompanyMark = "companymark.png";
    public const string Home = "companymark.png";
    public const string Logout = "outred.png";
    public const string DefaultFood = "default_food.png";
    public const string DefaultTable = "table_1.png";

    public static class Payment
    {
        public const string Cash = "payment_cash.png";
        public const string Card = "payment_card.png";
        public const string GiftCard = "payment_gift_card.png";
    }

    public static class Status
    {
        public const string Online = "status_online.png";
        public const string Offline = "status_offline.png";
        public const string Pending = "status_pending.png";
        public const string Success = "status_success.png";
        public const string Warning = "status_warning.png";
        public const string Error = "status_error.png";
    }

    public static class Dashboard
    {
        public const string DashboardHome = "dashboard.png";
        public const string Restaurant = "restaurant.png";
        public const string Collection = "collection.png";
        public const string Delivery = "delivery.png";
        public const string LiveOrders = "liveorder.png";
        public const string OrderHistory = "orderhistory.png";
        public const string Customers = "customers.png";
        public const string Reservations = "reservation.png";
        public const string GiftCards = "giftcards.png";
        public const string Loyalty = "loyalty.png";
        public const string Reports = "report.png";
        public const string Printers = "printers.png";
        public const string Settings = "settings.png";
    }
}
