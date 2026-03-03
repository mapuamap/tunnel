# Deployment Scripts

## deploy.bat

Automated deployment script for Tunnel Manager.

### Prerequisites

1. Set `GIT_TOKEN` environment variable:
   ```cmd
   set GIT_TOKEN=your_github_token_here
   ```

2. Or set it permanently in Windows:
   - Open System Properties > Environment Variables
   - Add `GIT_TOKEN` with your GitHub personal access token

### Usage

```cmd
deploy.bat [commit_message]
```

If no commit message is provided, it will use "auto deploy".

### What it does

1. Builds the project in Release mode
2. Commits and pushes changes to GitHub
3. Deploys to VM (192.168.66.154)
4. Restarts the tunnelmanager service

### Note

If GitHub blocks push due to security rules (secrets in history), you can:
- Allow the secret in GitHub repository settings
- Or push manually: `git push -u origin master`
