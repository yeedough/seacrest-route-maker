@echo off
cd /d "%~dp0"
py -3 setup_beta.py
if errorlevel 1 echo Setup failed. Read the message above.
pause
