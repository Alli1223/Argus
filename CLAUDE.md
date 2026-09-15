# Argus

A self-hosted system monitoring tool: a .NET server with a React web UI, TimescaleDB storage, and agents for Linux and Windows. `TODO.md` is the master task list.

## Git workflow

- Work on a branch, not on `main`. Push it, open a pull request, and merge it into `main` once the CI checks have passed. Claude may merge its own pull requests after CI completes.
- Commit early and often. Commit messages, PR titles, PR descriptions and merge commit messages stay short and plain.
- Never add Claude as a contributor: no `Co-Authored-By` or `Claude-Session` trailers in commits, and no "Generated with Claude Code" line in PR descriptions or merge commits.
