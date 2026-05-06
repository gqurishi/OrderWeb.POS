# POS-in-NET - Restaurant Point of Sale System

A comprehensive .NET MAUI cross-platform restaurant point-of-sale application with modern UI and advanced features.

## 🚀 Features

### Core POS Functionality
- **Order Management**: Full order lifecycle from placement to completion
- **Table Service**: Visual floor plans with table management
- **Menu Management**: Categories, items, modifiers, and add-ons
- **Multi-Payment Support**: Cash, card, gift cards, and tips
- **Receipt Printing**: Kitchen orders and customer receipts

### Business Management
- **Business Information**: Complete business profile with logo upload
- **User Management**: Role-based access control
- **Customer Management**: Delivery, collection, and loyalty customers
- **VAT System**: Comprehensive tax calculation and reporting
- **Gift Card System**: Issue and redeem gift cards

### Advanced Features
- **OrderWeb Integration**: Online ordering system integration
- **Print Groups**: Intelligent kitchen order routing
- **Label Printing**: Brother label printer integration
- **Postcode Lookup**: Address validation and autocomplete
- **Cloud Sync**: Real-time data synchronization
- **Offline Queue**: Handles offline operations with auto-sync

### Technical Features
- **Modern UI**: Professional XAML design with custom styling
- **Database**: SQLite with comprehensive migration system
- **Network Printing**: ESC/POS printer support
- **WebSocket Integration**: Real-time order updates
- **Background Services**: Automatic sync and cleanup tasks

## 🛠️ Technical Stack

- **.NET MAUI**: Cross-platform framework (Windows, macOS, iOS, Android)
- **C# 12**: Modern C# with latest language features
- **XAML**: Declarative UI with custom controls and converters
- **SQLite**: Embedded database with Entity Framework
- **WebSockets**: Real-time communication
- **Network APIs**: RESTful services and cloud integration

## 📋 Prerequisites

- **.NET 9.0 SDK** or later
- **Visual Studio 2022** or **VS Code** with MAUI workload
- **macOS** (for iOS/Mac development) or **Windows** (for Windows/Android)

## 🚀 Getting Started

### Clone the Repository
```bash
git clone https://github.com/gqurishi/POS-in-NET.git
cd POS-in-NET
```

### Restore Dependencies
```bash
dotnet restore
```

### Build the Project
```bash
dotnet build
```

### Run the Application
```bash
dotnet run --framework net9.0-maccatalyst
```

For other platforms:
- Windows: `net9.0-windows10.0.19041.0`
- Android: `net9.0-android`
- iOS: `net9.0-ios`

## 📁 Project Structure

```
├── Controllers/           # Business logic controllers
├── Database/             # SQL scripts and migrations
├── Models/               # Data models and entities
├── Pages/                # XAML pages and views
├── Services/             # Business services and APIs
├── Views/                # Custom controls and dialogs
├── Resources/            # Images, fonts, and styles
└── Platforms/            # Platform-specific code
```

### Key Components

- **UnifiedSettingsPage**: Central settings with business info and logo upload
- **FoodMenuManagement**: Complete menu and category management
- **OrderManagement**: Order processing and history
- **PrinterSetup**: Network printer configuration
- **CloudSettings**: OrderWeb integration setup

## 🗄️ Database

The application uses SQLite with a comprehensive schema supporting:
- Menu items and categories
- Orders and customer management
- Print groups and printer assignments
- User management and permissions
- VAT calculations and reporting
- Table sessions and floor plans

### Database Migrations
All database migrations are in the `Database/` folder and run automatically on startup.

## 🎨 UI Features

- **Modern Design**: Clean, professional interface
- **Custom Controls**: Toast notifications, modern dialogs
- **Responsive Layout**: Adapts to different screen sizes
- **Professional Styling**: Consistent branding and colors
- **Logo Upload**: Business branding with image validation

## 🔧 Configuration

### Business Setup
1. Navigate to **Settings > Business Information**
2. Upload your business logo (max 5MB)
3. Complete business details (address, VAT, contact info)
4. Configure payment methods and receipt settings

### OrderWeb Integration
1. Go to **Settings > OrderWeb**
2. Configure API endpoints and credentials
3. Set up automatic order pulling
4. Configure print groups for kitchen orders

### Printer Setup
1. Access **Settings > Printers**
2. Add network printers with IP addresses
3. Configure print groups for different order types
4. Test printer connections

## 📱 Platform Support

- **Windows**: Full desktop experience
- **macOS**: Native Mac application via Mac Catalyst
- **iOS**: iPad and iPhone support (requires Apple Developer account)
- **Android**: Android 7.0+ support

## 🔒 Security Features

- Role-based user management
- Secure API communication
- Local data encryption options
- Audit trail for all transactions

## 📊 Reporting

- Daily sales reports
- VAT breakdown reports
- Customer analytics
- Print job tracking
- Order history with search filters

## 🛠️ Development

### Adding New Features
1. Create models in `Models/` folder
2. Add services in `Services/` folder
3. Create pages in `Pages/` folder
4. Update database schema if needed
5. Add navigation in `AppShell.xaml`

### Debugging
- Use Visual Studio debugger with breakpoints
- Check console output for logging information
- Database can be inspected with SQLite browser tools

## 📄 Documentation

Additional documentation available:
- `PRINT_GROUP_IMPLEMENTATION.md` - Print system setup
- `VAT_SYSTEM_SUMMARY.md` - VAT calculation guide
- `POSTCODE_LOOKUP_GUIDE.md` - Address lookup setup
- `LABEL_PRINTER_INTEGRATION.md` - Label printing guide
- `ORDER_LIFECYCLE_PHASE1.md` - Canonical local POS order lifecycle contract
- `ORDER_LIFECYCLE_PHASE2_MARIADB.md` - MariaDB schema contract for lifecycle, payments, and audit logs

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly
5. Submit a pull request

## 📝 License

This project is proprietary software. All rights reserved.

## 📞 Support

For support and questions, please create an issue in this repository.

---

**Built with ❤️ using .NET MAUI**