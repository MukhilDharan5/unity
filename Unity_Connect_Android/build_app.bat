@echo off
set "JAVA_HOME=C:\Projects\Unity\.tools\jdk-17\jdk-17.0.20.1+1"
set "ANDROID_HOME=C:\Users\mukhi\AppData\Local\Android\Sdk"
set "ANDROID_PREFS_ROOT="
set "ANDROID_USER_HOME=C:\Users\mukhi\.android"
set "PATH=%JAVA_HOME%\bin;%ANDROID_HOME%\platform-tools;%PATH%"

cd /d C:\Projects\Unity\Unity_Connect_Android
call gradlew.bat assembleDebug --no-daemon
