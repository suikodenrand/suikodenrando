@echo off
where py >nul 2>nul
if errorlevel 1 (
    python -B "%~dp0seed.py" %*
) else (
    py -3 -B "%~dp0seed.py" %*
)
if errorlevel 1 pause
