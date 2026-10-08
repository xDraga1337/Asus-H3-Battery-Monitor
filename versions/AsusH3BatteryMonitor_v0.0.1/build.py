#!/usr/bin/env python3
"""Publish this version using the existing Windows SDK; never access HID devices."""
from pathlib import Path
import json
import os
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
# Resolve the version folder itself so builds never write into the Desktop reference.
WINDOWS_ROOT = str(ROOT) if sys.platform == "win32" else subprocess.check_output(
    ["wslpath", "-w", str(ROOT)], text=True).strip()
(ROOT / "tmp").mkdir(exist_ok=True)
(ROOT / "notes").mkdir(exist_ok=True)
SDK = "/mnt/c/Users/M/.dotnet/dotnet.exe" if sys.platform != "win32" else r"C:\Users\M\.dotnet\dotnet.exe"

env = os.environ.copy()
env.update({
    "DOTNET_CLI_HOME": WINDOWS_ROOT + r"\tmp\dotnet-cli",
    "NUGET_PACKAGES": WINDOWS_ROOT + r"\packages\nuget-cache",
    "NUGET_HTTP_CACHE_PATH": WINDOWS_ROOT + r"\tmp\nuget-http-cache",
    "NUGET_PLUGINS_CACHE_PATH": WINDOWS_ROOT + r"\tmp\nuget-plugins-cache",
    "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
    "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
    "DOTNET_NOLOGO": "1",
    "DOTNET_ADD_GLOBAL_TO_PATH": "false",
    "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false",
    "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "true",
    "DOTNET_CLI_USE_MSBUILD_SERVER": "0",
    "MSBUILDDISABLENODEREUSE": "1",
    "TEMP": WINDOWS_ROOT + r"\tmp",
    "TMP": WINDOWS_ROOT + r"\tmp",
})
cmd = [SDK, "publish", WINDOWS_ROOT + r"\src\AsusH3BatteryMonitor.csproj",
       "--configuration", "Release", "--runtime", "win-x64", "--self-contained", "true",
       "--output", WINDOWS_ROOT + r"\publish", "--disable-build-servers",
       "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true"]
(ROOT / "notes" / "build-command.json").write_text(json.dumps({"command": cmd, "environment": {k: env[k] for k in [
    "DOTNET_CLI_HOME", "NUGET_PACKAGES", "NUGET_HTTP_CACHE_PATH", "NUGET_PLUGINS_CACHE_PATH", "TEMP", "TMP"]}}, indent=2))
result = subprocess.run(cmd, cwd=ROOT, env=env, capture_output=True, text=True)
log = result.stdout + result.stderr
(ROOT / "notes" / "build-log.txt").write_text(log)
print(log, end="")
print("Build exit code:", result.returncode)
raise SystemExit(result.returncode)
