# Builder - PowerShell Module for Build, Release, and Version Management
# This file contains PowerShell cmdlets that wrap the Builder .NET functionality

function Initialize-BuilderEnvironment {
    <#
    .SYNOPSIS
        Initializes a Builder environment for managing builds, versions, and releases.
    
    .DESCRIPTION
        Initializes a Builder environment with the necessary configuration for managing 
        builds, versions, and releases. This cmdlet should be called before using other Builder cmdlets.
    
    .PARAMETER WorkspacePath
        The path to the workspace directory. If not provided, the current directory is used.
    
    .PARAMETER GitHubOwner
        The GitHub owner/organization of the repository.
    
    .PARAMETER GitHubRepo
        The GitHub repository name.
    
    .PARAMETER GitHubToken
        The GitHub personal access token for API operations.
    
    .PARAMETER NuGetApiKey
        The NuGet API key for package publishing.
    
    .PARAMETER PSGalleryApiKey
        The PowerShell Gallery API key for module publishing.
    
    .EXAMPLE
        Initialize-BuilderEnvironment -WorkspacePath C:\MyProject
    
    .EXAMPLE
        Initialize-BuilderEnvironment -GitHubOwner "myorg" -GitHubRepo "myrepo" -GitHubToken $token
    #>
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)]
        [string]$WorkspacePath = (Get-Location).Path,
        
        [Parameter()]
        [string]$GitHubOwner,
        
        [Parameter()]
        [string]$GitHubRepo,
        
        [Parameter()]
        [string]$GitHubToken,
        
        [Parameter()]
        [string]$NuGetApiKey,
        
        [Parameter()]
        [string]$PSGalleryApiKey
    )
    
    begin {
        Write-Verbose "Initializing Builder environment"
    }
    
    process {
        try {
            # Create the BuildWorkflow object
            $buildWorkflow = [PSBuild.DependencyInjection.ServiceLocator]::GetBuildWorkflow()
            
            # Initialize the build environment
            $buildConfig = $buildWorkflow.InitializeBuildEnvironment($WorkspacePath)
            
            # Update additional configuration settings if provided
            if ($GitHubOwner) { $buildConfig.GitHubOwner = $GitHubOwner }
            if ($GitHubRepo) { $buildConfig.GitHubRepo = $GitHubRepo }
            if ($GitHubToken) { $buildConfig.GithubToken = $GitHubToken }
            if ($NuGetApiKey) { $buildConfig.NuGetApiKey = $NuGetApiKey }
            if ($PSGalleryApiKey) { $buildConfig.PSGalleryApiKey = $PSGalleryApiKey }
            
            # Store the configuration in the session
            $Global:BuilderConfig = $buildConfig
            
            Write-Verbose "Builder environment initialized successfully"
            return $buildConfig
        }
        catch {
            Write-Error "Error initializing Builder environment: $_"
            throw
        }
    }
}

