param(
    [Parameter(Mandatory=$true)]
    [string]
    $MigrationName
)

$startupProject = "./FluentCMS.Repositories.Postgres.csproj"
$project = "./FluentCMS.Repositories.Postgres.csproj"
$context = "PostgresDbContext"



try {

    # Save the current directory
    $originalDir = Get-Location

    # Set the scripts directory as the current directory
    Set-Location -Path $PSScriptRoot

    dotnet tool update dotnet-ef -g

    # Generate migrations
    $output = dotnet ef migrations add $MigrationName -o ./Migrations --context $context --startup-project $startupProject --project $project
    Write-Output "Migrations generated. Output was: `n$output"

    # Generate SQL script from migrations
    #$output = dotnet ef migrations script --project $project --context $context --output ./Data/sql/migrations.sql --idempotent
    #Write-Output "Migration SQL script generated. Output was: `n$output"


    # Restore the original directory
    Set-Location -Path $originalDir
} catch {
    Write-Output "Command failed. Error was: `n$_"
}

