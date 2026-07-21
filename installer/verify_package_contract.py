#!/usr/bin/env python3
"""Validate the packaging contract without requiring Windows or Inno Setup."""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
MANIFEST_PATH = REPO_ROOT / "installer" / "package-manifest.json"


def fail(message: str) -> None:
    print(f"PACKAGING CONTRACT ERROR: {message}", file=sys.stderr)
    raise SystemExit(1)


def quoted_file_names(source: str) -> list[str]:
    return re.findall(r'"([^"\r\n]+\.(?:dll|gha|json))"', source, re.IGNORECASE)


def normalize_relative_path(path: str) -> str:
    return path.replace("\\", "/")


def main() -> None:
    manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    artifact_root = manifest.get("artifactRoot")
    required = manifest.get("requiredRuntimeFiles")
    samples = manifest.get("sampleFiles")

    if artifact_root != "bin/dist":
        fail(f"artifactRoot must be bin/dist, got {artifact_root!r}")
    if not isinstance(required, list) or not required:
        fail("requiredRuntimeFiles must be a non-empty list")
    if len(required) != len(set(required)):
        fail("requiredRuntimeFiles contains duplicates")
    invalid_paths = [
        name
        for name in required
        if not isinstance(name, str)
        or name != normalize_relative_path(name)
        or Path(name).is_absolute()
        or ".." in Path(name).parts
    ]
    if invalid_paths:
        fail("requiredRuntimeFiles contains invalid relative paths: " + repr(invalid_paths))
    if not isinstance(samples, list) or not samples:
        fail("sampleFiles must be a non-empty list")

    missing_artifacts = [
        name for name in required if not (REPO_ROOT / artifact_root / name).is_file()
    ]
    if missing_artifacts:
        fail("missing build artifacts: " + ", ".join(missing_artifacts))

    deps_path = REPO_ROOT / artifact_root / "URSUS.GH.deps.json"
    deps = json.loads(deps_path.read_text(encoding="utf-8"))
    runtime_targets = []
    for target in deps.get("targets", {}).values():
        for library in target.values():
            for path, metadata in library.get("runtimeTargets", {}).items():
                if metadata.get("assetType") == "runtime":
                    runtime_targets.append(normalize_relative_path(path))
    runtime_targets = list(dict.fromkeys(runtime_targets))
    if not runtime_targets:
        fail("URSUS.GH.deps.json does not declare any runtimeTargets")
    missing_runtime_targets = [path for path in runtime_targets if path not in required]
    if missing_runtime_targets:
        fail(
            "package manifest omits runtimeTargets declared by URSUS.GH.deps.json: "
            + ", ".join(missing_runtime_targets)
        )

    missing_samples = [name for name in samples if not (REPO_ROOT / name).is_file()]
    if missing_samples:
        fail("missing sample files: " + ", ".join(missing_samples))

    contract_path = REPO_ROOT / "src" / "URSUS" / "Config" / "DeploymentContract.cs"
    contract_files = quoted_file_names(contract_path.read_text(encoding="utf-8"))
    if contract_files != required:
        fail(
            "DeploymentContract.RequiredRuntimeFiles differs from package manifest: "
            f"{contract_files!r} != {required!r}"
        )

    iss_path = REPO_ROOT / "installer" / "URSUS.iss"
    iss_text = iss_path.read_text(encoding="utf-8")
    iss_runtime_entries = re.findall(
        r'^Source:\s*"\.\.\\bin\\dist\\([^"]+)";\s*DestDir:\s*"([^"]+)";',
        iss_text,
        re.MULTILINE | re.IGNORECASE,
    )
    iss_runtime = [normalize_relative_path(source) for source, _ in iss_runtime_entries]
    if iss_runtime != required:
        fail(f"Inno runtime payload differs from package manifest: {iss_runtime!r}")
    for source, destination in iss_runtime_entries:
        relative = Path(normalize_relative_path(source))
        parent = normalize_relative_path(str(relative.parent))
        expected_destination = "{app}" if parent == "." else "{app}\\" + parent.replace("/", "\\")
        if destination.lower() != expected_destination.lower():
            fail(
                f"Inno destination for {source!r} is {destination!r}; "
                f"expected {expected_destination!r}"
            )

    iss_postinstall_checks = re.findall(
        r"CheckRequiredFile\(InstallDir,\s*'([^']+)'",
        iss_text,
        re.IGNORECASE,
    )
    iss_postinstall_checks = [normalize_relative_path(path) for path in iss_postinstall_checks]
    if iss_postinstall_checks != required:
        fail(
            "Inno post-install verifier differs from package manifest: "
            f"{iss_postinstall_checks!r} != {required!r}"
        )

    iss_samples = re.findall(
        r'^Source:\s*"\.\.\\([^"\\]+\.(?:gh|ghx))";',
        iss_text,
        re.MULTILINE | re.IGNORECASE,
    )
    if iss_samples != samples:
        fail(f"Inno sample payload differs from package manifest: {iss_samples!r}")

    checked_files = [
        REPO_ROOT / ".github" / "workflows" / "build-installer.yml",
        REPO_ROOT / "installer" / "build.ps1",
        iss_path,
    ]
    stale_patterns = (
        "bin/" + "Release",
        "bin" + "\\Release",
        "bin/" + "adstrd_legald_mapping.json",
        "bin" + "\\adstrd_legald_mapping.json",
    )
    for path in checked_files:
        text = path.read_text(encoding="utf-8")
        stale = [pattern for pattern in stale_patterns if pattern in text]
        if stale:
            fail(f"{path.relative_to(REPO_ROOT)} contains stale paths: {stale!r}")

    for path in checked_files[:2]:
        text = path.read_text(encoding="utf-8")
        if "package-manifest.json" not in text:
            fail(f"{path.relative_to(REPO_ROOT)} does not consume package-manifest.json")

    build_script = checked_files[1].read_text(encoding="utf-8")
    if "Copy-PackagePayload" not in build_script:
        fail("installer/build.ps1 does not stage the runtime payload beside standalone Setup")
    if "Join-Path $DestinationRoot $file" not in build_script:
        fail("installer/build.ps1 does not preserve manifest-relative payload paths")

    workflow = checked_files[0].read_text(encoding="utf-8")
    workflow_path_contracts = (
        "Join-Path $packageRoot $file",
        "Join-Path $staging $file",
        "dist/package/",
    )
    missing_workflow_contracts = [
        contract for contract in workflow_path_contracts if contract not in workflow
    ]
    if missing_workflow_contracts:
        fail(
            "workflow does not preserve recursive package paths: "
            + ", ".join(missing_workflow_contracts)
        )

    print(
        f"Packaging contract valid: {len(required)} runtime files and "
        f"{len(samples)} samples under {artifact_root}."
    )


if __name__ == "__main__":
    main()
