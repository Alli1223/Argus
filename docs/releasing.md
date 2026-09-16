# Releasing

A release is a git tag. Pushing one publishes the server and updater images, then the agents on the
[releases page](https://github.com/Alli1223/Argus/releases), which is where Argus servers look for
updates.

1. On a branch, set `VersionPrefix` in [`Directory.Build.props`](../Directory.Build.props) to the
   new version, and merge it once CI passes.
2. Tag the merge on `main` and push the tag:

   ```sh
   git switch main && git pull
   git tag v0.3.0
   git push origin v0.3.0
   ```

The [release workflow](../.github/workflows/release.yml) refuses a tag that does not match
`VersionPrefix`. It first pushes the images, for x86-64 and ARM64, to GitHub's container registry:

| Image | What it is |
| --- | --- |
| `ghcr.io/alli1223/argus:<version>` and `:latest` | The server, with the web app and the agent builds. |
| `ghcr.io/alli1223/argus-updater:<version>` and `:latest` | The updater, which installs releases from **Settings**. |

GitHub makes a new package private. After the first release that publishes them, open each package's
settings on GitHub and make it public, or servers cannot download it.

Then it publishes the release, since servers offer a release as soon as it is out:

| File | What it is |
| --- | --- |
| `argus-agent-<version>-linux-x64.tar.gz`, `…-linux-arm64.tar.gz`, `…-win-x64.zip` | The agent with its install scripts, for installing by hand. |
| `argus-agent-linux-x64`, `argus-agent-linux-arm64`, `argus-agent-win-x64.exe` | The bare agent. Servers download these to update agents. |
| `SHA256SUMS` | Checksums of all of the above. Servers check downloads against it. |

The notes list the pull requests merged since the previous release. Edit them on GitHub if they
need more. When a release changes `deploy/docker-compose.yml`, say so in the notes: the updater
only replaces the server, so such a release has to be installed by hand.

Servers only offer releases marked as the latest, so a pre-release or a draft reaches nobody.