function Invoke-Builder {
    <#
    .SYNOPSIS
        Executes a build operation using Builder.
    
    .DESCRIPTION
        Executes a build operation for .NET projects or PowerShell modules using Builder.
        This cmdlet can build .NET solutions/projects or PowerShell modules.
    
    .PARAMETER ProjectPath
        The path to the .NET project or solution file to build.
    
    .PARAMETER ModuleName
        The name of the PowerShell module to build.
    
    .PARAMETER Configuration
        The build configuration to use (Debug or Release). Default is Release.
    
    .PARAMETER SkipTests
        Whether to skip running tests as part of the build.
    
    .PARAMETER UpdateVersion
        Whether to update version information in the project files.
    
    .EXAMPLE
        Invoke-Builder -ProjectPath .\src\MyProject.csproj
    
    .EXAMPLE
        Invoke-Builder -ModuleName MyModule -SkipTests
    #>
    [CmdletBinding(DefaultParameterSetName = "DotNet")]
    param(
        [Parameter(ParameterSetName = "DotNet", Mandatory = $true, Position = 0)]
        [string]$ProjectPath,
        
        [Parameter(ParameterSetName = "PowerShell", Mandatory = $true)]
        [string]$ModuleName,
        
        [Parameter()]
        [ValidateSet("Debug", "Release")]
        [string]$Configuration = "Release",
        
        [Parameter()]
        [switch]$SkipTests,
        
        [Parameter()]
        [switch]$UpdateVersion
    )
    
    begin {
        Write-Verbose "Starting Builder build operation"
        
        # Ensure Builder is initialized
        if (-not $Global:BuilderConfig) {
            Write-Verbose "Builder environment not initialized, initializing now"
            $Global:BuilderConfig = Initialize-BuilderEnvironment
        }
    }
    
    process {
        try {
            # Get the build workflow
            $buildWorkflow = [PSBuild.DependencyInjection.ServiceLocator]::GetBuildWorkflow()
            
            # Create a copy of the configuration
            $config = $Global:BuilderConfig
            
            if ($UpdateVersion) {
                Write-Verbose "Updating version information"
                $versionInfo = $buildWorkflow.IncrementVersion($config, 2, $null, $false)
                Write-Verbose "Version set to $($versionInfo.Version)"
            }
            
            # Execute the build based on parameter set
            $successful = $false
            
            if ($PSCmdlet.ParameterSetName -eq "DotNet") {
                Write-Verbose "Building .NET project/solution: $ProjectPath"
                $successful = $buildWorkflow.RunDotNetBuild($config, $ProjectPath, $Configuration, $SkipTests)
            }
            else {
                Write-Verbose "Building PowerShell module: $ModuleName"
                $successful = $buildWorkflow.RunPowerShellModuleBuild($config, $ModuleName, $SkipTests)
            }
            
            if ($successful) {
                Write-Verbose "Build completed successfully"
            }
            else {
                Write-Error "Build failed"
            }
            
            return $successful
        }
        catch {
            Write-Error "Error during build: $_"
            throw
        }
    }
}

function New-BuilderRelease {
    <#
    .SYNOPSIS
        Creates a new release using Builder.
    
    .DESCRIPTION
        Creates a new release of a .NET project or PowerShell module using Builder.
        This can include creating a GitHub release, publishing to NuGet, and/or 
        publishing to the PowerShell Gallery.
    
    .PARAMETER TagVersion
        The version tag to use for the release. If not provided, the current version is used.
    
    .PARAMETER PublishToNuGet
        Whether to publish packages to NuGet.
    
    .PARAMETER PublishToPSGallery
        Whether to publish modules to the PowerShell Gallery.
    
    .PARAMETER CreateGitHubRelease
        Whether to create a GitHub release.
    
    .PARAMETER ReleaseNotes
        Optional release notes to include in the release. If not provided, notes will be
        extracted from the changelog.
    
    .EXAMPLE
        New-BuilderRelease -PublishToNuGet -CreateGitHubRelease
    
    .EXAMPLE
        New-BuilderRelease -TagVersion "1.2.0" -PublishToPSGallery
    #>
    [CmdletBinding()]
    param(
        [Parameter()]
        [string]$TagVersion,
        
        [Parameter()]
        [switch]$PublishToNuGet,
        
        [Parameter()]
        [switch]$PublishToPSGallery,
        
        [Parameter()]
        [switch]$CreateGitHubRelease,
        
        [Parameter()]
        [string]$ReleaseNotes
    )
    
    begin {
        Write-Verbose "Starting Builder release operation"
        
        # Ensure Builder is initialized
        if (-not $Global:BuilderConfig) {
            Write-Verbose "Builder environment not initialized, initializing now"
            $Global:BuilderConfig = Initialize-BuilderEnvironment
        }
    }
    
    process {
        try {
            # Get the build workflow
            $buildWorkflow = [PSBuild.DependencyInjection.ServiceLocator]::GetBuildWorkflow()
            
            # Create a copy of the configuration
            $config = $Global:BuilderConfig
            
            # If no tag version provided, use the current version
            if (-not $TagVersion) {
                $versionManager = [PSBuild.DependencyInjection.ServiceLocator]::GetVersionManager()
                $versionInfo = $versionManager.GetVersionFromGit($config.WorkspacePath)
                $TagVersion = $versionInfo.Version
            }
            
            Write-Verbose "Creating release for version $TagVersion"
            
            # For GitHub releases, ensure we have a token
            if ($CreateGitHubRelease -and -not $config.GithubToken) {
                Write-Error "GitHub token is required for creating GitHub releases"
                return $false
            }
            
            # For NuGet publishing, ensure we have an API key
            if ($PublishToNuGet -and -not $config.NuGetApiKey) {
                Write-Error "NuGet API key is required for publishing to NuGet"
                return $false
            }
            
            # For PowerShell Gallery publishing, ensure we have an API key
            if ($PublishToPSGallery -and -not $config.PSGalleryApiKey) {
                # Fall back to NuGet API key if PS Gallery key not provided
                if ($config.NuGetApiKey) {
                    $config.PSGalleryApiKey = $config.NuGetApiKey
                }
                else {
                    Write-Error "PowerShell Gallery API key is required for publishing to the PowerShell Gallery"
                    return $false
                }
            }
            
            # Create the release
            $successful = $buildWorkflow.PublishReleaseAsync(
                $config,
                $TagVersion,
                $PublishToNuGet,
                $PublishToPSGallery
            ).GetAwaiter().GetResult()
            
            if ($successful) {
                Write-Verbose "Release created successfully"
            }
            else {
                Write-Error "Release creation failed"
            }
            
            return $successful
        }
        catch {
            Write-Error "Error creating release: $_"
            throw
        }
    }
}

