function Open-MsiDatabase {
    param([string]$PackagePath)

    $installer = New-Object -ComObject WindowsInstaller.Installer
    return $installer.OpenDatabase($PackagePath, 0)
}

function Get-MsiQueryRows {
    param(
        [object]$Database,
        [string]$Query,
        [int]$ColumnCount
    )

    $view = $Database.OpenView($Query)
    try {
        $null = $view.Execute()
        while ($record = $view.Fetch()) {
            $row = @()
            for ($column = 1; $column -le $ColumnCount; $column++) {
                $row += $record.StringData($column)
            }
            Write-Output ([pscustomobject]@{ Values = $row })
        }
    }
    finally {
        $null = $view.Close()
    }
}

function Get-MsiProperty {
    param(
        [object]$Database,
        [string]$Name
    )

    $rows = @(Get-MsiQueryRows $Database "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$Name'" 1)
    if ($rows.Count -eq 0) {
        return $null
    }
    return $rows[0].Values[0]
}

function Get-MsiPropertyFromPackage {
    param(
        [string]$PackagePath,
        [string]$Name
    )

    $database = Open-MsiDatabase $PackagePath
    return Get-MsiProperty $database $Name
}
