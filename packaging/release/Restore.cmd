@echo off
setlocal DisableDelayedExpansion
rem End the batch context before restore can remove it. The already-expanded
rem PowerShell tail still runs and its exact exit code becomes the result.
(goto) 2>nul & "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Restore.ps1" %*
