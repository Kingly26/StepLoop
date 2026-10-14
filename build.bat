@echo off
rem Builds StepLoop (all files in src\) with the C# compiler included in .NET Framework 4.x.
rem The icon is icon\StepLoop.ico, generated from icon\logo.png with icon\make-icon-from-logo.cs
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ /out:StepLoop.exe /win32icon:icon\StepLoop.ico /resource:icon\StepLoop.ico,StepLoop.ico /r:System.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll src\*.cs
if errorlevel 1 (echo BUILD FAILED) else (echo Build OK: StepLoop.exe)
pause
