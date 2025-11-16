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
