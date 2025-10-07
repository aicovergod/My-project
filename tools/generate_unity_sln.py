#!/usr/bin/env python3
import argparse
import os
from pathlib import Path

SLN = """\
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio 17
Project("{00000000-0000-0000-0000-000000000001}") = "UnityAnalysis", "{csproj}", "{00000000-0000-0000-0000-000000000002}"
EndProject
Global
    GlobalSection(SolutionConfigurationPlatforms) = preSolution
        Debug|Any CPU = Debug|Any CPU
    EndGlobalSection
    GlobalSection(ProjectConfigurationPlatforms) = postSolution
        {00000000-0000-0000-0000-000000000002}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        {00000000-0000-0000-0000-000000000002}.Debug|Any CPU.Build.0 = Debug|Any CPU
    EndGlobalSection
EndGlobal
"""

CSPROJ = """\
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <EnableDefaultItems>false</EnableDefaultItems>
  </PropertyGroup>

  <ItemGroup>
{items}
  </ItemGroup>

{references}

  <ItemGroup>
    <PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.507" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="8.0.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
"""

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default="Assets")
    ap.add_argument("--sln", default="ci/UnityAnalysis.sln")
    ap.add_argument("--csproj", default="ci/UnityAnalysis.csproj")
    ap.add_argument(
        "--unity-assemblies",
        action="append",
        default=[],
        help="Directory containing Unity managed assemblies (.dll). Repeat to add multiple search roots.",
    )
    args = ap.parse_args()

    root = Path(args.root)
    if not root.is_absolute():
        root = (Path.cwd() / root).resolve()
    else:
        root = root.resolve()

    csproj_path = Path(args.csproj)
    if not csproj_path.is_absolute():
        csproj_path = (Path.cwd() / csproj_path).resolve()
    else:
        csproj_path = csproj_path.resolve()
    csproj_dir = csproj_path.parent

    sln_path = Path(args.sln)
    if not sln_path.is_absolute():
        sln_path = (Path.cwd() / sln_path).resolve()
    else:
        sln_path = sln_path.resolve()

    csproj_dir.mkdir(parents=True, exist_ok=True)
    sln_path.parent.mkdir(parents=True, exist_ok=True)

    blocked_segments = ("/Library/", "/Packages/", "/ProjectSettings/", "/Temp/", "/Build/", "/obj/", "/bin/")
    files = []
    for path in sorted(root.rglob("*.cs")):
        posix_path = path.as_posix()
        if any(seg in posix_path for seg in blocked_segments):
            continue
        rel_path = os.path.relpath(path, csproj_dir)
        files.append(Path(rel_path).as_posix())

    compile_items = "\n".join(f'    <Compile Include="{f}" />' for f in files) or "    <!-- no C# files found -->"

    project_root = None
    for candidate in [root, *root.parents]:
        if (candidate / "ProjectSettings").exists():
            project_root = candidate
            break
    if project_root is None:
        project_root = root if root == root.parent else root.parent

    def normalize_dir(value) -> Path:
        path = Path(value).expanduser()
        if not path.is_absolute():
            path = (project_root / path).resolve()
        else:
            path = path.resolve()
        return path

    search_roots = []
    env_search = os.environ.get("UNITY_ASSEMBLIES")
    if env_search:
        search_roots.extend(normalize_dir(Path(p)) for p in env_search.split(os.pathsep) if p)
    search_roots.extend(normalize_dir(Path(p)) for p in args.unity_assemblies)

    if not search_roots:
        default_roots = (
            project_root / "Library/ScriptAssemblies",
            project_root / "Library/PlayerScriptAssemblies",
            project_root / "Library/PackageCache",
            project_root / "Library/UnityAssemblies",
        )
        search_roots.extend(default_roots)

    seen_root_paths = set()
    unique_search_roots = []
    for base in search_roots:
        if base in seen_root_paths:
            continue
        seen_root_paths.add(base)
        unique_search_roots.append(base)
    search_roots = unique_search_roots

    excluded_prefixes = ("System", "Microsoft.", "netstandard", "Mono.")
    excluded_names = {"mscorlib"}
    references = []
    seen = set()
    for base in search_roots:
        if not base.exists():
            continue
        if base.is_file():
            candidates = [base] if base.suffix.lower() == ".dll" else []
        else:
            candidates = sorted(base.rglob("*.dll"))
        for dll in candidates:
            name = dll.stem
            if name in seen:
                continue
            if name in excluded_names or name.startswith(excluded_prefixes):
                continue
            rel_hint = Path(os.path.relpath(dll, csproj_dir)).as_posix()
            references.append(
                f"    <Reference Include=\"{name}\">\n"
                f"      <HintPath>{rel_hint}</HintPath>\n"
                "    </Reference>"
            )
            seen.add(name)

    if references:
        references_block = "  <ItemGroup>\n" + "\n".join(references) + "\n  </ItemGroup>"
    else:
        references_block = (
            "  <ItemGroup>\n"
            "    <!-- no Unity assemblies found; specify --unity-assemblies or UNITY_ASSEMBLIES -->\n"
            "  </ItemGroup>"
        )

    csproj_path.write_text(CSPROJ.format(items=compile_items, references=references_block), encoding="utf-8")
    csproj_reference_in_sln = Path(os.path.relpath(csproj_path, sln_path.parent)).as_posix()
    sln_path.write_text(SLN.replace("{csproj}", csproj_reference_in_sln), encoding="utf-8")
    sln_display_path = Path(os.path.relpath(sln_path, Path.cwd())).as_posix()
    print(
        f"Generated {len(files)} Compile items and {len(references)} references into {sln_display_path}"
    )

if __name__ == "__main__":
    main()