function Get-BuilderVersion {
    <#
    .SYNOPSIS
        Gets the version information for a project.
    
    .DESCRIPTION
        Gets the version information for a project based on Git history.
    
    .PARAMETER WorkspacePath
        The path to the workspace directory. If not provided, the current directory is used.
    
    .EXAMPLE
        Get-BuilderVersion
    
    .EXAMPLE
        Get-BuilderVersion -WorkspacePath C:\MyProject
    #>
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)]
        [string]$WorkspacePath = (Get-Location).Path
    )
    
    begin {
        Write-Verbose "Getting version information"
    }
    
    process {
        try {
            # Get the version manager
            $versionManager = [PSBuild.DependencyInjection.ServiceLocator]::GetVersionManager()
            
            # Get the version information
            $versionInfo = $versionManager.GetVersionFromGit($WorkspacePath)
            
            return $versionInfo
        }
        catch {
            Write-Error "Error getting version information: $_"
            throw
        }
    }
}

function Update-BuilderVersion {
    <#
    .SYNOPSIS
        Updates the version information for a project.
    
    .DESCRIPTION
        Updates the version information for a project by incrementing the major, minor, or patch version.
    
    .PARAMETER WorkspacePath
        The path to the workspace directory. If not provided, the current directory is used.
    
    .PARAMETER Major
        Increments the major version (e.g., 1.0.0 to 2.0.0).
    
    .PARAMETER Minor
        Increments the minor version (e.g., 1.0.0 to 1.1.0).
    
    .PARAMETER Patch
        Increments the patch version (e.g., 1.0.0 to 1.0.1).
    
    .PARAMETER PreRelease
        Adds a pre-release suffix to the version (e.g., 1.0.0 to 1.0.0-beta.1).
    
    .PARAMETER CreateTag
        Creates a Git tag for the new version.
    
    .PARAMETER UpdateFiles
        Updates version information in project files.
    
    .EXAMPLE
        Update-BuilderVersion -Minor -UpdateFiles
    
    .EXAMPLE
        Update-BuilderVersion -Patch -CreateTag
    #>
    [CmdletBinding(DefaultParameterSetName = "Patch")]
    param(
        [Parameter(Position = 0)]
        [string]$WorkspacePath = (Get-Location).Path,
        
        [Parameter(ParameterSetName = "Major")]
        [switch]$Major,
        
        [Parameter(ParameterSetName = "Minor")]
        [switch]$Minor,
        
        [Parameter(ParameterSetName = "Patch")]
        [switch]$Patch,
        
        [Parameter()]
        [string]$PreRelease,
        
        [Parameter()]
        [switch]$CreateTag,
        
        [Parameter()]
        [switch]$UpdateFiles = $true
    )
    
    begin {
        Write-Verbose "Updating version information"
        
        # Ensure Builder is initialized
        if (-not $Global:BuilderConfig) {
            Write-Verbose "Builder environment not initialized, initializing now"
            $Global:BuilderConfig = Initialize-BuilderEnvironment -WorkspacePath $WorkspacePath
        }
    }
    
    process {
        try {
            # Get the build workflow
            $buildWorkflow = [PSBuild.DependencyInjection.ServiceLocator]::GetBuildWorkflow()
            
            # Create a copy of the configuration
            $config = $Global:BuilderConfig
            
            # Determine which part to increment
            $part = 2 # Default to patch
            if ($Major) { $part = 0 }
            elseif ($Minor) { $part = 1 }
            
            # Update the version
            $versionInfo = $buildWorkflow.IncrementVersion($config, $part, $PreRelease, $CreateTag)
            
            Write-Verbose "Version updated to $($versionInfo.Version)"
            return $versionInfo
        }
        catch {
            Write-Error "Error updating version: $_"
            throw
        }
    }
}

