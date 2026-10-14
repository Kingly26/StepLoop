@echo off
rem Compila StepLoop (tutti i file in src\)
rem L'icona e' icona\StepLoop.ico, ricavata da icona\logo.png con icona\make-icon-da-logo.cs
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ /out:StepLoop.exe /win32icon:icona\StepLoop.ico /resource:icona\StepLoop.ico,StepLoop.ico /r:System.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll src\*.cs
if errorlevel 1 (echo BUILD FALLITA) else (echo Build OK: StepLoop.exe)
pause
