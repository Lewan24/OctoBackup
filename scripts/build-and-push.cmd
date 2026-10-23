@echo off
setlocal
pushd "%~dp0.."
if errorlevel 1 exit /b 1
docker build -f Backend/Dockerfile -t lewan24/octobackup-api:latest .
if errorlevel 1 goto failed
docker build -f Frontend/Dockerfile -t lewan24/octobackup-web:latest .
if errorlevel 1 goto failed
docker push lewan24/octobackup-api:latest
if errorlevel 1 goto failed
docker push lewan24/octobackup-web:latest
if errorlevel 1 goto failed
popd
exit /b 0
:failed
popd
exit /b 1
