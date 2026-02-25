@echo off

set STREAM_ENDPOINT=%~1
if "%STREAM_ENDPOINT%"=="" set STREAM_ENDPOINT=stream1

echo ============================================
echo  RTMP Test Push - %STREAM_ENDPOINT%
echo  Target: rtmp://localhost:8783/%STREAM_ENDPOINT%
echo ============================================
echo.
echo Pushing FFmpeg test pattern (640x480 @ 30fps, H.264)
echo Press Ctrl+C to stop
echo.
REM URL format: rtmp://host:port/streampath
REM The driver accepts both rtmp://host:port/stream1 and rtmp://host:port/live/stream1
ffmpeg -re -f lavfi -i testsrc2=size=640x480:rate=30 -c:v libx264 -preset ultrafast -tune zerolatency -g 30 -f flv rtmp://localhost:8783/%STREAM_ENDPOINT%
