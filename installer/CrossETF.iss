#ifndef MyAppVersion
  #define MyAppVersion "8.10.10"
#endif
#ifndef SourceDir
  #error SourceDir must be provided by Build-CrossEtfInstaller.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be provided by Build-CrossEtfInstaller.ps1
#endif
#ifndef IconFile
  #error IconFile must be provided by Build-CrossEtfInstaller.ps1
#endif

#define MyAppName "跨境ETF智能投资决策系统"
#define MyAppExeName "跨境ETF.exe"
#define MyAppPublisher "CrossETF"

[Setup]
AppId={{C1935940-49E2-4F33-BAF2-70E991F37959}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription={#MyAppName} 安装程序
DefaultDirName={localappdata}\Programs\CrossETF
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=跨境ETF安装程序_v{#MyAppVersion}_win-x64
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
UsePreviousAppDir=yes
ChangesAssociations=no
ChangesEnvironment=no
MinVersion=10.0.17763

[Languages]
Name: "chinesesimplified"; MessagesFile: "{#SourcePath}\Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
