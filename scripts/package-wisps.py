#!/usr/bin/env python3
"""Build a separate Windows x64 debug bundle on Linux; no existing installation is touched."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
from datetime import datetime, timezone
import zipfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", default="https://api.nuget.org/v3/index.json", help="NuGet source or local package cache")
    parser.add_argument("--packages", help="NuGet package cache directory")
    parser.add_argument("--include-unique", action="store_true", help="Include UniqueLoot and create the unique-drop test bundle")
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    artifact_dir = root / "artifacts" / ("unique" if args.include_unique else "wisps")
    bundle_name = "GameHelper-unique-debug-win-x64" if args.include_unique else "WhereTheWispsAt-debug-win-x64"
    artifact_dir.mkdir(parents=True, exist_ok=True)
    environment = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")

    def run(*command):
        subprocess.run(command, cwd=root, env=environment, check=True)

    projects = ["Plugins/WhereTheWispsAt/WhereTheWispsAt.csproj", "Plugins/Radar/Radar.csproj"]
    if args.include_unique:
        projects.append("Plugins/UniqueLoot/UniqueLoot.csproj")
    for project in projects:
        restore = ["dotnet", "restore", project, "-r", "win-x64", "-p:EnableWindowsTargeting=true",
                   "-p:SelfContained=true", "--disable-parallel", "--source", args.source, "-v:minimal"]
        if args.packages:
            restore += ["--packages", args.packages]
        run(*restore)
    common = ["-c", "Release", "--no-restore", "-p:EnableWindowsTargeting=true", "-p:SelfContained=true",
              "-m:1", "-nr:false", "--disable-build-servers", "-v:minimal"]
    for project in projects:
        run("dotnet", "build", project, *common)

    with tempfile.TemporaryDirectory(prefix="wisps-package-") as staging:
        package = Path(staging) / bundle_name
        run("dotnet", "publish", "GameHelper/GameHelper.csproj", *common, "-r", "win-x64",
            "-p:PublishSingleFile=false", "-p:PublishTrimmed=false", "-p:PublishReadyToRun=false",
            "-p:PublishDocumentationFile=false", "-o", str(package))
        # Copy an explicit allowlist of plugin assets, excluding local configs, logs and other plugins.
        plugins = {
            "WhereTheWispsAt": [],
            "Radar": ["icons.png", "important_tgt_files.txt", "boss_arena_tgt_files.txt", "stairs_tgt_files.txt"],
        }
        if args.include_unique:
            plugins["UniqueLoot"] = []
        for name, assets in plugins.items():
            output = root / "Plugins" / name / "bin/Release/net10.0-windows/win-x64"
            target = package / "Plugins" / name
            target.mkdir(parents=True, exist_ok=True)
            for file in [name + ".dll", name + ".pdb", *assets]:
                shutil.copy2(output / file, target / file)
            shutil.copytree(root / "Plugins" / name / "Localization", target / "Localization")
        shutil.copy2(root / "LICENSE", package / "LICENSE")
        shutil.copy2(root / "Plugins/WhereTheWispsAt/README.md", package / "WhereTheWispsAt-README.zh-CN.md")
        start_here = "Plugins/UniqueLoot/README.md" if args.include_unique else "Plugins/WhereTheWispsAt/WINDOWS-DEBUG.zh-CN.md"
        shutil.copy2(root / start_here, package / "START-HERE.zh-CN.md")
        if args.include_unique:
            data_target = package / "Plugins/UniqueLoot/Data"
            data_target.mkdir(parents=True, exist_ok=True)
            shutil.copy2(root / "Plugins/UniqueLoot/Data/SOURCES.md", data_target / "SOURCES.md")
            startup = package / "START-HERE.zh-CN.md"
            startup.write_text(startup.read_text().replace("(Data/SOURCES.md)", "(Plugins/UniqueLoot/Data/SOURCES.md)"))
        shutil.copy2(root / "TODO.md", package / "TODO.zh-CN.md")
        # Redirect the existing host's stdout/stderr too: plugin logging cannot diagnose a failed load.
        (package / "Start-Debug.cmd").write_bytes((
            '@echo off\r\ncd /d "%~dp0"\r\n'
            'powershell.exe -NoProfile -Command "$ErrorActionPreference = \'Stop\'; if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw \'Right-click Start-Debug.cmd and select Run as administrator (required by the existing GameHelper manifest).\' }; $p = Start-Process -FilePath \'./GameHelper.exe\' -WorkingDirectory (Get-Location).Path -RedirectStandardOutput \'host-stdout.log\' -RedirectStandardError \'host-stderr.log\' -PassThru -Wait; exit $p.ExitCode"\r\n'
            'if errorlevel 1 pause\r\n'
        ).encode("ascii"))
        required = ["GameHelper.exe", "GameHelper.dll", "GameOffsets.dll", "GameHelper.deps.json",
                    "GameHelper.runtimeconfig.json", "coreclr.dll", "hostfxr.dll", "hostpolicy.dll",
                    "System.Private.CoreLib.dll", "cimgui.dll", "ImGui.NET.dll", "ClickableTransparentOverlay.dll",
                    "ProcessMemoryUtilities.dll", "fonts/DejaVuSans.ttf", "fonts/unifont.ttf",
                    "Localization/zh-CN.json", "Plugins/WhereTheWispsAt/WhereTheWispsAt.dll",
                    "Plugins/WhereTheWispsAt/Localization/zh-CN.json", "Plugins/Radar/Radar.dll"]
        if args.include_unique:
            required += ["Plugins/UniqueLoot/UniqueLoot.dll", "Plugins/UniqueLoot/Localization/zh-CN.json", "Plugins/UniqueLoot/Data/SOURCES.md"]
        for file in required:
            if not (package / file).is_file():
                raise RuntimeError("Required package file missing: " + file)
        for file in ["GameHelper.exe", "coreclr.dll", "cimgui.dll"]:
            data = (package / file).read_bytes()
            pe = int.from_bytes(data[0x3C:0x40], "little")
            if data[pe:pe + 4] != b"PE\0\0" or int.from_bytes(data[pe + 4:pe + 6], "little") != 0x8664:
                raise RuntimeError("Expected Windows x64 PE: " + file)
        runtime = json.loads((package / "GameHelper.runtimeconfig.json").read_text())["runtimeOptions"]
        if "framework" in runtime or "frameworks" in runtime or not runtime.get("includedFrameworks"):
            raise RuntimeError("Publish did not produce a self-contained runtime configuration")
        sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
        source_hashes = {}
        for folder in ["GameHelper", "GameOffsets", *["Plugins/" + name for name in plugins]]:
            for path in (root / folder).rglob("*"):
                if path.suffix in {".cs", ".csproj", ".json"} and not {"obj", "bin", "config", "diagnostics"}.intersection(path.parts) and path.is_file():
                    source_hashes[str(path.relative_to(root))] = sha(path)
        manifest = {
            "CreatedUtc": datetime.now(timezone.utc).isoformat(),
            "BaseCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip(),
            "Description": "Local modified GameHelper core + " + " + ".join(plugins) + "; Windows live validation pending.",
            "Sdk": subprocess.check_output(["dotnet", "--version"], cwd=root, env=environment, text=True).strip(),
            "IncludedFrameworks": runtime["includedFrameworks"],
            "SourceSha256": source_hashes,
            "FilesSha256": {str(p.relative_to(package)): sha(p) for p in sorted(package.rglob("*")) if p.is_file()},
        }
        (package / "build-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
        archive = artifact_dir / (bundle_name + ".zip")
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as output:
            for path in sorted(package.rglob("*")):
                if path.is_file():
                    output.write(path, path.relative_to(package.parent))
        with zipfile.ZipFile(archive) as check:
            bad = check.testzip()
            if bad:
                raise RuntimeError("ZIP integrity failed: " + bad)
        (artifact_dir / (archive.name + ".sha256")).write_text(sha(archive) + "  " + archive.name + "\n")
        print(f"Verified self-contained Windows x64 bundle: {archive} ({archive.stat().st_size / 1024**2:.1f} MiB)")
        print("Not executed on Windows; live game validation remains pending.")


if __name__ == "__main__":
    main()
