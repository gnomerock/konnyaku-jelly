---
name: release
description: Commit the current changes, push to main, and release a new Konnyaku Jelly version (bump the csproj Version, tag, watch the release workflow, check Gnomod). Use when the user asks to commit/push/release or ship a version.
argument-hint: "[version, e.g. 0.7.1.0 | patch | minor]"
---

# Release Konnyaku Jelly

Commit everything, push `main`, tag a new version and confirm that it reached players. `$ARGUMENTS` may be an exact 4-part version (`0.7.1.0`), `patch` (third part +1, the default) or `minor` (second part +1, third reset to 0). The fourth part stays `0`.

## 1. Check

- `git status` and `git diff`. Read what will be committed. Stop and ask if there are files that look unrelated, secrets (`.dev.vars`, tokens), or build output.
- Must be on `main`. `git fetch` first; if `origin/main` is ahead, stop and tell the user.
- The new tag must not exist yet: `git tag -l v<version>`.
- If `worker/` or the glossary changed, or before any release, run `npm run export` in `worker/` to refresh `glossary.json` from the server. If it fails (not logged in to wrangler), say so and continue with the existing file.
- If user-visible behaviour changed, check that `README.md` and `docs/index.html` say the same thing (see CLAUDE.md → Docs).

## 2. Build

```
dotnet build Konyakku/Konyakku.csproj -c Release
```

It must end with `0 Error(s)`. Don't release a failing build.

## 3. Bump, commit, push

- Set `<Version>` in `Konyakku/Konyakku.csproj` to the new version.
- Stage specific files (not `git add -A` blindly) and commit with a short message describing the user-visible change, ending with the attribution lines from the system reminder.
- `git push origin main`

## 4. Tag and watch

```
git tag v<version>
git push origin v<version>
```

The `release.yml` workflow builds the tag, creates the GitHub release with `latest.zip` and updates the Konyakku entry in gnomerock/Gnomod's `repo.json`.

- Find the run: `gh run list --workflow release.yml --limit 1`, then `gh run watch <id> --exit-status`.
- If it fails, show the failing step's log (`gh run view <id> --log-failed`). Don't delete or move the tag without asking.

## 5. Verify

- `gh release view v<version>` has `latest.zip`.
- `https://raw.githubusercontent.com/gnomerock/Gnomod/main/repo.json` (or `git -C D:/repo/Gnomod pull` and read it): the entry with `InternalName` `Konyakku` shows the new `AssemblyVersion` and download links. raw.githubusercontent may cache for a few minutes.

## 6. Report

Tell the user the version, the commit, the release link, and whether Gnomod is updated. Say plainly if anything wasn't checked in game.

## Never

- Change `InternalName` or ImGui window IDs.
- Edit Gnomod's version, link, API level or `LastUpdate` fields by hand; the workflow does that.
- Force-push, or skip hooks.