function New-BuilderModule {
    <#
    .SYNOPSIS
        Creates a new PowerShell module with Builder.
    
    .DESCRIPTION
        Creates a new PowerShell module with the necessary structure and manifest.
    
    .PARAMETER Name
        The name of the new module.
    
    .PARAMETER Path
        The path where the module should be created. If not provided, the current directory is used.
    
    .PARAMETER Version
        The initial version of the module. Default is 0.1.0.
    
    .PARAMETER Description
        A description of the module.
    
    .PARAMETER Author
        The author of the module.
    
    .PARAMETER CompanyName
        The company that created the module.
    
    .PARAMETER FunctionsToExport
        A list of functions to export from the module.
    
    .PARAMETER Tags
        Tags for the module to aid in discovery.
    
    .EXAMPLE
        New-BuilderModule -Name MyModule -Description "My awesome module"
    
    .EXAMPLE
        New-BuilderModule -Name MyModule -Version "1.0.0" -Author "John Doe" -Tags "Utility", "DevOps"
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$Name,
        
        [Parameter()]
        [string]$Path = (Get-Location).Path,
        
        [Parameter()]
        [string]$Version = "0.1.0",
        
        [Parameter(Mandatory = $true)]
        [string]$Description,
        
        [Parameter()]
        [string]$Author = "",
        
        [Parameter()]
        [string]$CompanyName = "",
        
        [Parameter()]
        [string[]]$FunctionsToExport = @(),
        
        [Parameter()]
        [string[]]$Tags = @()
    )
    
    begin {
        Write-Verbose "Creating new PowerShell module: $Name"
    }
    
    process {
        try {
            # Get the module manifest generator
            $moduleGenerator = [PSBuild.DependencyInjection.ServiceLocator]::GetModuleManifestGenerator()
            
            # Create the module path
            $modulePath = Join-Path -Path $Path -ChildPath $Name
            
            # Create the module structure
            $result = $moduleGenerator.CreateModuleStructure(
                $modulePath,
                $Name,
                $Version,
                $Description
            )
            
            # Update the module manifest with additional information
            if ($Author -or $CompanyName -or $FunctionsToExport.Count -gt 0 -or $Tags.Count -gt 0) {
                $moduleGenerator.CreateModuleManifest(
                    $modulePath,
                    $Name,
                    $Version,
                    $Description,
                    $Author,
                    $CompanyName,
                    "",
                    $null,
                    $FunctionsToExport,
                    $null,
                    $null,
                    $null,
                    $null,
                    $null,
                    $Tags
                )
            }
            
            Write-Verbose "Module created at $modulePath"
            return Get-Item -Path $modulePath
        }
        catch {
            Write-Error "Error creating PowerShell module: $_"
            throw
        }
    }
}

