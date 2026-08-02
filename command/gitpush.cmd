@echo off
setlocal
set "COMMIT_MESSAGE=%~1"
if not defined COMMIT_MESSAGE set "COMMIT_MESSAGE=Update ScreenshotCat"

pushd "%~dp0.." || exit /b 1
git add -- README.md CHANGELOG.md SECURITY.md LICENSE worker.js wrangler.jsonc website installer command main ScreenshotCat ScreenshotCat.Verification assets
if errorlevel 1 goto :fail

git diff --cached --quiet
if not errorlevel 1 (
  echo No staged source changes.
  popd
  exit /b 0
)

git commit -m "%COMMIT_MESSAGE%"
if errorlevel 1 goto :fail
git push
if errorlevel 1 goto :fail

popd
exit /b 0

:fail
set "EXIT_CODE=%ERRORLEVEL%"
popd
exit /b %EXIT_CODE%
