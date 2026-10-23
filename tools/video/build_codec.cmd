@echo off
setlocal
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b %errorlevel%
cd /d "%~dp0..\.."
set "ATRIA_FFMPEG=third_party\ffmpeg\ffmpeg-n9.0.2-33-g4cf298e66a-win64-lgpl-shared-9.0"
if not exist "artifacts\codec" mkdir "artifacts\codec"
cl /nologo /std:c11 /W4 /WX /O2 /MT /LD /external:I"%ATRIA_FFMPEG%\include" /external:W0 /Fo:artifacts\codec\atria_video.obj /Fe:artifacts\codec\atria_video.dll tools\video\atria_video.c /link /LIBPATH:"%ATRIA_FFMPEG%\lib" avcodec.lib avutil.lib swscale.lib
exit /b %errorlevel%
