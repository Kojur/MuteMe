# Publishing MuteMe

This project can be pushed as a normal Git repository. `dist/`, executables, settings, and diagnostic output are ignored so only source and project assets go into Git.

## Create a new GitHub repository

From the MuteMe project folder, with [GitHub CLI](https://cli.github.com/) installed:

```powershell
gh auth login
```

If this folder is not already a Git repository:

```powershell
git init -b main
git add .
git commit -m "Initial MuteMe app"
```

Then choose the visibility you want:

```powershell
# Public: anyone can see the source
gh repo create MuteMe --public --source . --remote origin --push

# Or private: only people you grant access can see it
# gh repo create MuteMe --private --source . --remote origin --push
```

## Use an existing repository

Use the actual repository URL in place of `YOUR_ACCOUNT` below. The example assumes the remote repository is empty. If it already has commits, fetch and inspect its history before integrating the project; do not force-push over existing work.

```powershell
git remote add origin https://github.com/YOUR_ACCOUNT/MuteMe.git
git push -u origin main
```

## Publish a downloadable release

Build and test first, then create a release when you are ready:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test
gh release create v1.0.0 .\dist\MuteMe.exe --title "MuteMe 1.0.0" --generate-notes
```

Release creation publishes the executable to the selected repository. The binary is not code-signed. Add an open-source license if you want to grant others explicit rights to reuse the code; choose one that fits your intended terms.
