@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set WPF=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF
set XAML=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll
set ICON=%~dp0icon\Nanally.ico
set SRC=%~dp0Nanally.cs %~dp0Nanally.Ui.cs
set OUT=%~dp0Nanally.exe
if exist "%ICON%" (
  "%CSC%" /nologo /optimize+ /target:winexe /out:"%OUT%" /win32icon:"%ICON%" /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" /r:"%XAML%" /r:System.Windows.Forms.dll /r:System.Drawing.dll %SRC%
) else (
  "%CSC%" /nologo /optimize+ /target:winexe /out:"%OUT%" /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" /r:"%XAML%" /r:System.Windows.Forms.dll /r:System.Drawing.dll %SRC%
)
exit /b %ERRORLEVEL%
