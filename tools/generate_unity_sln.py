#!/usr/bin/env python3
import argparse
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
    args = ap.parse_args()

    root = Path(args.root)
    files = [p.as_posix() for p in root.rglob("*.cs")
             if not any(seg in p.as_posix() for seg in
                        ("/Library/", "/Packages/", "/ProjectSettings/", "/Temp/", "/Build/", "/obj/", "/bin/"))]

    Path("ci").mkdir(parents=True, exist_ok=True)
    items = "\n".join(f'    <Compile Include="{f}" />' for f in files) or "    <!-- no C# files found -->"
    Path(args.csproj).write_text(CSPROJ.format(items=items), encoding="utf-8")
    Path(args.sln).write_text(SLN.replace("{csproj}", Path(args.csproj).as_posix()), encoding="utf-8")
    print(f"Generated {len(files)} files into {args.sln}")

if __name__ == "__main__":
    main()