function New-BuilderModuleFunction {
    <#
    .SYNOPSIS
        Creates a new function in a PowerShell module.
    
    .DESCRIPTION
        Creates a new function in a PowerShell module with the necessary structure and documentation.
    
    .PARAMETER ModulePath
        The path to the module directory.
    
    .PARAMETER Name
        The name of the function.
    
    .PARAMETER Description
        A description of the function.
    
    .PARAMETER Parameters
        A hashtable of parameter names and types.
    
    .PARAMETER IsPublic
        Whether the function is public or private.
    
    .EXAMPLE
        New-BuilderModuleFunction -ModulePath .\MyModule -Name Get-Something -Description "Gets something"
    
    .EXAMPLE
        New-BuilderModuleFunction -ModulePath .\MyModule -Name Set-Something -Description "Sets something" -Parameters @{Name="string"; Value="int"} -IsPublic $true
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$ModulePath,
        
        [Parameter(Mandatory = $true, Position = 1)]
        [string]$Name,
        
        [Parameter(Mandatory = $true)]
        [string]$Description,
        
        [Parameter()]
        [hashtable]$Parameters = @{},
        
        [Parameter()]
        [bool]$IsPublic = $true
    )
    
    begin {
        Write-Verbose "Creating new function: $Name"
    }
    
    process {
        try {
            # Get the module manifest generator
            $moduleGenerator = [PSBuild.DependencyInjection.ServiceLocator]::GetModuleManifestGenerator()
            
            # Convert the hashtable to a Dictionary<string, string>
            $paramDict = New-Object 'System.Collections.Generic.Dictionary[string,string]'
            foreach ($key in $Parameters.Keys) {
                $paramDict.Add($key, $Parameters[$key])
            }
            
            # Create the function template
            $functionContent = $moduleGenerator.CreateFunctionTemplate(
                $Name,
                $Description,
                $paramDict
            )
            
            # Add the function to the module
            $result = $moduleGenerator.AddFunction(
                $ModulePath,
                $Name,
                $functionContent,
                $IsPublic
            )
            
            Write-Verbose "Function created at $result"
            return Get-Item -Path $result
        }
        catch {
            Write-Error "Error creating function: $_"
            throw
        }
    }
}

