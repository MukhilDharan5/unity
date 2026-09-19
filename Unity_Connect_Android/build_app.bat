@echo off
setlocal
set "PROJECT_DIR=%~dp0"

if not defined JAVA_HOME if exist "%PROJECT_DIR%..\.tools\jdk-17\jdk-17.0.20.1+1\bin\java.exe" set "JAVA_HOME=%PROJECT_DIR%..\.tools\jdk-17\jdk-17.0.20.1+1"
if not defined JAVA_HOME if exist "%ProgramFiles%\Android\Android Studio\jbr\bin\java.exe" set "JAVA_HOME=%ProgramFiles%\Android\Android Studio\jbr"
if not defined ANDROID_HOME if defined ANDROID_SDK_ROOT set "ANDROID_HOME=%ANDROID_SDK_ROOT%"
if not defined ANDROID_HOME if exist "%LOCALAPPDATA%\Android\Sdk" set "ANDROID_HOME=%LOCALAPPDATA%\Android\Sdk"

if defined JAVA_HOME set "PATH=%JAVA_HOME%\bin;%PATH%"
where java >nul 2>nul || (
    echo JDK 17 was not found. Set JAVA_HOME or install Android Studio.
    exit /b 1
)
if not defined ANDROID_HOME if not exist "%PROJECT_DIR%local.properties" (
    echo Android SDK was not found. Set ANDROID_HOME or create local.properties with sdk.dir.
    exit /b 1
)

pushd "%PROJECT_DIR%"
if "%~1"=="" (
    call gradlew.bat :app:assembleDebug --no-daemon
) else (
    call gradlew.bat %*
)
set "BUILD_EXIT=%ERRORLEVEL%"
popd
exit /b %BUILD_EXIT%
