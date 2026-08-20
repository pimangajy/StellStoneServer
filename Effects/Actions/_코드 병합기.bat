@echo off
title Script Merger
echo C# Script Merging Start...

call npx repomix --include "**/*.cs" -o Action_code.txt

echo.
echo Complete! Created all_code.xml
pause