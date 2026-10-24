@echo off
setlocal
set "ATRIA_VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%ATRIA_VSWHERE%" (
    echo Visual Studio Installer with C++ build tools is required. 1>&2
    exit /b 1
)
for /f "usebackq tokens=*" %%I in (`"%ATRIA_VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "ATRIA_VS=%%I"
if not defined ATRIA_VS (
    echo No Visual Studio installation with x64 C++ tools was found. 1>&2
    exit /b 1
)
call "%ATRIA_VS%\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b %errorlevel%
cd /d "%~dp0..\.."
set "ATRIA_FFMPEG=third_party\ffmpeg\ffmpeg-n9.0.2-33-g4cf298e66a-win64-lgpl-shared-9.0"
if not exist "artifacts\codec" mkdir "artifacts\codec"
cl /nologo /std:c11 /W4 /WX /O2 /MT /LD /external:I"%ATRIA_FFMPEG%\include" /external:W0 /Fo:artifacts\codec\atria_video.obj /Fe:artifacts\codec\atria_video.dll tools\video\atria_video.c /link /LIBPATH:"%ATRIA_FFMPEG%\lib" avcodec.lib avutil.lib swscale.lib
exit /b %errorlevel%
