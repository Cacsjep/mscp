@echo off
echo ============================================
echo  Starting 4 RTMP streams
echo  3 video files + 1 test pattern
echo ============================================
echo.

start "Stream 1 - Video" cmd /c ""%~dp0video_test_push.bat" stream1 "%~dp0test-vids\avalon-city-sta.-catalina-island-6.mp4""
start "Stream 2 - Video" cmd /c ""%~dp0video_test_push.bat" stream2 "%~dp0test-vids\city-town-aerial.mp4""
start "Stream 3 - Video" cmd /c ""%~dp0video_test_push.bat" stream3 "%~dp0test-vids\royal-palace-of-madrid-3.mp4""
start "Stream 4 - Test Pattern" cmd /c ""%~dp0test_push.bat" stream4"

echo All 4 streams launched in separate windows.
echo Close the windows or press Ctrl+C in each to stop.
