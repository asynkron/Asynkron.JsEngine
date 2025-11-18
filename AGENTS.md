# Agent Guidance

## .NET installation
To ensure the correct .NET runtime/SDK is available when working on this repository, use the official `dotnet-install` script.

On Linux/macOS:
```
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0
export PATH="$HOME/.dotnet:$PATH"
```

On Windows PowerShell:
```
irm https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
./dotnet-install.ps1 -Channel 10.0
$env:PATH = "$env:USERPROFILE\.dotnet;" + $env:PATH
```

These commands install the latest .NET 10 SDK (which includes the runtime) into the user profile so the repository can build and run tests without requiring global admin access.

## Continue working

Read the continue.md file fully, alanyze it and understad the next steps described there.
Then continue working on the project as per the instructions given in that file.

Whenever some task is completed, remove it from the continue.md file and update the file with the new next steps.
So we get a form of rolling window of next steps to be done.
