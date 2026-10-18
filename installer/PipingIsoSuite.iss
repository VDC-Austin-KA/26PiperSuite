; Piping ISO Suite - Revit 2026 add-in installer (Inno Setup 6)
; Build: build\build.ps1  (or ISCC.exe /DAppVersion=1.0.0 /DSourceDir=<publish dir> installer\PipingIsoSuite.iss)
;
; Install modes (chosen on the first page, or /ALLUSERS | /CURRENTUSER on the command line):
;   All users    -> C:\ProgramData\Autodesk\Revit\Addins\2026\      (requires admin)
;   Current user -> %AppData%\Autodesk\Revit\Addins\2026\

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\addin"
#endif

[Setup]
AppId={{5C1F6B2E-7D4A-4E0B-9B53-2A6E8C1D4F70}
AppName=Piping ISO Suite for Revit 2026
AppVersion={#AppVersion}
AppPublisher=PipingIsoSuite
DefaultDirName={autoappdata}\Autodesk\Revit\Addins\2026
DisableDirPage=yes
DisableProgramGroupPage=yes
DirExistsWarning=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
OutputDir=..\dist
OutputBaseFilename=PipingIsoSuite-Revit2026-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=Piping ISO Suite for Revit 2026
; Revit locks the add-in DLL while running.
CloseApplications=yes
RestartApplications=no

[Files]
Source: "{#SourceDir}\PipingIsoSuite.addin"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*.dll"; DestDir: "{app}\PipingIsoSuite"; Flags: ignoreversion
Source: "{#SourceDir}\*.pdb"; DestDir: "{app}\PipingIsoSuite"; Flags: ignoreversion skipifsourcedoesntexist

[UninstallDelete]
Type: dirifempty; Name: "{app}\PipingIsoSuite"

[Code]
function RevitExeFromRegistry(var Path: String): Boolean;
var
  Keys: TArrayOfString;
  I: Integer;
  Location: String;
begin
  Result := False;
  { Revit registers each product/language under HKLM\SOFTWARE\Autodesk\Revit\2026\<product key>. }
  if RegGetSubkeyNames(HKLM64, 'SOFTWARE\Autodesk\Revit\2026', Keys) then
    for I := 0 to GetArrayLength(Keys) - 1 do
      if RegQueryStringValue(HKLM64, 'SOFTWARE\Autodesk\Revit\2026\' + Keys[I], 'InstallationLocation', Location) then
        if FileExists(AddBackslash(Location) + 'Revit.exe') then
        begin
          Path := AddBackslash(Location) + 'Revit.exe';
          Result := True;
          Exit;
        end;
end;

function FindRevit2026(var Path: String): Boolean;
begin
  Result := RevitExeFromRegistry(Path);
  if not Result then
  begin
    Path := ExpandConstant('{commonpf64}\Autodesk\Revit 2026\Revit.exe');
    Result := FileExists(Path);
  end;
end;

function InitializeSetup(): Boolean;
var
  RevitPath: String;
begin
  Result := True;
  if FindRevit2026(RevitPath) then
    Log('Revit 2026 found: ' + RevitPath)
  else if not WizardSilent() then
    Result := MsgBox('Autodesk Revit 2026 was not found on this computer.' + #13#10 +
      'Install the add-in anyway (e.g. Revit will be installed later)?', mbConfirmation, MB_YESNO) = IDYES
  else
    Log('Revit 2026 not found; continuing silent install.');
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := 'Add-in folder:' + NewLine + Space + ExpandConstant('{app}');
end;