function New-BuilderPowerShellModule {
    <#
    .SYNOPSIS
        Creates a new PowerShell module using Builder.
    
    .DESCRIPTION
        Creates a new PowerShell module with the necessary structure and files, including a module manifest,
        script module file, and basic folder structure for public and private functions.
    
    .PARAMETER ModulePath
        The path where the module will be created. If not provided, it will use the current directory.
    
    .PARAMETER ModuleName
        The name of the module to create.
    
    .PARAMETER ModuleVersion
        The version of the module. If not provided, it will default to 0.1.0.
    
    .PARAMETER Description
        A description of the module.
    
    .PARAMETER Author
        The author of the module. If not provided, it will use the current user.
    
    .PARAMETER CompanyName
        The company name. If not provided, it will use the author name.
    
    .PARAMETER Functions
        A hashtable of function names and their content. Each function will be created as a .ps1 file
        in the public folder of the module.
    
    .PARAMETER ProjectUri
        The URI to the project repository.
    
    .PARAMETER LicenseUri
        The URI to the module's license.
    
    .PARAMETER Tags
        An array of tags for the module to aid in discovery.
    
    .EXAMPLE
        New-BuilderPowerShellModule -ModuleName "MyModule" -Description "A module for doing things" -Author "John Doe"
    
    .EXAMPLE
        New-BuilderPowerShellModule -ModulePath "C:\Modules" -ModuleName "AwesomeModule" -ModuleVersion "1.0.0" -Description "An awesome module"
    #>
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)]
        [string]$ModulePath = (Get-Location).Path,
        
        [Parameter(Mandatory = $true, Position = 1)]
        [string]$ModuleName,
        
        [Parameter(Position = 2)]
        [string]$ModuleVersion = "0.1.0",
        
        [Parameter(Mandatory = $true, Position = 3)]
        [string]$Description,
        
        [Parameter()]
        [string]$Author = $env:USERNAME,
        
        [Parameter()]
        [string]$CompanyName,
        
        [Parameter()]
        [hashtable]$Functions,
        
        [Parameter()]
        [string]$ProjectUri,
        
        [Parameter()]
        [string]$LicenseUri,
        
        [Parameter()]
        [string[]]$Tags
    )
    
    begin {
        Write-Verbose "Starting PowerShell module creation"
        
        # Ensure Builder is initialized
        if (-not $Global:BuilderConfig) {
            Write-Verbose "Builder environment not initialized, initializing now"
            $Global:BuilderConfig = Initialize-BuilderEnvironment
        }
    }
    
    process {
        try {
            # Convert hashtable to dictionary for .NET method
            $functionDictionary = $null
            if ($Functions) {
                $functionDictionary = New-Object 'System.Collections.Generic.Dictionary[string,string]'
                foreach ($key in $Functions.Keys) {
                    $functionDictionary.Add($key, $Functions[$key])
                }
            }
            
            # Use the default company name if not provided
            if (-not $CompanyName) {
                $CompanyName = $Author
            }
            
            # Convert tags to .NET array if provided
            $tagArray = $null
            if ($Tags) {
                $tagArray = $Tags
            }
            
            # Get the ReleaseManager
            $releaseManager = [PSBuild.DependencyInjection.ServiceLocator]::GetReleaseManager()
            
            # Create the module
            $modulePath = $releaseManager.CreatePowerShellModule(
                $ModulePath,
                $ModuleName,
                $ModuleVersion,
                $Description,
                $Author,
                $CompanyName,
                $functionDictionary,
                $ProjectUri,
                $LicenseUri,
                $tagArray
            )
            
            if ($modulePath) {
                Write-Verbose "PowerShell module created successfully at $modulePath"
                return $modulePath
            }
            else {
                Write-Error "Failed to create PowerShell module"
                return $null
            }
        }
        catch {
            Write-Error "Error creating PowerShell module: $_"
            throw
        }
    }
}

