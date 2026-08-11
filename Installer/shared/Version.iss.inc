; Shared product/version constants for OrderWeb POS Inno Setup scripts.
; Override at compile time with /DMyAppVersion=1.2.3 when needed.

#ifndef MyAppName
  #define MyAppName "OrderWeb POS"
#endif

#ifndef MyAppPublisher
  #define MyAppPublisher "OrderWeb"
#endif

#ifndef MyAppVersion
  #define MyAppVersion "1.0.1"
#endif

#ifndef MyAppExeName
  #define MyAppExeName "POS-in-NET.exe"
#endif

#ifndef DatabaseSetupExe
  #define DatabaseSetupExe "OrderWeb.DatabaseSetup.exe"
#endif

#ifndef PublishRoot
  ; Relative to Installer\ when compiling .iss files.
  #define PublishRoot "..\publish\win-x64"
#endif

#ifndef MariaDbMsiPattern
  #define MariaDbMsiPattern "Prerequisites\MariaDB\*.msi"
#endif

#ifndef ConfigFolderName
  #define ConfigFolderName "OrderWebPOS"
#endif

#ifndef ConfigFileName
  #define ConfigFileName "orderweb-database.json"
#endif
