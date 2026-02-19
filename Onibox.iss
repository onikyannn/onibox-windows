#ifndef MyAppName
  #define MyAppName "Onibox"
#endif

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef MyAppExeName
  #define MyAppExeName "Onibox.exe"
#endif

#ifndef MyAppPublishDir
  #define MyAppPublishDir "Onibox\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\publish"
#endif

#ifndef MyAppOutputBaseFilename
  #define MyAppOutputBaseFilename "OniboxSetup"
#endif

#ifndef MyAppArch
  #define MyAppArch "x64"
#endif

#if MyAppArch == "arm64"
  #define MyArchitecturesAllowed "arm64"
  #define MyArchitecturesInstallIn64BitMode "arm64"
#else
  #define MyArchitecturesAllowed "x64compatible"
  #define MyArchitecturesInstallIn64BitMode "x64compatible"
#endif

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\Onibox
DefaultGroupName=Onibox
OutputBaseFilename={#MyAppOutputBaseFilename}
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed={#MyArchitecturesAllowed}
ArchitecturesInstallIn64BitMode={#MyArchitecturesInstallIn64BitMode}

[Files]
Source: "{#MyAppPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Onibox"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"

[Registry]
Root: HKCR; Subkey: "onibox"; ValueType: string; ValueName: ""; ValueData: "URL:Onibox Protocol"; Flags: uninsdeletekey
Root: HKCR; Subkey: "onibox"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCR; Subkey: "onibox\\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\\icon.ico"
Root: HKCR; Subkey: "onibox\\shell\\open\\command"; ValueType: string; ValueName: ""; ValueData: """{app}\\{#MyAppExeName}"" ""%1"""

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить Onibox"; Flags: nowait postinstall skipifsilent shellexec; Verb: "runas"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
