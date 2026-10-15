@echo off
rem 用 Windows 內建的 csc 編譯（不需要另外安裝任何東西）
chcp 65001 >nul
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /platform:x64 /out:"..\GG2修改器.exe" /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /resource:names.json,names.json *.cs
if errorlevel 1 (echo 編譯失敗 & pause & exit /b 1)
echo 完成：..\GG2修改器.exe