function New-BuilderPowerShellFunction {
    <#
    .SYNOPSIS
        Creates a new PowerShell function in a module.
    
    .DESCRIPTION
        Creates a new PowerShell function and adds it to an existing module.
        The function will be created as a .ps1 file in the appropriate folder.
    
    .PARAMETER ModulePath
        The path to the module where the function will be added.
    
    .PARAMETER FunctionName
        The name of the function to create.
    
    .PARAMETER Description
        A description of the function.
    
    .PARAMETER Parameters
        A hashtable of parameter names and their descriptions.
    
    .PARAMETER IsPublic
        Whether the function should be public (exported) or private.
    
    .EXAMPLE
        New-BuilderPowerShellFunction -ModulePath "C:\Modules\MyModule" -FunctionName "Get-Something" -Description "Gets something"
    
    .EXAMPLE
        New-BuilderPowerShellFunction -ModulePath ".\MyModule" -FunctionName "Set-Setting" -Description "Sets a setting" -Parameters @{Name="The name"; Value="The value"}
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$ModulePath,
        
        [Parameter(Mandatory = $true, Position = 1)]
        [string]$FunctionName,
        
        [Parameter(Mandatory = $true, Position = 2)]
        [string]$Description,
        
        [Parameter()]
        [hashtable]$Parameters,
        
        [Parameter()]
        [switch]$IsPublic = $true
    )
    
    begin {
        Write-Verbose "Starting PowerShell function creation"
        
        # Ensure Builder is initialized
        if (-not $Global:BuilderConfig) {
            Write-Verbose "Builder environment not initialized, initializing now"
            $Global:BuilderConfig = Initialize-BuilderEnvironment
        }
    }
    
    process {
        try {
            # Convert hashtable to dictionary for .NET method
            $paramDictionary = $null
            if ($Parameters) {
                $paramDictionary = New-Object 'System.Collections.Generic.Dictionary[string,string]'
                foreach ($key in $Parameters.Keys) {
                    $paramDictionary.Add($key, $Parameters[$key])
                }
            }
            
            # Get the ReleaseManager
            $releaseManager = [PSBuild.DependencyInjection.ServiceLocator]::GetReleaseManager()
            
            # Create the function
            $functionContent = $releaseManager.CreatePowerShellFunction(
                $ModulePath,
                $FunctionName,
                $Description,
                $paramDictionary,
                $IsPublic
            )
            
            if ($functionContent) {
                Write-Verbose "PowerShell function created successfully"
                return $functionContent
            }
            else {
                Write-Error "Failed to create PowerShell function"
                return $null
            }
        }
        catch {
            Write-Error "Error creating PowerShell function: $_"
            throw
        }
    }
}

function Update-BuilderModuleVersion {
    <#
    .SYNOPSIS
        Updates the version in a PowerShell module.
    
    .DESCRIPTION
        Updates the version in a PowerShell module manifest and related files.
        This cmdlet can also update changelog files if they exist.
    
    .PARAMETER ModulePath
        The path to the PowerShell module.
    
    .PARAMETER Version
        The new version to set.
    
    .PARAMETER UpdateChangelog
        Whether to update the changelog with the new version information.
    
    .EXAMPLE
        Update-BuilderModuleVersion -ModulePath "C:\Modules\MyModule" -Version "1.2.0"
    
    .EXAMPLE
        Update-BuilderModuleVersion -ModulePath ".\MyModule" -Version "2.0.0-preview" -UpdateChangelog
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$ModulePath,
        
        [Parameter(Mandatory = $true, Position = 1)]
        [string]$Version,
        
        [Parameter()]
        [switch]$UpdateChangelog = $true
    )
    
    begin {
        Write-Verbose "Starting PowerShell module version update"
        
        # Ensure Builder is initialized
        if (-not $Global:BuilderConfig) {
            Write-Verbose "Builder environment not initialized, initializing now"
            $Global:BuilderConfig = Initialize-BuilderEnvironment
        }
    }
    
    process {
        try {
            # Get the VersionManager
            $versionManager = [PSBuild.DependencyInjection.ServiceLocator]::GetVersionManager()
            
            # Update the module version
            $success = $versionManager.UpdatePowerShellModuleVersion($ModulePath, $Version, $UpdateChangelog)
            
            if ($success) {
                Write-Verbose "PowerShell module version updated successfully to $Version"
                return $true
            }
            else {
                Write-Error "Failed to update PowerShell module version"
                return $false
            }
        }
        catch {
            Write-Error "Error updating PowerShell module version: $_"
            throw
        }
    }
}

# Export the public functions
Export-ModuleMember -Function Initialize-BuilderEnvironment, 
                              Invoke-Builder, 
                              New-BuilderRelease, 
                              Get-BuilderVersion, 
                              Update-BuilderVersion, 
                              New-BuilderModule, 
                              New-BuilderModuleFunction, 
                              New-BuilderPowerShellModule, 
                              New-BuilderPowerShellFunction, 
                              Update-BuilderModuleVersion 