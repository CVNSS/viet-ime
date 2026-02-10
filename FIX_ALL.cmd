@echo off
setlocal EnableExtensions
cd /d C:\Users\Admin\viet-ime

echo ===============================
echo 1) FIND OLD AUTHOR TEXT
echo ===============================
powershell -NoProfile -ExecutionPolicy Bypass -Command "$hits=Get-ChildItem src -Recurse -Include *.xaml,*.cs,*.resx,*.xml,*.txt | Select-String -SimpleMatch 'Đỗ Nam' -EA SilentlyContinue; $hits | ForEach-Object { '{0}:{1}:{2}' -f $_.Path,$_.LineNumber,$_.Line.Trim() }; Write-Host ('TOTAL_HITS='+$hits.Count)"

echo ===============================
echo 2) FORCE REPLACE (XAML/CS/RESX/XML/TXT)
echo ===============================
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem src -Recurse -Include *.xaml,*.cs,*.resx,*.xml,*.txt | ForEach-Object { $p=$_.FullName; $t=Get-Content $p -Raw -EA SilentlyContinue; if($null -eq $t){return}; $t2=$t -replace 'Đỗ Nam','Long Ngo' -replace 'Teacher\s*THCS\s*Duyên\s*Thái','Long Ngo, 2026' -replace 'Tác giả\s*:\s*[^<\r\n]*','Tác giả: Long Ngo, 2026'; if($t2 -ne $t){ Set-Content $p $t2 -Encoding UTF8 } }"

echo ===============================
echo 3) CONFIRM NO OLD TEXT LEFT
echo ===============================
powershell -NoProfile -ExecutionPolicy Bypass -Command "$hits=Get-ChildItem src -Recurse -Include *.xaml,*.cs,*.resx,*.xml,*.txt | Select-String -SimpleMatch 'Đỗ Nam' -EA SilentlyContinue; Write-Host ('TOTAL_HITS_AFTER='+$hits.Count)"

echo ===============================
echo 4) CREATE RED FLAG ICON (procedural vector -> PNG -> REAL ICO)
echo ===============================
if not exist assets mkdir assets
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -AssemblyName System.Drawing; $size=1024; $bmp=New-Object System.Drawing.Bitmap $size,$size; $g=[System.Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::HighQuality; $g.Clear([System.Drawing.Color]::FromArgb(200,0,0)); $cx=$size/2; $cy=$size/2; $outer=380; $inner=155; $pts=New-Object 'System.Drawing.PointF[]' 10; for($i=0;$i -lt 10;$i++){ $a=($i*36-90)*[Math]::PI/180; $r= if($i%2 -eq 0){$outer}else{$inner}; $pts[$i]=New-Object System.Drawing.PointF($cx+[Math]::Cos($a)*$r,$cy+[Math]::Sin($a)*$r) }; $brush=New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,255,212,0)); $g.FillPolygon($brush,$pts); $bmp.Save('assets\cvnss.png',[System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()"

where magick >nul 2>nul
if errorlevel 1 (
  echo Installing ImageMagick...
  winget install ImageMagick.Q16 --silent --accept-package-agreements --accept-source-agreements
)

set "MAGICK="
for /f "delims=" %%M in ('where magick 2^>nul') do if not defined MAGICK set "MAGICK=%%M"
if not defined MAGICK (
  for /f "delims=" %%M in ('dir /b /s "C:\Program Files\ImageMagick*\magick.exe" 2^>nul') do if not defined MAGICK set "MAGICK=%%M"
)
if not defined MAGICK (
  echo ERROR: magick not found. Close CMD, open CMD again, re-run FIX_ALL.cmd
  exit /b 1
)

"%MAGICK%" assets\cvnss.png -define icon:auto-resize=256,128,64,48,32,16 assets\cvnss.ico

echo ===============================
echo 5) FORCE ICON + METADATA INTO CSPROJ
echo ===============================
copy /y assets\cvnss.ico src\VietIME.App\cvnss.ico >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p='src\VietIME.App\VietIME.App.csproj'; [xml]$x=Get-Content $p; $pg=($x.Project.PropertyGroup | Select-Object -First 1); function SetNode($n,$v){ $node=$pg.$n; if(-not $node){ $node=$x.CreateElement($n); $pg.AppendChild($node)|Out-Null }; $node.InnerText=$v }; SetNode 'AssemblyName' 'VietIME'; SetNode 'Authors' 'Long Ngo'; SetNode 'Company' 'Long Ngo'; SetNode 'Product' 'VietIME CVNSS4.0'; SetNode 'Copyright' '© Long Ngo, 2026'; SetNode 'ApplicationIcon' 'cvnss.ico'; $x.Save($p)"

echo ===============================
echo 6) BUILD SINGLE FILE EXE -> release\VietIME.exe
echo ===============================
dotnet clean
dotnet restore
dotnet publish src\VietIME.App\VietIME.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o release

echo ===============================
echo 7) SHOW VERSIONINFO (PROOF)
echo ===============================
powershell -NoProfile -ExecutionPolicy Bypass -Command "(Get-Item .\release\VietIME.exe).VersionInfo | Format-List ProductName,ProductVersion,CompanyName,LegalCopyright"

echo ===============================
echo 8) GIT SYNC TO GITHUB
echo ===============================
git add .
git commit -m "Update author Long Ngo 2026 + red star icon" 2>nul
git pull --rebase origin master
git push

echo ===============================
echo DONE. RUN THIS EXACT FILE:
echo %CD%\release\VietIME.exe
echo ===============================
pause
