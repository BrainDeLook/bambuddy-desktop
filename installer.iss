#ifndef AppVersion
  #define AppVersion "0.1.2"
#endif

[Setup]
AppId=BambuddyDesktop.Unofficial
AppName=Bambuddy Desktop
AppVersion={#AppVersion}
AppPublisher=Bambuddy Desktop (unofficial)
DefaultDirName={localappdata}\Programs\Bambuddy Desktop
DefaultGroupName=Bambuddy Desktop
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=dist
OutputBaseFilename=Bambuddy Desktop Setup
SetupIconFile=app.ico
UninstallDisplayIcon={app}\Bambuddy Desktop {#AppVersion}.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "dist\portable\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Bambuddy Desktop"; Filename: "{app}\Bambuddy Desktop.exe"; IconFilename: "{app}\Bambuddy Desktop {#AppVersion}.ico"
Name: "{autodesktop}\Bambuddy Desktop"; Filename: "{app}\Bambuddy Desktop.exe"; IconFilename: "{app}\Bambuddy Desktop {#AppVersion}.ico"
