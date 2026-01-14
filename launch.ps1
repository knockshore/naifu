# ETL Node Editor Launcher - PowerShell Version
# Using WinPython from D:\programs\WPy64

Write-Host "Starting ETL Node Editor..." -ForegroundColor Green
Write-Host ""

# Set WinPython paths
$WINPYTHON_DIR = "D:\programs\WPy64"
$PYTHON_PATH = "$WINPYTHON_DIR\python-3.11.5.amd64"
$SCRIPTS_PATH = "$WINPYTHON_DIR\python-3.11.5.amd64\Scripts"

# Alternative common WinPython structures - uncomment and adjust if needed
# $PYTHON_PATH = "$WINPYTHON_DIR\WPy64-3115\python-3.11.5.amd64"
# $SCRIPTS_PATH = "$WINPYTHON_DIR\WPy64-3115\python-3.11.5.amd64\Scripts"

# Add to PATH
$env:PATH = "$PYTHON_PATH;$SCRIPTS_PATH;$env:PATH"

# Check if Python is accessible
Write-Host "Checking Python installation..." -ForegroundColor Cyan
& python --version
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Python not found in WinPython directory" -ForegroundColor Red
    Write-Host "Please verify the path in launch.ps1 matches your WinPython installation" -ForegroundColor Red
    Write-Host "Current path: $WINPYTHON_DIR" -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host ""
Write-Host "Installing/updating dependencies..." -ForegroundColor Cyan
& python -m pip install --upgrade pip
& python -m pip install -r requirements.txt

Write-Host ""
Write-Host "Launching ETL Node Editor..." -ForegroundColor Green
Write-Host ""
& python main.py
