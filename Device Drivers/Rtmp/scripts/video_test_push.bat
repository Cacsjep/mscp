@echo off

set STREAM_ENDPOINT=%~1
set VIDEO_FILE=%~2
if "%STREAM_ENDPOINT%"=="" (
    echo Usage: video_test_push.bat ^<stream_endpoint^> ^<video_file^>
    echo   video_test_push.bat stream1 myvideo.mp4
    echo   video_test_push.bat stream2 myvideo.mp4
    exit /b 1
)
if "%VIDEO_FILE%"=="" (
    echo Usage: video_test_push.bat ^<stream_endpoint^> ^<video_file^>
    echo   video_test_push.bat stream1 myvideo.mp4
    echo   video_test_push.bat stream2 myvideo.mp4
    exit /b 1
)

echo ============================================
echo  RTMP Video File Push - %STREAM_ENDPOINT%
echo  Target: rtmp://localhost:8783/%STREAM_ENDPOINT%
echo ============================================
echo.

if not exist "%VIDEO_FILE%" (
    echo ERROR: File not found: %VIDEO_FILE%
    exit /b 1
)

echo Source: %VIDEO_FILE%
echo Mode: Looping (press Ctrl+C to stop)
echo.
:loop
ffmpeg -re -i "%VIDEO_FILE%" -c:v libx264 -preset ultrafast -tune zerolatency -g 30 -c:a aac -f flv rtmp://localhost:8783/%STREAM_ENDPOINT%
echo.
echo Restarting stream...
goto loop
