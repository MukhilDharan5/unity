@echo off
setlocal
cd /d "%~dp0"
if not defined JAVA_HOME (
  for /d %%D in ("%~dp0..\.tools\jdk-17\jdk-17*") do set "JAVA_HOME=%%~fD"
)
if not defined ANDROID_HOME set "ANDROID_HOME=%LOCALAPPDATA%\Android\Sdk"
if not exist "%JAVA_HOME%\bin\java.exe" (
  echo Set JAVA_HOME to a JDK 17 installation before building. 1>&2
  exit /b 1
)
call gradlew.bat :app:testDebugUnitTest :app:assembleDebug --no-daemon
set "BUILD_RESULT=%ERRORLEVEL%"
endlocal & exit /b %BUILD_RESULT%
