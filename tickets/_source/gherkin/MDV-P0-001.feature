Feature: Create the Medvya monorepo skeleton (Medvya.sln, web workspaces, packages)
  As a developer, I want the agreed repository layout with pinned tooling, so that every later ticket builds on a deployable, consistent base.

  # chunk: Create the monorepo skeleton with pinned tooling and README
  @done
  Scenario: Fresh clone builds with zero warnings
    Given a fresh clone of the repository
    When I run "dotnet build" and "pnpm install && pnpm -r build"
    Then both succeed with zero warnings

  @done
  Scenario: Folder layout matches the architecture document
    Given docs/03-architecture/repository-structure.md
    When I list the repository folders needed in P0
    Then they match the document
    And later modules are not created yet

  @done
  Scenario: Versions are pinned and centrally managed
    Given the repository root
    Then global.json pins a .NET 10 SDK
    And Directory.Packages.props holds every NuGet version
    And no project file contains a version number

  @done
  Scenario: Clinic web app starts with the shared UI package
    When I run "pnpm --filter clinic dev"
    Then the app starts on React 19 with TypeScript and Vite
    And it renders a placeholder page using packages/ui

  @done
  Scenario: README and AGENTS.md state prerequisites and safety rules
    Then README.md explains prerequisites, local start and links to docs/
    And AGENTS.md states the synthetic-data-only and no-PHI-in-logs rules

  @done
  Scenario: CI builds the solution and web workspace on a clean runner
    When the CI job runs on a clean runner
    Then the solution and the web workspace both build
