#ifndef Payload
  #define Payload "..\artifacts\publish"
#endif
[Setup]
AppId={{D27C2F53-9096-4D47-993E-24D7A73B13DA}
AppName=Windows Keyboard Integrity Check
AppVersion=1.0.0
AppPublisher=UNSA-studio
AppPublisherURL=mailto:UNSA-studio@outlook.com
AppSupportURL=mailto:UNSA-studio@outlook.com
DefaultDirName={autopf}\UNSA-studio\WKIC
DefaultGroupName=Windows Keyboard Integrity Check
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\WindowsKeyboardIntegrityCheck.exe
OutputDir=..\artifacts\installer
OutputBaseFilename=WindowsKeyboardIntegrityCheck
SetupIconFile=..\assets\app.ico
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Windows Keyboard Integrity Check"; Filename: "{app}\WindowsKeyboardIntegrityCheck.exe"; IconFilename: "{app}\WindowsKeyboardIntegrityCheck.exe"
Name: "{group}\Uninstall"; Filename: "{uninstallexe}"
[Messages]
WelcomeLabel2=This installer includes the .NET and Windows App SDK runtimes.%n%nDeveloper: UNSA-studio%nEmail: UNSA-studio@outlook.com%n%nThe application must be run as administrator for keyboard testing.
