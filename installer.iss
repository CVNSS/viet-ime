[Setup]
AppName=VietIME CVNSS4.0
AppVersion=4.0.0
DefaultDirName={pf}\VietIME
DefaultGroupName=VietIME
OutputDir=installer
OutputBaseFilename=VietIME-CVNSS4.0-Setup
Compression=lzma
SolidCompression=yes

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{group}\VietIME"; Filename: "{app}\VietIME.exe"
Name: "{commondesktop}\VietIME"; Filename: "{app}\VietIME.exe"
