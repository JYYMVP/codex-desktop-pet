#ifndef AppVersion
  #define AppVersion "0.10.0"
#endif
#ifndef PublishDir
  #error PublishDir must point to the dotnet publish directory
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif

[Setup]
AppId={{2F23D50D-77A4-4F0A-B64A-80F4574D6F29}
AppName=Codex Desktop Pet
AppVersion={#AppVersion}
AppPublisher=JYYMVP
AppPublisherURL=https://github.com/JYYMVP/codex-desktop-pet
AppSupportURL=https://github.com/JYYMVP/codex-desktop-pet/issues
DefaultDirName={localappdata}\CodexDesktopPet\app
DefaultGroupName=Codex Desktop Pet
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=CodexDesktopPet-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=Codex Desktop Pet
VersionInfoVersion={#AppVersion}
VersionInfoCompany=JYYMVP
VersionInfoDescription=Codex Desktop Pet installer

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Codex Desktop Pet"; Filename: "{app}\CodexDesktopPet.exe"
Name: "{autodesktop}\Codex Desktop Pet"; Filename: "{app}\CodexDesktopPet.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Run]
Filename: "{app}\CodexDesktopPet.exe"; Description: "启动 Codex Desktop Pet"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
