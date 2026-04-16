@echo off
echo Saving project to GitHub...
"C:\Program Files\Git\cmd\git.exe" add .
set /p commit_msg="Enter a short description of your changes: "
"C:\Program Files\Git\cmd\git.exe" commit -m "%commit_msg%"
"C:\Program Files\Git\cmd\git.exe" push
echo Done! Your project is saved to GitHub.
pause
